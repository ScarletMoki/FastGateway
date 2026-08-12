using System.Buffers.Binary;
using System.Text;
using FastGateway.Dto;
using FastGateway.Services.Statistics;

namespace FastGateway.Infrastructure;

/// <summary>
///     将地区规则（"国家" 或 "国家|省份"）反向编译为 ip2region.xdb 中对应的 IPv4 区间。
///     仅在配置变更时执行：直接扫描 xdb 段索引（每段 14 字节：起始 IP、结束 IP、region 数据指针），
///     按数据指针去重后匹配规则，请求路径上完全不做 GeoIP 查询。
/// </summary>
public static class RegionIpRangeProvider
{
    // xdb v2 头部 256 字节：version(2) indexPolicy(2) createdAt(4) startIndexPtr(4) endIndexPtr(4)
    private const int HeaderSize = 256;
    private const int SegmentIndexSize = 14;

    private static readonly object CatalogLock = new();
    private static RegionCatalogDto? _catalog;

    private static string XdbPath => Path.Combine(AppContext.BaseDirectory, "ip2region.xdb");

    /// <summary>
    ///     解析地区规则并返回命中的全部 IPv4 区间（未合并、未排序，由调用方交给 IpPolicyMatcher 处理）。
    /// </summary>
    public static List<(uint Start, uint End)> GetRanges(IReadOnlyCollection<string> regionRules, ILogger? logger = null)
    {
        var ranges = new List<(uint Start, uint End)>();
        if (regionRules.Count == 0) return ranges;

        var rules = ParseRules(regionRules);
        if (rules.Count == 0) return ranges;

        if (!TryReadXdb(out var buffer, out var startPtr, out var endPtr, logger)) return ranges;

        // 同一地区字符串被大量段共享同一数据指针，按指针缓存匹配结果避免重复解码
        var matchByDataPtr = new Dictionary<uint, bool>();

        for (var ptr = startPtr; ptr <= endPtr; ptr += SegmentIndexSize)
        {
            var span = buffer.AsSpan((int)ptr, SegmentIndexSize);
            var dataPtr = BinaryPrimitives.ReadUInt32LittleEndian(span[10..]);

            if (!matchByDataPtr.TryGetValue(dataPtr, out var matched))
            {
                var dataLen = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
                matched = dataPtr + dataLen <= (uint)buffer.Length &&
                          MatchesRegion(buffer.AsSpan((int)dataPtr, dataLen), rules);
                matchByDataPtr[dataPtr] = matched;
            }

            if (!matched) continue;

            ranges.Add((BinaryPrimitives.ReadUInt32LittleEndian(span),
                BinaryPrimitives.ReadUInt32LittleEndian(span[4..])));
        }

        return ranges;
    }

    /// <summary>
    ///     扫描 xdb 得到全部国家及中国省份的去重列表（一次扫描后缓存），
    ///     供前端选择器使用，保证名称与匹配时完全一致。
    /// </summary>
    public static RegionCatalogDto GetAvailableRegions(ILogger? logger = null)
    {
        var catalog = Volatile.Read(ref _catalog);
        if (catalog != null) return catalog;

        lock (CatalogLock)
        {
            _catalog ??= BuildCatalog(logger);
            return _catalog;
        }
    }

    private static RegionCatalogDto BuildCatalog(ILogger? logger)
    {
        var result = new RegionCatalogDto();
        if (!TryReadXdb(out var buffer, out var startPtr, out var endPtr, logger)) return result;

        var countries = new HashSet<string>(StringComparer.Ordinal);
        var provinces = new HashSet<string>(StringComparer.Ordinal);
        var seenDataPtr = new HashSet<uint>();

        for (var ptr = startPtr; ptr <= endPtr; ptr += SegmentIndexSize)
        {
            var span = buffer.AsSpan((int)ptr, SegmentIndexSize);
            var dataPtr = BinaryPrimitives.ReadUInt32LittleEndian(span[10..]);
            if (!seenDataPtr.Add(dataPtr)) continue;

            var dataLen = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            if (dataPtr + dataLen > (uint)buffer.Length) continue;

            var (country, province) = ParseRegionData(buffer.AsSpan((int)dataPtr, dataLen));
            if (country.Length == 0 || country == GeoIpService.Unknown) continue;

            countries.Add(country);
            if (country == GeoIpService.China && province.Length > 0) provinces.Add(province);
        }

        result.Countries = countries.Order(StringComparer.Ordinal).ToList();
        result.ChinaProvinces = provinces.Order(StringComparer.Ordinal).ToList();
        return result;
    }

    private static bool TryReadXdb(out byte[] buffer, out uint startPtr, out uint endPtr, ILogger? logger)
    {
        buffer = [];
        startPtr = 0;
        endPtr = 0;

        try
        {
            var path = XdbPath;
            if (!File.Exists(path))
            {
                logger?.LogWarning("未找到 ip2region.xdb，地区规则未生效");
                return false;
            }

            buffer = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "读取 ip2region.xdb 失败，地区规则未生效");
            return false;
        }

        if (buffer.Length < HeaderSize)
        {
            logger?.LogWarning("ip2region.xdb 文件过小，地区规则未生效");
            return false;
        }

        startPtr = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
        endPtr = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12));

        if (startPtr < HeaderSize || endPtr < startPtr || endPtr + SegmentIndexSize > (uint)buffer.Length)
        {
            logger?.LogWarning("ip2region.xdb 段索引指针异常，地区规则未生效");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     规则 → 国家 → 省份集合；值为 null 表示封禁整个国家。
    /// </summary>
    private static Dictionary<string, HashSet<string>?> ParseRules(IReadOnlyCollection<string> regionRules)
    {
        var rules = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);

        foreach (var raw in regionRules)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var rule = raw.Trim();
            var sep = rule.IndexOf('|');
            var country = GeoIpService.NormalizeCountryName(sep < 0 ? rule : rule[..sep].Trim());
            if (country.Length == 0 || country == GeoIpService.Unknown) continue;

            var province = sep < 0 ? string.Empty : rule[(sep + 1)..].Trim();

            if (province.Length == 0)
            {
                // 整个国家封禁，覆盖已存在的省级规则
                rules[country] = null;
                continue;
            }

            if (rules.TryGetValue(country, out var provinces))
            {
                provinces?.Add(province);
            }
            else
            {
                rules[country] = new HashSet<string>(StringComparer.Ordinal) { province };
            }
        }

        return rules;
    }

    private static bool MatchesRegion(ReadOnlySpan<byte> data, Dictionary<string, HashSet<string>?> rules)
    {
        var (country, province) = ParseRegionData(data);
        if (country.Length == 0) return false;

        if (!rules.TryGetValue(country, out var provinces)) return false;

        return provinces == null || provinces.Contains(province);
    }

    /// <summary>
    ///     解析 xdb region 字符串（格式：国家|区域|省份|城市|ISP，缺失字段为 0），
    ///     国家名归一化规则与 <see cref="GeoIpService" /> 保持一致。
    /// </summary>
    private static (string Country, string Province) ParseRegionData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return (string.Empty, string.Empty);

        var region = Encoding.UTF8.GetString(data);
        var parts = region.Split('|');

        var rawCountry = parts.Length > 0 ? parts[0] : string.Empty;
        var rawProvince = parts.Length > 2 ? parts[2] : string.Empty;

        var country = rawCountry.Length == 0 || rawCountry == "0"
            ? string.Empty
            : GeoIpService.NormalizeCountryName(rawCountry);
        var province = rawProvince == "0" ? string.Empty : rawProvince;

        return (country, province);
    }
}
