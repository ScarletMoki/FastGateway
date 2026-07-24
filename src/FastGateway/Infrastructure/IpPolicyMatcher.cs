using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;

namespace FastGateway.Infrastructure;

/// <summary>
///     黑白名单预解析匹配器。构建一次（配置变更时），匹配时零分配：
///     精确 IP 走哈希集合 O(1)，IPv4 区间/CIDR 合并排序后二分查找，IPv6 CIDR 按掩码字节比较。
///     规则格式与旧版一致：单个 IP、"10.0.0.1-10.0.0.255" 区间、"172.16.0.1/24" CIDR；
///     无法解析的规则退回精确字符串匹配。
/// </summary>
public sealed class IpPolicyMatcher
{
    public static readonly IpPolicyMatcher Empty = Build(Array.Empty<string>());

    private readonly FrozenSet<string> _exact;
    private readonly uint[] _rangeStarts;
    private readonly uint[] _rangeEnds;
    private readonly V6Network[] _v6Networks;

    private IpPolicyMatcher(FrozenSet<string> exact, uint[] rangeStarts, uint[] rangeEnds, V6Network[] v6Networks)
    {
        _exact = exact;
        _rangeStarts = rangeStarts;
        _rangeEnds = rangeEnds;
        _v6Networks = v6Networks;
    }

    public bool IsEmpty => _exact.Count == 0 && _rangeStarts.Length == 0 && _v6Networks.Length == 0;

    public static IpPolicyMatcher Build(IEnumerable<string> rules)
    {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranges = new List<(uint Start, uint End)>();
        var v6Networks = new List<V6Network>();

        foreach (var raw in rules)
        {
            var rule = raw.Trim();
            if (rule.Length == 0) continue;

            var dash = rule.IndexOf('-');
            if (dash > 0)
            {
                if (TryParseIpv4(rule.AsSpan(0, dash).Trim(), out var start) &&
                    TryParseIpv4(rule.AsSpan(dash + 1).Trim(), out var end))
                {
                    ranges.Add(start <= end ? (start, end) : (end, start));
                    continue;
                }

                exact.Add(rule);
                continue;
            }

            var slash = rule.IndexOf('/');
            if (slash > 0)
            {
                var baseSpan = rule.AsSpan(0, slash).Trim();
                var prefixSpan = rule.AsSpan(slash + 1).Trim();

                if (int.TryParse(prefixSpan, out var prefix))
                {
                    if (TryParseIpv4(baseSpan, out var baseValue) && prefix is >= 0 and <= 32)
                    {
                        var hostMask = prefix == 0 ? uint.MaxValue : uint.MaxValue >> prefix;
                        var network = baseValue & ~hostMask;
                        ranges.Add((network, network | hostMask));
                        continue;
                    }

                    if (IPAddress.TryParse(baseSpan, out var v6Address) &&
                        v6Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                        prefix is >= 0 and <= 128)
                    {
                        v6Networks.Add(new V6Network(v6Address, prefix));
                        continue;
                    }
                }

                exact.Add(rule);
                continue;
            }

            exact.Add(rule);
        }

        // 排序并合并重叠区间，保证二分查找时任意值最多命中一个候选区间
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(uint Start, uint End)>(ranges.Count);
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                if (range.End > merged[^1].End) merged[^1] = (merged[^1].Start, range.End);
            }
            else
            {
                merged.Add(range);
            }
        }

        var starts = new uint[merged.Count];
        var ends = new uint[merged.Count];
        for (var i = 0; i < merged.Count; i++)
        {
            starts[i] = merged[i].Start;
            ends[i] = merged[i].End;
        }

        return new IpPolicyMatcher(
            exact.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            starts,
            ends,
            v6Networks.ToArray());
    }

    public bool Contains(string ip)
    {
        if (_exact.Count > 0 && _exact.Contains(ip)) return true;

        if (_rangeStarts.Length > 0 && TryParseIpv4(ip, out var value))
        {
            var index = Array.BinarySearch(_rangeStarts, value);
            if (index >= 0) return true;

            index = ~index - 1; // 最后一个 Start <= value 的区间
            if (index >= 0 && value <= _rangeEnds[index]) return true;
        }

        if (_v6Networks.Length > 0 && ip.Contains(':') && IPAddress.TryParse(ip, out var address) &&
            address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> bytes = stackalloc byte[16];
            if (address.TryWriteBytes(bytes, out var written) && written == 16)
                foreach (var network in _v6Networks)
                    if (network.Contains(bytes))
                        return true;
        }

        return false;
    }

    private static bool TryParseIpv4(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        var segment = 0;
        var segments = 0;
        var digits = 0;

        foreach (var c in text)
            if (c == '.')
            {
                if (digits == 0 || segments == 3)
                {
                    value = 0;
                    return false;
                }

                value = (value << 8) | (uint)segment;
                segments++;
                segment = 0;
                digits = 0;
            }
            else if (c is >= '0' and <= '9')
            {
                if (++digits > 3)
                {
                    value = 0;
                    return false;
                }

                segment = segment * 10 + (c - '0');
                if (segment > 255)
                {
                    value = 0;
                    return false;
                }
            }
            else
            {
                value = 0;
                return false;
            }

        if (digits == 0 || segments != 3)
        {
            value = 0;
            return false;
        }

        value = (value << 8) | (uint)segment;
        return true;
    }

    private readonly struct V6Network
    {
        private readonly byte[] _network = new byte[16];
        private readonly int _prefix;

        public V6Network(IPAddress baseAddress, int prefix)
        {
            _prefix = prefix;
            baseAddress.TryWriteBytes(_network, out _);

            // 归零主机位，允许非规范化的基址（如 fd00::1/64）
            for (var bit = prefix; bit < 128; bit++)
                _network[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        public bool Contains(ReadOnlySpan<byte> addressBytes)
        {
            var fullBytes = _prefix / 8;
            for (var i = 0; i < fullBytes; i++)
                if (addressBytes[i] != _network[i])
                    return false;

            var remainingBits = _prefix % 8;
            if (remainingBits == 0) return true;

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (addressBytes[fullBytes] & mask) == (_network[fullBytes] & mask);
        }
    }
}
