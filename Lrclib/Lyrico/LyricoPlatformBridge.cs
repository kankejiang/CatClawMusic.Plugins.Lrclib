using System.Globalization;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 宿主 API（globalThis.Platform）到 CLR 的桥。
/// 按 Lyrico 官方宿主 API 参考 https://replica0110.github.io/Lyrico/plugins/host-api.html 实现，
/// 覆盖 5 个内嵌源（netease/qq/kugou/soda/apple）实际用到的子模块：
/// http / crypto / base64 / bytes / compression / xml / cache / log / runtime / app。
/// </summary>
public sealed class LyricoPlatform
{
    public LyricoHttp http { get; }
    public LyricoCrypto crypto { get; }
    public LyricoBase64 base64 { get; }
    public LyricoBytes bytes { get; }
    public LyricoCompression compression { get; }
    public LyricoXml xml { get; }
    public LyricoCache cache { get; }
    public LyricoLogBridge log { get; }
    public LyricoRuntime runtime { get; }
    public LyricoApp app { get; }

    public LyricoPlatform(Engine engine)
    {
        http = new LyricoHttp(engine);
        crypto = new LyricoCrypto();
        base64 = new LyricoBase64(engine);
        bytes = new LyricoBytes(engine);
        compression = new LyricoCompression(engine);
        xml = new LyricoXml(engine);
        cache = new LyricoCache();
        log = new LyricoLogBridge();
        runtime = new LyricoRuntime(engine);
        app = new LyricoApp(engine);
    }
}

/// <summary>把 CLR 值转成 Jint JsValue，确保 List&lt;string&gt; 变成真正的 JS 数组（Set-Cookie 等需要 Array.isArray）。</summary>
internal static class LyricoJs
{
    public static JsValue FromClr(Engine engine, object? value)
    {
        switch (value)
        {
            case null: return JsValue.Null;
            case string s: return new JsString(s);
            case bool b: return b ? JsBoolean.True : JsBoolean.False;
            case int i: return JsValue.FromObject(engine, i);
            case long l: return JsValue.FromObject(engine, l);
            case double d: return JsValue.FromObject(engine, d);
            case byte bt: return JsValue.FromObject(engine, (int)bt);
            case List<string> strs:
                var arr0 = new JsArray(engine);
                for (int i = 0; i < strs.Count; i++) arr0.Set((uint)i, new JsString(strs[i]), false);
                return arr0;
            case Dictionary<string, object?> dict:
                var obj = new JsObject(engine);
                foreach (var (k, v) in dict) obj.Set(k, FromClr(engine, v), false);
                return obj;
            case object[] arrObj:
                var a1 = new JsArray(engine);
                for (int i = 0; i < arrObj.Length; i++) a1.Set((uint)i, FromClr(engine, arrObj[i]), false);
                return a1;
            default:
                return JsValue.FromObject(engine, value);
        }
    }

