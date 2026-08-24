using Jint;
using Jint.Native;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// 单个 Lyrico 源插件的脚本宿主：按 Lyrico 规范把 includeDirs 的 lib 脚本（按相对路径排序）
/// 拼到 entry 之前，注入 globalThis.Platform，在 Jint 中执行，然后调用全局 getLyrics(request)。
/// <para>Jint 引擎非线程安全：每次执行经 _lock 串行，且放在后台线程以免阻塞 UI。</para>
/// </summary>
public sealed class LyricoScriptHost
{
    private readonly object _initLock = new();
    private readonly SemaphoreSlim _execLock = new(1, 1);
    private readonly LyricoSourceCatalog _catalog;
    private readonly string _plugin;
    private readonly LyricoManifest _manifest;

    private Engine? _engine;
    private bool _loaded;
    private volatile string? _loadError;

    public LyricoScriptHost(LyricoSourceCatalog catalog, string plugin, LyricoManifest manifest)
    {
        _catalog = catalog;
        _plugin = plugin;
        _manifest = manifest;
    }

    public string PluginName => _plugin;
    public string DisplayName => _manifest.Name;
    public bool IsLoaded => _loaded;
    public string? LoadError => _loadError;

    /// <summary>（懒加载）拼接脚本并执行。并发安全。</summary>
    public bool EnsureLoaded()
    {
        if (_loaded) return true;
        lock (_initLock)
        {
            if (_loaded) return true;
            try
            {
                var script = BuildCompositeScript();
                if (script == null)
                {
                    _loadError = $"插件 {_plugin} 资源缺失（entry/manifest）";
                    return false;
                }

                var engine = new Engine(opts => opts
                    .LimitRecursion(5000)
                    .TimeoutInterval(TimeSpan.FromSeconds(10)));
                engine.Global["Platform"] = JsValue.FromObject(engine, new LyricoPlatform(engine));
                engine.Execute("var console={log:function(){},error:function(){},warn:function(){},info:function(){},debug:function(){},trace:function(){}};");
                engine.Execute(script);

                var hasGetLyrics = engine.Global.HasOwnProperty("getLyrics");
                if (!hasGetLyrics)
                {
                    _loadError = $"插件 {_plugin} 未声明 getLyrics";
                    return false;
                }
                _engine = engine;
                _loaded = true;
                return true;
            }
            catch (Exception ex)
            {
                _loadError = $"插件 {_plugin} 执行失败：{ex.Message}";
                LyricoLog.Warn(_plugin, _loadError);
                return false;
            }
        }
    }

    /// <summary>拼接 lib（includeDirs 内按相对路径排序）+ entry。</summary>
    private string? BuildCompositeScript()
    {
        var files = _catalog.GetScriptFiles(_plugin);
        if (files == null) return null;
        if (!files.TryGetValue(_manifest.Entry, out var entryCode)) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var dir in _manifest.IncludeDirs)
        {
            var normalized = dir.Trim('/');
            var prefix = normalized.Length == 0 ? "" : normalized + "/";
            var dirJs = files.Keys
                .Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && p.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (var path in dirJs)
            {
                sb.Append($"// Platform include: {path}\n");
                sb.Append(files[path]);
                sb.Append($"\n//# sourceURL={path}\n\n");
            }
        }

        sb.Append($"// Platform entry: {_manifest.Entry}\n");
        sb.Append(entryCode);
        return sb.ToString();
    }

    /// <summary>
    /// 触发 getLyrics(request)。返回歌词候选列表（CLR），失败返回空列表。
    /// request.song 不含平台 id → 各源内部先 searchSongs 再取词。
    /// </summary>
    public async Task<List<object?>> GetLyricsAsync(
        string title, string artist, string album, long durationMs, CancellationToken ct = default)
    {
        if (!EnsureLoaded()) return new List<object?>();
        var engine = _engine!;

        var song = new Dictionary<string, object?>
        {
            ["title"] = title ?? "",
            ["artist"] = artist ?? "",
            ["album"] = album ?? "",
            ["duration"] = durationMs,
        };
        var request = new Dictionary<string, object?>
        {
            ["song"] = song,
            ["page"] = 1,
            ["pageSize"] = 5,
            ["separator"] = "/",
            ["config"] = new Dictionary<string, object?>(),
        };

        await _execLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(() =>
            {
                var fn = engine.Global.Get("getLyrics");
                var reqJs = JsValue.FromObject(engine, request);
                var value = fn.Call(JsValue.Undefined, new JsValue[] { reqJs });
                return LyricoHttp.JsToClr(value);
            }, ct).ConfigureAwait(false);
            return result as List<object?> ?? new List<object?>();
        }
        catch (Exception ex)
        {
            _loadError = $"插件 {_plugin} getLyrics 异常：{ex.Message}";
            LyricoLog.Warn(_plugin, _loadError);
            return new List<object?>();
        }
        finally
        {
            _execLock.Release();
        }
    }

    /// <summary>释放引擎。宿主卸载时调用。</summary>
    public void Unload()
    {
        lock (_initLock)
        {
            _engine = null;
            _loaded = false;
            _loadError = null;
        }
    }
}