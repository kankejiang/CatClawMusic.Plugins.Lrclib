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