    /// <summary>构造 HTTP 响应对象 { code, message, headers, body, bodyBase64 }。</summary>
    public static JsValue HttpResponse(Engine engine, int code, string message, Dictionary<string, object?> headers, string body, string bodyBase64)
    {
        var resp = new JsObject(engine);
        resp.Set("code", JsValue.FromObject(engine, code), false);
        resp.Set("message", JsValue.FromObject(engine, message), false);
        resp.Set("headers", FromClr(engine, headers), false);
        resp.Set("body", JsValue.FromObject(engine, body), false);
        resp.Set("bodyBase64", JsValue.FromObject(engine, bodyBase64), false);
        return resp;
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  http
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Platform.http：getText/postText（旧 API 返回字符串）与 get/post/postBytesResponse（新 API 返回响应对象）。</summary>
public sealed class LyricoHttp
{
    private static readonly HttpClient Client = CreateClient();
    private readonly Engine _engine;
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Lyrico/1.0";

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public LyricoHttp(Engine engine) { _engine = engine; }

    public string getText(string url, JsValue options)
    {
        var (req, _) = BuildRequest(url, "GET", null, options);
        using var resp = Client.Send(req, HttpCompletionOption.ResponseContentRead);
        return new StreamReader(resp.Content.ReadAsStream(), Encoding.UTF8).ReadToEnd();
    }

    public string postText(string url, string body, JsValue options)
    {
        var (req, ct) = BuildRequest(url, "POST", body, options);
        if (ct != null) req.Content = new StringContent(body, Encoding.UTF8, MediaTypeOnly(ct));
        using var resp = Client.Send(req);
        return new StreamReader(resp.Content.ReadAsStream(), Encoding.UTF8).ReadToEnd();
    }

    public JsValue get(string url, JsValue options) => SendJson(url, "GET", null, options);

    public JsValue post(string url, JsValue body, JsValue options)
        => SendJson(url, "POST", BodyAsString(body), options);

    /// <summary>二进制响应（netease EAPI）：body 可能是字符串表单，返回 bodyBase64 + 响应头（含 Set-Cookie）。</summary>
    public JsValue postBytesResponse(string url, string body, JsValue options)
        => SendJson(url, "POST", body, options);

    private JsValue SendJson(string url, string method, string? body, JsValue options)
    {
        var (req, ct) = BuildRequest(url, method, body, options);
        if (ct != null && body != null) req.Content = new StringContent(body, Encoding.UTF8, MediaTypeOnly(ct));
        try
        {
            using var resp = Client.Send(req);
            var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var bodyText = TryUtf8(bytes);
            var headers = ReadHeaders(resp);
            return LyricoJs.HttpResponse(_engine, (int)resp.StatusCode, resp.ReasonPhrase ?? "",
                headers, bodyText, Convert.ToBase64String(bytes));
        }
        catch (Exception ex)
        {
            return LyricoJs.HttpResponse(_engine, 0, ex.Message, new Dictionary<string, object?>(), "", "");
        }
    }

    /// <summary>构造请求。返回 (request, contentType)。</summary>
    private static (HttpRequestMessage, string?) BuildRequest(string url, string method, string? body, JsValue options)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

        string? contentType = null;
        int connectTimeoutMs = 8000, readTimeoutMs = 12000;
        bool followRedirects = true;

        if (options.IsObject())
        {
            var o = options.AsObject();
            var ct = o.Get("contentType");
            if (ct.IsString()) contentType = ct.AsString();

            var h = o.Get("headers");
            if (h.IsObject())
                foreach (var (k, v) in EnumerateObject(h.AsObject()))
                    if (v.IsString() && !string.IsNullOrEmpty(k))
                        ApplyHeader(req, k, v.AsString());

            var connect = o.Get("connectTimeoutMs");
            if (connect.IsNumber()) connectTimeoutMs = (int)connect.AsNumber();
            var read = o.Get("readTimeoutMs");
            if (read.IsNumber()) readTimeoutMs = (int)read.AsNumber();
            var follow = o.Get("followRedirects");
            if (follow.IsBoolean()) followRedirects = follow.AsBoolean();
        }

        // 连接/读取超时合计作为本请求 CancellationToken 超时
        var totalMs = Math.Max(1000, connectTimeoutMs + readTimeoutMs);
        var cts = new CancellationTokenSource(totalMs);
        req.Options.Set(new HttpRequestOptionsKey<CancellationToken>("request-cancellation"), cts.Token);
        if (!followRedirects) { req.Options.Set(new HttpRequestOptionsKey<bool>("follow-redirects"), false); }

        return (req, contentType);
    }

    private static void ApplyHeader(HttpRequestMessage req, string name, string value)
    {
        if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase)) return; // 由 HttpContent 管理
        // content-length 等特殊头忽略
        if (name.Equals("content-length", StringComparison.OrdinalIgnoreCase)) return;
        try { req.Headers.TryAddWithoutValidation(name, value); }
        catch { }
    }

    private static string BodyAsString(JsValue body)
    {
        if (body.IsString()) return body.AsString();
        if (body.IsObject() || body.IsArray())
        {
            var j = System.Text.Json.JsonSerializer.Serialize(JsToClr(body));
            return j;
        }
        return body.IsNull() ? "" : body.ToString();
    }

    /// <summary>StringContent 的 mediaType 拒收 "; charset=..." 后缀，取其纯 media type。</summary>
    private static string MediaTypeOnly(string contentType)
    {
        var semi = contentType.IndexOf(';');
        return (semi >= 0 ? contentType.Substring(0, semi) : contentType).Trim();
    }

    private static Dictionary<string, object?> ReadHeaders(HttpResponseMessage resp)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var setCookies = new List<string>();

        void Add(string key, string value)
        {
            if (key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
            {
                setCookies.Add(value);
                return;
            }
            var canonical = TitleKey(key);
            dict[canonical] = dict.TryGetValue(canonical, out var existing) && existing is string s
                ? s + "," + value : value;
        }

        foreach (var h in resp.Headers) if (h.Value != null) foreach (var v in h.Value) Add(h.Key, v);
        foreach (var h in resp.Content.Headers) if (h.Value != null) foreach (var v in h.Value) Add(h.Key, v);

        if (setCookies.Count > 0) dict["Set-Cookie"] = setCookies;
        return dict;
    }

    private static string TitleKey(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

    private static string TryUtf8(byte[] bytes)
    {
        try { return new UTF8Encoding(false, false).GetString(bytes); }
        catch { return ""; }
    }

    internal static IEnumerable<KeyValuePair<string, JsValue>> EnumerateObject(ObjectInstance obj)
    {
        foreach (var p in obj.GetOwnProperties())
        {
            var key = p.Key.ToString();
            if (!string.IsNullOrEmpty(key)) yield return new KeyValuePair<string, JsValue>(key, p.Value.Value);
        }
    }

    internal static object? JsToClr(JsValue v) => v switch
    {
        _ when v.IsString() => v.AsString(),
        _ when v.IsNumber() => IsWholeNumber(v.AsNumber()) ? (object)(int)v.AsNumber() : v.AsNumber(),
        _ when v.IsBoolean() => v.AsBoolean(),
        _ when v.IsNull() || v.IsUndefined() => null,
        _ when v.IsArray() => EnumerateObject(v.AsObject()).Select(x => JsToClr(x.Value)).Where(x => x != null).ToList(),
        _ when v.IsObject() => EnumerateObject(v.AsObject()).ToDictionary(x => x.Key, x => JsToClr(x.Value))!,
        _ => v.ToString(),
    };

    private static bool IsWholeNumber(double n) => n == Math.Floor(n) && n >= int.MinValue && n <= int.MaxValue;
}

