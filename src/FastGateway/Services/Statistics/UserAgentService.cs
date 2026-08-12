using MyCSharp.HttpUserAgentParser;

namespace FastGateway.Services.Statistics;

/// <summary>
///     User-Agent 解析（OS + 浏览器）。仅由统计后台消费者单线程调用。
/// </summary>
public static class UserAgentService
{
    public const string Unknown = "未知";

    private const int CacheLimit = 10_000;

    // 两代缓存：写满后整体降为旧代而非清空，热点 UA 命中旧代时晋升回新代，
    // 避免 Clear 造成的周期性全量重解析
    private static Dictionary<string, (string Os, string Browser)> _cache = new();
    private static Dictionary<string, (string Os, string Browser)> _previousCache = new();

    public static (string Os, string Browser) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return (Unknown, Unknown);

        if (_cache.TryGetValue(userAgent, out var cached)) return cached;

        if (!_previousCache.TryGetValue(userAgent, out cached)) cached = ParseCore(userAgent);

        if (_cache.Count >= CacheLimit)
        {
            _previousCache = _cache;
            _cache = new Dictionary<string, (string Os, string Browser)>(CacheLimit);
        }

        _cache[userAgent] = cached;
        return cached;
    }

    private static (string Os, string Browser) ParseCore(string userAgent)
    {
        try
        {
            var info = HttpUserAgentParser.Parse(userAgent);

            var os = info.Platform?.Name;
            var browser = info.Name;

            if (string.IsNullOrEmpty(browser) && info.Type == HttpUserAgentType.Robot) browser = "Bot";
            if (string.IsNullOrEmpty(os) && StartsWithAny(userAgent, "curl/", "Wget/")) os = userAgent.Split('/')[0];

            return (string.IsNullOrEmpty(os) ? Unknown : os,
                string.IsNullOrEmpty(browser) ? Unknown : browser);
        }
        catch
        {
            return (Unknown, Unknown);
        }
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
