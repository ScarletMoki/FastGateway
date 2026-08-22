using Core.Entities;
using Core.Entities.Core;
using FastGateway.Dto;
using FastGateway.Infrastructure;
using FastGateway.Services;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace FastGateway.Middleware;

public sealed class ClusterRequestFailoverMiddleware
{
    private static readonly ConcurrentDictionary<int, HttpMessageInvoker> Clients = new();

    private readonly RequestDelegate _next;
    private readonly string _serverId;
    private readonly string _gatewayVersion;
    private readonly ILogger<ClusterRequestFailoverMiddleware> _logger;

    // 按配置版本缓存的路由表：条目预排序、路径预归一化、transformer 预创建，
    // 避免每请求线性扫描配置 + LINQ 路由重匹配
    private FailoverRouteTable? _routeTable;

    public ClusterRequestFailoverMiddleware(
        RequestDelegate next,
        string serverId,
        string gatewayVersion,
        ILogger<ClusterRequestFailoverMiddleware> logger)
    {
        _next = next;
        _serverId = serverId;
        _gatewayVersion = gatewayVersion;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ConfigurationService configurationService,
        IProxyStateLookup proxyStateLookup,
        IHttpForwarder httpForwarder,
        IDestinationHealthUpdater destinationHealthUpdater)
    {
        var table = GetRouteTable(configurationService);
        if (!table.Enabled || !ShouldHandleRequest(context))
        {
            await _next(context);
            return;
        }

        var entry = table.Match(context.Request.Host.Host, context.Request.Path);
        if (entry is null)
        {
            await _next(context);
            return;
        }

        if (!proxyStateLookup.TryGetCluster(entry.DomainName.Id, out var cluster) || cluster is null)
        {
            await _next(context);
            return;
        }

        var candidates = GetCandidateDestinations(cluster);
        if (candidates.Length <= 1)
        {
            await _next(context);
            return;
        }

        Shuffle(candidates);

        var httpClient = GetOrCreateClient(table.ConnectTimeoutMs);
        var attempts = 0;
        var startTimestamp = Stopwatch.GetTimestamp();
        ForwarderError lastError = ForwarderError.None;
        Exception? lastException = null;

        foreach (var destination in candidates)
        {
            if (attempts > 0 && Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds > table.BudgetMs)
            {
                break;
            }

            attempts++;
            context.Features.Set<IForwarderErrorFeature?>(null);

            var error = await httpForwarder.SendAsync(
                context,
                destination.Model.Config.Address,
                httpClient,
                table.RequestConfig,
                entry.Transformer,
                context.RequestAborted);

            if (error == ForwarderError.None)
            {
                if (attempts > 1)
                {
                    _logger.LogInformation(
                        "集群请求故障转移成功 ClusterId={ClusterId} DestinationId={DestinationId} Attempts={Attempts} ElapsedMs={ElapsedMs}",
                        cluster.ClusterId,
                        destination.DestinationId,
                        attempts,
                        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }
                return;
            }

            var errorFeature = context.Features.Get<IForwarderErrorFeature>();
            lastError = error;
            lastException = errorFeature?.Exception;

            if (IsRetriableTransportError(error, lastException) && !context.Response.HasStarted)
            {
                destinationHealthUpdater.SetPassive(cluster, destination, DestinationHealth.Unhealthy,
                    entry.ReactivationPeriod);
                _logger.LogWarning(
                    lastException,
                    "集群请求故障转移，目标切换 ClusterId={ClusterId} DestinationId={DestinationId} Error={Error} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    cluster.ClusterId,
                    destination.DestinationId,
                    error,
                    attempts,
                    (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                continue;
            }

            return;
        }

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.Headers["Server"] = "FastGateway";
            context.Response.Headers["X-FastGateway-Version"] = _gatewayVersion;
            await context.Response.WriteAsJsonAsync(new CodeMessageDto
            {
                Code = StatusCodes.Status504GatewayTimeout,
                Message = "没有可用的健康上游节点或请求级故障转移预算已耗尽",
                Error = lastError.ToString(),
                Detail = lastException?.Message
            }, AppJsonContext.Default.CodeMessageDto);
        }
    }

    private FailoverRouteTable GetRouteTable(ConfigurationService configurationService)
    {
        var table = Volatile.Read(ref _routeTable);
        var version = configurationService.Version;
        if (table is not null && table.Version == version)
        {
            return table;
        }

        table = FailoverRouteTable.Build(
            version,
            configurationService.GetServer(_serverId),
            configurationService.GetDomainNamesByServerId(_serverId),
            _gatewayVersion);
        Volatile.Write(ref _routeTable, table);
        return table;
    }

    private static bool ShouldHandleRequest(HttpContext context)
    {
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            return false;
        }

        if (context.WebSockets.IsWebSocketRequest)
        {
            return false;
        }

        if (context.Request.Headers.Connection == "Upgrade")
        {
            return false;
        }

        var accept = context.Request.Headers.Accept;
        for (var i = 0; i < accept.Count; i++)
        {
            var value = accept[i];
            if (value is not null && value.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchHost(DomainName domainName, string requestHost)
    {
        if (domainName.Domains is not { Length: > 0 })
        {
            return true;
        }

        foreach (var hostPattern in domainName.Domains)
        {
            if (string.IsNullOrWhiteSpace(hostPattern))
            {
                continue;
            }

            if (hostPattern == "*")
            {
                return true;
            }

            if (string.Equals(hostPattern, requestHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (hostPattern.StartsWith("*.", StringComparison.Ordinal) &&
                requestHost.EndsWith(hostPattern[1..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRoutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return "/" + path.Trim().Trim('/');
    }

    private static DestinationState[] GetCandidateDestinations(ClusterState cluster)
    {
        var destinations = cluster.Destinations.Values;
        var result = new List<DestinationState>(cluster.Destinations.Count);
        foreach (var destination in destinations)
        {
            if (GetEffectiveHealth(destination.Health) != DestinationHealth.Unhealthy)
            {
                result.Add(destination);
            }
        }

        return result.ToArray();
    }

    private static DestinationHealth GetEffectiveHealth(DestinationHealthState healthState)
    {
        if (healthState.Active == DestinationHealth.Unhealthy || healthState.Passive == DestinationHealth.Unhealthy)
        {
            return DestinationHealth.Unhealthy;
        }

        if (healthState.Active == DestinationHealth.Unknown || healthState.Passive == DestinationHealth.Unknown)
        {
            return DestinationHealth.Unknown;
        }

        return DestinationHealth.Healthy;
    }

    private static void Shuffle(DestinationState[] destinations)
    {
        // 就地 Fisher-Yates，替代 OrderBy(Random) 的排序与键分配
        for (var i = destinations.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (destinations[i], destinations[j]) = (destinations[j], destinations[i]);
        }
    }

    private static bool IsRetriableTransportError(ForwarderError error, Exception? exception)
    {
        if (exception is HttpRequestException or SocketException or TaskCanceledException or TimeoutException)
        {
            return true;
        }

        return error is ForwarderError.Request
            or ForwarderError.RequestTimedOut
            or ForwarderError.ResponseHeaders
            or ForwarderError.RequestCreation;
    }

    private static HttpMessageInvoker GetOrCreateClient(int connectTimeoutMs)
    {
        return Clients.GetOrAdd(connectTimeoutMs, static timeout =>
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                EnableMultipleHttp2Connections = false,
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
                ConnectTimeout = TimeSpan.FromMilliseconds(timeout),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                ResponseDrainTimeout = TimeSpan.FromSeconds(5)
            };

            return new HttpMessageInvoker(handler, disposeHandler: true);
        });
    }

    private sealed class FailoverRouteTable
    {
        private static readonly FailoverEntry[] EmptyEntries = [];

        public required long Version { get; init; }
        public required bool Enabled { get; init; }
        public required FailoverEntry[] Entries { get; init; }
        public required ForwarderRequestConfig RequestConfig { get; init; }
        public required int ConnectTimeoutMs { get; init; }
        public required int BudgetMs { get; init; }

        public static FailoverRouteTable Build(
            long version,
            Server? server,
            DomainName[] domainNames,
            string gatewayVersion)
        {
            if (server is null || !server.EnableRequestFailover)
            {
                return CreateDisabled(version);
            }

            var entries = new List<FailoverEntry>();
            foreach (var domainName in domainNames)
            {
                if (domainName is not { Enable: true, ServiceType: ServiceType.ServiceCluster })
                {
                    continue;
                }

                var routePath = NormalizeRoutePath(domainName.Path);
                var hasWildcardHost = false;
                var hasExactHost = false;
                if (domainName.Domains is { Length: > 0 })
                {
                    foreach (var host in domainName.Domains)
                    {
                        if (host.Contains('*')) hasWildcardHost = true;
                        else hasExactHost = true;
                    }
                }

                var reactivationSeconds = domainName.HealthCheckIntervalSeconds;
                if (reactivationSeconds <= 0) reactivationSeconds = 10;

                entries.Add(new FailoverEntry
                {
                    DomainName = domainName,
                    RoutePath = routePath,
                    HostPriority = hasExactHost ? 2 : hasWildcardHost ? 1 : 0,
                    ReactivationPeriod = TimeSpan.FromSeconds(reactivationSeconds),
                    Transformer = new ClusterFailoverTransformer(
                        routePath,
                        hasWildcardHost || server.CopyRequestHost,
                        gatewayVersion)
                });
            }

            if (entries.Count == 0)
            {
                return CreateDisabled(version);
            }

            // 预排序：精确域名优先于泛域名，路径越长越优先；匹配时取第一个命中即可
            entries.Sort((a, b) =>
            {
                var byHost = b.HostPriority.CompareTo(a.HostPriority);
                return byHost != 0 ? byHost : b.RoutePath.Length.CompareTo(a.RoutePath.Length);
            });

            var connectTimeoutMs = server.FailoverConnectTimeoutMs > 0 ? server.FailoverConnectTimeoutMs : 150;
            var budgetMs = server.FailoverBudgetMs >= connectTimeoutMs ? server.FailoverBudgetMs : 500;
            var requestTimeoutSeconds = server.Timeout > 0 ? server.Timeout : 900;

            return new FailoverRouteTable
            {
                Version = version,
                Enabled = true,
                Entries = entries.ToArray(),
                RequestConfig = new ForwarderRequestConfig
                {
                    ActivityTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds),
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                },
                ConnectTimeoutMs = connectTimeoutMs,
                BudgetMs = budgetMs
            };
        }

        private static FailoverRouteTable CreateDisabled(long version)
        {
            return new FailoverRouteTable
            {
                Version = version,
                Enabled = false,
                Entries = EmptyEntries,
                RequestConfig = ForwarderRequestConfig.Empty,
                ConnectTimeoutMs = 150,
                BudgetMs = 500
            };
        }

        public FailoverEntry? Match(string requestHost, PathString requestPath)
        {
            foreach (var entry in Entries)
            {
                if (MatchHost(entry.DomainName, requestHost) && entry.MatchPath(requestPath))
                {
                    return entry;
                }
            }

            return null;
        }
    }

    private sealed class FailoverEntry
    {
        public required DomainName DomainName { get; init; }
        public required string RoutePath { get; init; }
        public required int HostPriority { get; init; }
        public required TimeSpan ReactivationPeriod { get; init; }
        public required ClusterFailoverTransformer Transformer { get; init; }

        public bool MatchPath(PathString requestPath)
        {
            return RoutePath == "/" || requestPath.StartsWithSegments(RoutePath, out _);
        }
    }

    private sealed class ClusterFailoverTransformer(string routePath, bool copyRequestHost, string gatewayVersion)
        : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext httpContext,
            HttpRequestMessage proxyRequest,
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

            var forwardPath = httpContext.Request.Path;
            if (routePath != "/" && httpContext.Request.Path.StartsWithSegments(routePath, out var remaining))
            {
                forwardPath = remaining.HasValue ? remaining : new PathString("/");
            }

            proxyRequest.RequestUri =
                RequestUtilities.MakeDestinationAddress(destinationPrefix, forwardPath, httpContext.Request.QueryString);

            if (copyRequestHost)
            {
                proxyRequest.Headers.Host = httpContext.Request.Host.Value;
            }
        }

        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext httpContext,
            HttpResponseMessage? proxyResponse,
            CancellationToken cancellationToken)
        {
            var shouldCopy = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            if (!shouldCopy)
            {
                return false;
            }

            httpContext.Response.Headers.Remove("Server");
            httpContext.Response.Headers["Server"] = "FastGateway";
            httpContext.Response.Headers["X-FastGateway-Version"] = gatewayVersion;
            return true;
        }
    }
}

public static class ClusterRequestFailoverMiddlewareExtensions
{
    public static IApplicationBuilder UseClusterRequestFailover(this IApplicationBuilder app, string serverId, string gatewayVersion)
    {
        return app.UseMiddleware<ClusterRequestFailoverMiddleware>(serverId, gatewayVersion);
    }
}