// ──────────────────────────────────────────────────────────────────────────
//  crypto
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Platform.crypto：md5 + AES-ECB-PKCS5 加解密（netease/eapi 需要）。</summary>
public sealed class LyricoCrypto
{
    public string md5(string text)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public string aesEcbPkcs5EncryptHex(string text, string key)
        => Convert.ToHexString(AesEcb(Encoding.UTF8.GetBytes(text ?? ""), KeyBytes(key), true)).ToLowerInvariant();

    public string aesEcbPkcs5EncryptBase64(string text, string key)
        => Convert.ToBase64String(AesEcb(Encoding.UTF8.GetBytes(text ?? ""), KeyBytes(key), true));

    public string aesEcbPkcs5DecryptBase64ToText(string base64, string key)
    {
        var data = Convert.FromBase64String(base64 ?? "");
        var plain = AesEcb(data, KeyBytes(key), false);
        return Encoding.UTF8.GetString(plain).TrimEnd('\0');
    }

    private static byte[] KeyBytes(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key ?? "");
        return bytes.Length switch
        {
            16 or 24 or 32 => bytes,
            _ => FixKey(bytes),
        };
    }

    private static byte[] FixKey(byte[] k)
    {
        var target = k.Length >= 24 ? 24 : (k.Length > 16 ? 24 : 16);
        var result = new byte[target];
        Array.Copy(k, result, Math.Min(k.Length, target));
        return result;
    }

    private static byte[] AesEcb(byte[] data, byte[] key, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  base64
// ──────────────────────────────────────────────────────────────────────────

public sealed class LyricoBase64
{
    private readonly Engine _engine;
    public LyricoBase64(Engine engine) { _engine = engine; }

    public string encodeText(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
    public string decodeText(string base64)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64 ?? "")); }
        catch { return ""; }
    }

    public string encodeUrlText(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? "")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public string decodeUrlText(string base64Url)
    {
        try
        {
            var b = (base64Url ?? "").Replace('-', '+').Replace('_', '/');
            switch (b.Length % 4) { case 2: b += "=="; break; case 3: b += "="; break; }
            return Encoding.UTF8.GetString(Convert.FromBase64String(b));
        }
        catch { return ""; }
    }

    public string dropBytes(string base64, int count)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64 ?? "");
            if (count >= bytes.Length) return Convert.ToBase64String(Array.Empty<byte>());
            return Convert.ToBase64String(bytes.Skip(Math.Max(0, count)).ToArray());
        }
        catch { return ""; }
    }

    public JsValue encodeBytes(JsValue bytes) => FromClrJs(Convert.ToBase64String(ToByteArray(bytes)));
    public JsValue decodeBytes(JsValue b64)
    {
        var s = b64.IsString() ? b64.AsString() : (b64.IsNull() ? "" : b64.ToString());
        try { return new JsArray(_engine, Convert.FromBase64String(s).Select(x => (JsValue)JsValue.FromObject(_engine, (int)x)).ToArray()); }
        catch { return new JsArray(_engine); }
    }

    private JsValue FromClrJs(string s) => new JsString(s);

    internal static byte[] ToByteArray(JsValue bytes)
    {
        if (bytes.IsString()) return Encoding.UTF8.GetBytes(bytes.AsString());
        if (bytes.IsArray())
        {
            var arr = bytes.AsArray();
            var data = new byte[(int)arr.Length];
            for (uint i = 0; i < arr.Length; i++) data[i] = (byte)GetNumber(arr.Get(i));
            return data;
        }
        return Array.Empty<byte>();
    }

    internal static double GetNumber(JsValue v) => v.IsNumber() ? v.AsNumber() : 0;
}

