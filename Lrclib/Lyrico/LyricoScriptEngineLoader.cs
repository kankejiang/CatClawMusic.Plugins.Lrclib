using System.Reflection;
using System.Runtime.CompilerServices;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

// ──────────────────────────────────────────────────────────────────────────
//  Jint 引擎加载器：从嵌入资源加载 Jint.dll / Acornima.dll
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// 把嵌入资源的 Jint.dll / Acornima.dll 在首次需要时通过 AppDomain.AssemblyResolve
/// 加载进 AppDomain。插件 .ccp 是单 DLL、宿主不提供 Jint，故必须自加载。
/// <para>契约：插件类型不得继承/字段签名引用 Jint 类型——只能在方法体内引用，
/// 否则宿主 GetTypes() 阶段 JIT 解析类型时会早于本加载器注册而失败。</para>
/// </summary>
public static class LyricoScriptEngineLoader
{
    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jint"] = "Jint.dll",
        ["Acornima"] = "Acornima.dll",
    };

    private static int _registered;

    /// <summary>注册 AssemblyResolve 处理器（幂等）。由 ModuleInitializer 调用。</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0) return;
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    /// <summary>
    /// 主动把 Jint/Acornima 从嵌入资源装入 AppDomain（幂等）。
    /// 背景：ModuleInitializer→AssemblyResolve 的懒解析链在部分运行时（Mono/Android）
    /// 不可靠——若 JIT 在 resolve 事件注册前触发 Jint 类型解析会直接抛
    /// FileNotFoundException。此方法在任何 Jint 类型被引用前显式装载，
    /// 装入后 JIT 按已加载程序集解析，不再依赖 resolve 事件。
    /// </summary>
    public static void EnsureEngineLoaded()
    {
        Register();  // 兜底：若 ModuleInitializer 未触发，这里补注册

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var n = asm.GetName().Name;
            if (n != null) loaded.Add(n);
        }
        foreach (var (asmName, resName) in ByName)
        {
            if (loaded.Contains(asmName)) continue;
            LoadFromResource(resName);
        }
    }

    private static void LoadFromResource(string resName)
    {
        try
        {
            var asm = typeof(LyricoScriptEngineLoader).Assembly;
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return;
            using var ms = new MemoryStream((int)stream.Length);
            stream.CopyTo(ms);
            Assembly.Load(ms.ToArray());
        }
        catch
        {
            // 装载失败时交给 AssemblyResolve 兜底；最终失败会在宿主执行处显式抛出
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name ?? "";
        if (!ByName.TryGetValue(name, out var resName)) return null;
        var asm = typeof(LyricoScriptEngineLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream == null) return null;
        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        return Assembly.Load(ms.ToArray());
    }
}