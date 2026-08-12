using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Core.Entities;
using FastGateway.Dto;
using FastGateway.Infrastructure;
using FastGateway.Services.Statistics;

namespace FastGateway.Services;

/// <summary>
///     限流：基于 .NET 内置 System.Threading.RateLimiting 的按 IP 分区固定窗口限流。
///     替代 AspNetCoreRateLimit（IMemoryCache + 异步锁），未命中规则的请求零额外开销，
///     且无需在管道里增删头传递客户端 IP。规则在网关构建时绑定，变更后需 Reload 网关生效。
/// </summary>
public static class RateLimitService
{
    public static WebApplication UseRateLimitMiddleware(this WebApplication app, List<RateLimit> rateLimits)
    {
        var entries = BuildEntries(rateLimits);
        if (entries.Length == 0) return app;

        // 网关关闭时释放分区限流器（内部按 IP 缓存 FixedWindowRateLimiter 并自动清理空闲分区）
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            foreach (var entry in entries) entry.Limiter.Dispose();
        });

        app.Use(async (context, next) =>
        {
            RateLimitEntry? rejected = null;
            string? ip = null;

            foreach (var entry in entries)
            {
                if (!entry.Matches(context.Request.Method, context.Request.Path)) continue;

                ip ??= ClientIpHelper.GetClientIp(context);
                if (string.IsNullOrEmpty(ip)) break;
                if (!entry.IpWhitelist.IsEmpty && entry.IpWhitelist.Contains(ip)) continue;

                using var lease = entry.Limiter.AttemptAcquire(ip);
                if (lease.IsAcquired) continue;

                rejected = entry;
                break;
            }

            if (rejected == null)
            {
                await next(context);
                return;
            }

            context.Items[StatisticsCollector.BlockReasonKey] = (byte)BlockReason.RateLimit;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = rejected.RetryAfterSeconds;
            context.Response.Headers["X-Rate-Limit-Limit"] = rejected.LimitText;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ResultDto.CreateFailed("请求过于频繁,请稍后再试"),
                AppJsonContext.Default.ResultDto);
        });

        return app;
    }

    private static RateLimitEntry[] BuildEntries(List<RateLimit> rateLimits)
    {
        var entries = new List<RateLimitEntry>(rateLimits.Count);

        foreach (var rule in rateLimits)
        {
            if (!rule.Enable || rule.Limit <= 0 || string.IsNullOrWhiteSpace(rule.Endpoint)) continue;

            entries.Add(RateLimitEntry.Create(rule));
        }

        return entries.ToArray();
    }

    /// <summary>
    ///     解析 AspNetCoreRateLimit 风格周期："1s" / "5m" / "1h" / "1d"，另兼容 UI 的 "1w" / "1M"。
    /// </summary>
    private static TimeSpan ParsePeriod(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return TimeSpan.FromSeconds(1);

        var span = period.AsSpan().Trim();
        var unit = span[^1];
        if (!double.TryParse(span[..^1], out var amount) || amount <= 0) amount = 1;

        return unit switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            'w' => TimeSpan.FromDays(amount * 7),
            'M' => TimeSpan.FromDays(amount * 30),
            _ => TimeSpan.FromSeconds(amount)
        };
    }

    private sealed class RateLimitEntry
    {
        private string? _verb;
        private PathString _pathPrefix;
        private string _wildcardPattern = string.Empty;
        private bool _matchAll;
        private string[] _endpointWhitelist = [];

        public required PartitionedRateLimiter<string> Limiter { get; init; }
        public required IpPolicyMatcher IpWhitelist { get; init; }
        public required string RetryAfterSeconds { get; init; }
        public required string LimitText { get; init; }

        public static RateLimitEntry Create(RateLimit rule)
        {
            var window = ParsePeriod(rule.Period);
            var limit = rule.Limit;

            var entry = new RateLimitEntry
            {
                Limiter = PartitionedRateLimiter.Create<string, string>(ip =>
                    RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    })),
                IpWhitelist = rule.IpWhitelist is { Length: > 0 }
                    ? IpPolicyMatcher.Build(rule.IpWhitelist.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : IpPolicyMatcher.Empty,
                RetryAfterSeconds = Math.Max(1, (long)window.TotalSeconds).ToString(),
                LimitText = limit.ToString()
            };

            entry.ParseEndpoint(rule.Endpoint);
            entry._endpointWhitelist = rule.EndpointWhitelist is { Length: > 0 }
                ? rule.EndpointWhitelist.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizePath).ToArray()
                : [];

            return entry;
        }

        /// <summary>
        ///     端点格式（沿用 AspNetCoreRateLimit 习惯）："*"、"/api"、"get:/api/*"。
        ///     无通配符按路径段前缀匹配（"/api" 命中 "/api" 与 "/api/x"，不命中 "/apix"）。
        /// </summary>
        private void ParseEndpoint(string endpoint)
        {
            var pattern = endpoint.Trim();

            var colon = pattern.IndexOf(':');
            if (colon > 0 && pattern.AsSpan(0, colon).IndexOf('/') < 0)
            {
                var verb = pattern[..colon].Trim();
                if (verb != "*") _verb = verb.ToUpperInvariant();
                pattern = pattern[(colon + 1)..].Trim();
            }

            if (pattern.Length == 0 || pattern == "*")
            {
                _matchAll = true;
                return;
            }

            pattern = NormalizePath(pattern);

            if (pattern.Contains('*'))
                _wildcardPattern = pattern;
            else
                _pathPrefix = new PathString(pattern);
        }

        private static string NormalizePath(string path)
        {
            path = path.Trim();
            return path.StartsWith('/') || path.StartsWith('*') ? path : "/" + path;
        }

        public bool Matches(string method, PathString path)
        {
            if (_verb != null && !string.Equals(method, _verb, StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var whitelisted in _endpointWhitelist)
            {
                if (whitelisted.Contains('*'))
                {
                    if (WildcardMatch(path.Value ?? "/", whitelisted)) return false;
                }
                else if (path.StartsWithSegments(whitelisted, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (_matchAll) return true;
            if (_wildcardPattern.Length > 0) return WildcardMatch(path.Value ?? "/", _wildcardPattern);
            return path.StartsWithSegments(_pathPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     迭代式通配符匹配（大小写不敏感，'*' 匹配任意长度）。
        /// </summary>
        private static bool WildcardMatch(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern)
        {
            int i = 0, p = 0, star = -1, mark = 0;

            while (i < input.Length)
            {
                if (p < pattern.Length && pattern[p] != '*' &&
                    char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(input[i]))
                {
                    i++;
                    p++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = i;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    i = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }

    public static IEndpointRouteBuilder MapRateLimit(this IEndpointRouteBuilder app)
    {
        var rateLimit = app.MapGroup("/api/v1/rate-limit")
            .WithTags("限流")
            .WithDescription("限流管理")
            .RequireAuthorization()
            .AddEndpointFilter<ResultFilter>()
            .WithDisplayName("限流");

        rateLimit.MapPost(string.Empty, (ConfigurationService configService, RateLimit limit) =>
        {
            if (string.IsNullOrWhiteSpace(limit.Name)) throw new ValidationException("限流名称不能为空");

            if (configService.GetRateLimits().Any(x => x.Name == limit.Name)) throw new ValidationException("限流名称已存在");

            configService.AddRateLimit(limit);
        }).WithDescription("创建限流").WithDisplayName("创建限流").WithTags("限流");

        rateLimit.MapGet(string.Empty, (ConfigurationService configService, int page, int pageSize) =>
            {
                var allLimits = configService.GetRateLimits();
                var total = allLimits.Count();
                var result = allLimits
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return new PagingDto<RateLimit>(total, result);
            })
            .WithDescription("获取限流列表")
            .WithDisplayName("获取限流列表")
            .WithTags("限流");

        rateLimit.MapDelete("{id}",
                (ConfigurationService configService, string id) => { configService.DeleteRateLimit(id); })
            .WithDescription("删除限流").WithDisplayName("删除限流").WithTags("限流");

        rateLimit.MapPut("{id}", (ConfigurationService configService, string id, RateLimit rateLimit) =>
        {
            if (string.IsNullOrWhiteSpace(rateLimit.Name)) throw new ValidationException("限流名称不能为空");

            if (configService.GetRateLimits().Any(x => x.Name == rateLimit.Name && x.Id != id))
                throw new ValidationException("限流名称已存在");

            rateLimit.Id = id;
            configService.UpdateRateLimit(rateLimit);
        }).WithDescription("更新限流").WithDisplayName("更新限流").WithTags("限流");

        return app;
    }
}