// ──────────────────────────────────────────────────────────────────────────
//  bytes
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Platform.bytes：XOR（kugou KRC 解密用）。</summary>
public sealed class LyricoBytes
{
    private readonly Engine _engine;
    public LyricoBytes(Engine engine) { _engine = engine; }

    public string xorBase64(string base64, JsValue keyBytes)
    {
        var data = Convert.FromBase64String(base64 ?? "");
        var key = LyricoBase64.ToByteArray(keyBytes);
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(data[i] ^ key[i % Math.Max(1, key.Length)]);
        return Convert.ToBase64String(data);
    }

    public JsValue xor(JsValue inputBytes, JsValue keyBytes)
    {
        var data = LyricoBase64.ToByteArray(inputBytes);
        var key = LyricoBase64.ToByteArray(keyBytes);
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ key[i % Math.Max(1, key.Length)]);
        return new JsArray(_engine, result.Select(x => (JsValue)JsValue.FromObject(_engine, (int)x)).ToArray());
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  compression
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Platform.compression：inflate（krc/qrc 压缩歌词）。</summary>
public sealed class LyricoCompression
{
    private readonly Engine _engine;
    public LyricoCompression(Engine engine) { _engine = engine; }

    public string inflateBase64ToText(string base64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64 ?? ""); } catch { return ""; }
        return Utf8(Inflate(bytes));
    }

    public string inflateBytesToText(JsValue bytes)
        => Utf8(Inflate(LyricoBase64.ToByteArray(bytes)));

    private static string Utf8(byte[] data)
    {
        if (data.Length == 0) return "";
        try { return Encoding.UTF8.GetString(data); } catch { return ""; }
    }

    /// <summary>zlib 优先、raw deflate 兜底（兼容两种压缩头）。</summary>
    internal static byte[] Inflate(byte[] data)
    {
        if (data == null || data.Length == 0) return Array.Empty<byte>();

        var z = TryInflate(data, t => new ZLibStream(t, CompressionMode.Decompress));
        if (z.Length > 0) return z;
        return TryInflate(data, t => new DeflateStream(t, CompressionMode.Decompress));
    }

    private static byte[] TryInflate(byte[] data, Func<Stream, Stream> open)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var dec = open(ms);
            using var outMs = new MemoryStream();
            dec.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch { return Array.Empty<byte>(); }
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  cache
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Platform.cache：内存缓存（netease 匿名登录态、apple token）。</summary>
public sealed class LyricoCache
{
    private sealed record Entry(string? Value, long ExpireTicks);
    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public JsValue get(string key)
    {
        if (!_store.TryGetValue(key, out var e)) return JsValue.Undefined;
        if (e.ExpireTicks > 0 && DateTime.UtcNow.Ticks > e.ExpireTicks) { _store.TryRemove(key, out _); return JsValue.Undefined; }
        return e.Value is null ? JsValue.Undefined : new JsString(e.Value);
    }

    public void set(string key, string value) => _store[key] = new Entry(value, 0);
    public void set(string key, string value, double ttlMs)
        => _store[key] = new Entry(value, ttlMs > 0 ? DateTime.UtcNow.AddMilliseconds(ttlMs).Ticks : 0);

    public void remove(string key) => _store.TryRemove(key, out _);
    public void clear() => _store.Clear();
}

// ──────────────────────────────────────────────────────────────────────────
//  log
// ──────────────────────────────────────────────────────────────────────────

public sealed class LyricoLogBridge
{
    public void debug(string tag, string message) => LyricoLogOut.Debug(tag, message);
    public void debug(string message) => LyricoLogOut.Debug("Lyrico", message);
    public void warn(string tag, string message) => LyricoLogOut.Warn(tag, message);
    public void warn(string message) => LyricoLogOut.Warn("Lyrico", message);
    public void error(string tag, string message) => LyricoLogOut.Warn(tag, message);
    public void error(string message) => LyricoLogOut.Warn("Lyrico", message);
}

internal static class LyricoLogOut
{
    public static void Debug(string tag, string message) => LyricoLog.Debug(tag, message);
    public static void Warn(string tag, string message) => LyricoLog.Warn(tag, message);
}

// ──────────────────────────────────────────────────────────────────────────
//  runtime / app
// ──────────────────────────────────────────────────────────────────────────

public sealed class LyricoRuntime
{
    private readonly Engine _engine;
    public LyricoRuntime(Engine engine) { _engine = engine; }

    public JsValue getInfo()
    {
        var obj = new JsObject(_engine);
        obj.Set("pluginApiVersion", JsValue.FromObject(_engine, 4), false);
        obj.Set("hostApiVersion", JsValue.FromObject(_engine, 3), false);
        obj.Set("engine", new JsString("quickjs"), false);
        obj.Set("engineVersion", new JsString(""), false);
        var apis = new JsArray(_engine);
        foreach (var a in SupportedHostApis) apis.Set((uint)apis.Get("length").AsNumber(), new JsString(a), false);
        obj.Set("supportedHostApis", apis, false);
        return obj;
    }

    private static readonly string[] SupportedHostApis =
    {
        "http.getText", "http.postText", "http.postBytes", "http.get", "http.post",
        "http.getBytes", "http.postBytesResponse",
        "crypto.md5", "crypto.aesEcbPkcs5EncryptHex", "crypto.aesEcbPkcs5DecryptBase64ToText",
        "base64.encodeText", "base64.decodeText", "base64.encodeBytes", "base64.decodeBytes",
        "base64.encodeUrlText", "base64.decodeUrlText", "base64.dropBytes",
        "bytes.xor", "bytes.xorBase64",
        "compression.inflateBytesToText", "compression.inflateBase64ToText",
        "cache.get", "cache.set", "cache.remove", "cache.clear",
        "log.debug", "log.warn", "log.error",
        "xml.getRootAttributes", "xml.findElements", "xml.replaceChildrenByAttr", "xml.removeElements",
        "runtime.getInfo", "app.getUserAgent",
    };
}

public sealed class LyricoApp
{
    private readonly Engine _engine;
    public LyricoApp(Engine engine) { _engine = engine; }

    public string getUserAgent() => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 LyricoPlugins/1.0";

    public JsValue getInfo()
    {
        var obj = new JsObject(_engine);
        obj.Set("name", new JsString("CatClawMusic"), false);
        obj.Set("versionName", new JsString("1.0.0"), false);
        return obj;
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  xml（仅 apple 官方歌词本地化用到；尽力实现）
// ──────────────────────────────────────────────────────────────────────────

public sealed class LyricoXml
{
    private readonly Engine _engine;
    public LyricoXml(Engine engine) { _engine = engine; }

    public JsValue getRootAttributes(string xml)
    {
        var attrs = new Dictionary<string, object?>();
        try
        {
            var doc = XDocument.Parse((xml ?? "").Trim());
            var root = doc.Root;
            if (root != null)
                foreach (var a in root.Attributes())
                    attrs[PrefixedName(a)] = a.Value;
        }
        catch { }
        return LyricoJs.FromClr(_engine, attrs);
    }

    public JsValue findElements(string xml, JsValue options)
    {
        var list = new List<object?>();
        try
        {
            var (tag, attrMatch) = ParseOptions(options);
            var doc = XDocument.Parse((xml ?? "").Trim());
            var root = doc.Root;
            if (root == null) return new JsArray(_engine);
            foreach (var el in root.DescendantsAndSelf())
            {
                if (!string.Equals(el.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase)) continue;
                if (attrMatch.Count > 0 && !MatchAttrs(el, attrMatch)) continue;
                list.Add(ElementToDict(el));
            }
        }
        catch { }
        return LyricoJs.FromClr(_engine, list);
    }

    public string removeElements(string xml, JsValue options)
    {
        try
        {
            var (tag, attrMatch) = ParseOptions(options);
            var doc = XDocument.Parse((xml ?? "").Trim());
            if (doc.Root == null) return xml;
            foreach (var el in doc.Root.DescendantsAndSelf().ToList())
            {
                if (!string.Equals(el.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase)) continue;
                if (attrMatch.Count == 0 || MatchAttrs(el, attrMatch)) el.Remove();
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }
        catch { return xml; }
    }

    public string replaceChildrenByAttr(string xml, JsValue options)
    {
        try
        {
            var o = options.AsObject();
            var targetTag = Str(o.Get("targetTag"), "p");
            var keyAttr = Str(o.Get("keyAttr"), "itunes:key").Split(':').Last();
            var rootAttrs = GetDict(o.Get("rootAttributes"));
            var replacementsJs = o.Get("replacements");
            var replacements = new Dictionary<string, (bool IsXml, string Value)>();
            if (replacementsJs.IsObject())
                foreach (var (k, v) in LyricoHttp.EnumerateObject(replacementsJs.AsObject()))
                {
                    if (!v.IsObject()) { replacements[k] = (false, Str(v, "")); continue; }
                    var mode = Str(v.AsObject().Get("mode"), "text");
                    replacements[k] = (mode == "xml", Str(v.AsObject().Get("value"), ""));
                }

            var doc = XDocument.Parse((xml ?? "").Trim());
            if (doc.Root == null) return xml;
            foreach (var el in doc.Root.DescendantsAndSelf().ToList())
            {
                if (!string.Equals(el.Name.LocalName, targetTag, StringComparison.OrdinalIgnoreCase)) continue;
                var keyVal = el.Attributes().FirstOrDefault(a => a.Name.LocalName == keyAttr)?.Value;
                if (keyVal != null && replacements.TryGetValue(keyVal, out var rep))
                {
                    el.RemoveNodes();
                    if (rep.IsXml && !string.IsNullOrEmpty(rep.Value))
                    {
                        foreach (var frag in XElement.Parse("<r>" + rep.Value + "</r>").Nodes()) el.Add(frag);
                    }
                    else
                    {
                        el.SetValue(rep.Value);
                    }
                }
            }
            if (rootAttrs.Count > 0)
                foreach (var (k, v) in rootAttrs)
                {
                    var existing = doc.Root.Attributes().FirstOrDefault(a =>
                        a.Name.LocalName == k.Split(':').Last() || (a.IsNamespaceDeclaration ? false : a.Name.LocalName == k));
                    if (existing != null) existing.Value = v;
                    else doc.Root.SetAttributeValue(k, v);
                }
            return doc.ToString(SaveOptions.DisableFormatting);
        }
        catch { return xml; }
    }

    private static (string tag, Dictionary<string, string> attrMatch) ParseOptions(JsValue options)
    {
        var tag = "p";
        var attrMatch = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!options.IsObject()) return (tag, attrMatch);
        var o = options.AsObject();
        tag = Str(o.Get("tag"), tag);
        if (string.IsNullOrEmpty(tag)) tag = Str(o.Get("targetTag"), "p");
        var attrs = o.Get("attrs");
        if (attrs.IsObject())
        {
            attrMatch = GetDict(attrs);
        }
        else if (!attrs.IsUndefined() && !attrs.IsNull())
        {
            // 兼容单 attr 写法 {key,value}
            var key = Str(o.Get("key"), "");
            var value = Str(o.Get("value"), "");
            if (!string.IsNullOrEmpty(key)) attrMatch[key] = value;
        }
        return (tag, attrMatch);
    }

    private static Dictionary<string, string> GetDict(JsValue v)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (v.IsObject())
            foreach (var (k, val) in LyricoHttp.EnumerateObject(v.AsObject()))
                if (val.IsString()) result[k] = val.AsString();
        return result;
    }

    private static bool MatchAttrs(XElement el, Dictionary<string, string> attrMatch)
    {
        foreach (var (name, value) in attrMatch)
        {
            var target = name.Split(':').Last();
            var a = el.Attributes().FirstOrDefault(x => x.Name.LocalName == target);
            if (a == null || !string.Equals(a.Value, value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string Str(JsValue v, string def)
    {
        if (v.IsString()) return v.AsString();
        if (v.IsNumber()) return v.AsNumber().ToString(CultureInfo.InvariantCulture);
        return def;
    }

    private static string PrefixedName(XAttribute a)
    {
        var raw = a.Name.NamespaceName.Length > 0 ? a.Name.NamespaceName + ":" + a.Name.LocalName : a.Name.LocalName;
        return raw;
    }

    private static Dictionary<string, object?> ElementToDict(XElement el)
    {
        var dict = new Dictionary<string, object?>();
        dict["tag"] = el.Name.LocalName;
        var attrs = new Dictionary<string, object?>();
        foreach (var a in el.Attributes()) attrs[PrefixedName(a)] = a.Value;
        dict["attrs"] = attrs;
        dict["text"] = el.Value;
        dict["innerXml"] = string.Concat(el.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        var children = el.Elements().Select(ElementToDict).ToList<object?>();
        dict["children"] = children;
        return dict;
    }
}