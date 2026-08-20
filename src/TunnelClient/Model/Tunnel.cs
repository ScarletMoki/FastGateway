using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;
using AppContext = TunnelClient.AppContext;

namespace TunnelClient.Model;

public class Tunnel
{
    /// <summary>
    ///     本地 YARP 监听端口；0 或缺省时自动选择空闲端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    ///     节点密钥（面板创建节点时生成）
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    ///     旧字段：全局令牌，仅兼容保留；优先使用 Key
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    ///     实际用于认证的凭据
    /// </summary>
    public string EffectiveKey => string.IsNullOrEmpty(Key) ? Token ?? string.Empty : Key;

    /// <summary>
    ///     ws 或 h2
    /// </summary>
    public string Type { get; set; } = "ws";

    public bool ServerHttp2Support { get; set; } = true;

    public bool IsHttp2 => Type.Equals("h2", StringComparison.OrdinalIgnoreCase);

    public bool IsWebSocket => Type.Equals("ws", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     服务器地址
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    ///     节点名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     重连间隔，单位秒
    /// </summary>
    public int ReconnectInterval { get; set; }

    /// <summary>
    ///     心跳间隔，单位秒
    /// </summary>
    public int HeartbeatInterval { get; set; }

    /// <summary>
    ///     跳过 TLS 证书校验（自签名证书场景显式开启，启动时会输出警告）
    /// </summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>
    ///     代理配置
    /// </summary>
    public TunnelProxy[] Proxy { get; set; } = [];

    /// <summary>
    ///     加载配置：优先 -c/--config 指定的文件；提供 --server 时走纯命令行模式；
    ///     否则回退到程序目录下的 tunnel.json。
    /// </summary>
    public static Tunnel Load(string[] args)
    {
        var configPath = GetArgValue(args, "-c", "--config");
        if (configPath != null)
        {
            if (!File.Exists(configPath))
                throw new ArgumentException($"配置文件不存在或路径错误: {configPath}");

            return FromFile(configPath);
        }

        var server = GetArgValue(args, "-s", "--server");
        if (server != null) return FromCommandLine(args, server);

        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tunnel.json");
        if (File.Exists(defaultPath)) return FromFile(defaultPath);

        throw new ArgumentException(
            "缺少启动参数。两种方式任选其一：\n" +
            "  1. 配置文件：./TunnelClient -c ./tunnel.json\n" +
            "  2. 命令行：./TunnelClient --server ws://gateway:8080 --name node1 --key fgk_xxx [--proxy [域名=]http://127.0.0.1:8080]");
    }

    private static Tunnel FromFile(string path)
    {
        var json = File.ReadAllText(path);
        var tunnel = JsonSerializer.Deserialize(json, AppContext.Default.Tunnel);
        if (tunnel == null)
            throw new ArgumentException($"配置文件解析失败: {path}");

        return tunnel;
    }

    private static Tunnel FromCommandLine(string[] args, string server)
    {
        var tunnel = new Tunnel
        {
            ServerUrl = server,
            Name = GetArgValue(args, "-n", "--name") ?? string.Empty,
            Key = GetArgValue(args, "-k", "--key"),
            Type = GetArgValue(args, "-t", "--type") ?? "ws",
            InsecureSkipVerify = args.Contains("--insecure")
        };

        if (int.TryParse(GetArgValue(args, "-p", "--port"), out var port)) tunnel.Port = port;

        // --proxy [域名=]本地地址，可重复；未指定域名时按 catch-all 匹配
        var proxies = new List<TunnelProxy>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--proxy") continue;

            var spec = args[i + 1];
            var separatorIndex = spec.IndexOf('=');
            string? domain = null;
            var local = spec;
            if (separatorIndex > 0 && !spec[..separatorIndex].Contains("://"))
            {
                domain = spec[..separatorIndex];
                local = spec[(separatorIndex + 1)..];
            }

            proxies.Add(new TunnelProxy
            {
                Route = "/",
                Domains = domain == null ? [] : [domain],
                LocalRemote = local,
                Description = "命令行代理规则",
                Enabled = true
            });
        }

        tunnel.Proxy = proxies.ToArray();
        return tunnel;
    }

    private static string? GetArgValue(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
                return args[i + 1].Trim();

        return null;
    }

    public static (IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) ToYarpOption(
        Tunnel tunnel)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        // 仅为启用的代理规则生成本地路由
        foreach (var proxy in tunnel.Proxy.Where(p => p.Enabled))
        {
            var routeMetadata = new Dictionary<string, string>(0);
            var routeId = Guid.NewGuid().ToString("N");
            var clusterId = Guid.NewGuid().ToString("N");

            var path = proxy.Route ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                path = "/{**catch-all}";
            else if (path == "/")
                path = "/{**catch-all}";
            else
                path = $"/{path.TrimStart('/')}/{{**catch-all}}";

            var route = new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch
                {
                    Path = path,
                    Hosts = proxy.Domains is { Length: > 0 } ? proxy.Domains : null
                },
                Metadata = routeMetadata
            };

            DestinationConfig config;

            if (!string.IsNullOrEmpty(proxy.Host))
                config = new DestinationConfig
                {
                    Address = proxy.LocalRemote,
                    Host = proxy.Host
                };
            else
                config = new DestinationConfig
                {
                    Address = proxy.LocalRemote
                };

            var cluster = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    {
                        Guid.NewGuid().ToString("N"),
                        config
                    }
                }
            };

            clusters.Add(cluster);

            routes.Add(route);
        }

        return (routes, clusters);
    }

    public void Validate()
    {
        if (string.IsNullOrEmpty(EffectiveKey))
            throw new ArgumentException("缺少节点密钥：请配置 Key（或命令行 --key）。");

        if (string.IsNullOrEmpty(ServerUrl))
            throw new ArgumentException("缺少服务器地址：请配置 ServerUrl（或命令行 --server）。");

        if (string.IsNullOrEmpty(Name))
            throw new ArgumentException("缺少节点名称：请配置 Name（或命令行 --name）。");

        if (!IsHttp2 && !IsWebSocket)
            throw new ArgumentException("Type 仅支持 ws 或 h2。");

        // 按传输类型归一化 URL scheme：h2 需要 http(s)，ws 需要 ws(s)
        ServerUrl = NormalizeServerUrl(ServerUrl, IsHttp2);

        if (ReconnectInterval <= 0) ReconnectInterval = 5; // 默认重连间隔 5 秒
        if (HeartbeatInterval <= 0) HeartbeatInterval = 30; // 默认心跳间隔 30 秒
        HeartbeatInterval = Math.Clamp(HeartbeatInterval, 10, 300);

        Proxy ??= [];

        foreach (var proxy in Proxy)
        {
            if (string.IsNullOrEmpty(proxy.LocalRemote))
                throw new ArgumentException("代理规则的 LocalRemote 不能为空。");

            // Route 允许为空（ToYarpOption 会回退到 catch-all），非空时必须以 '/' 开头
            if (!string.IsNullOrWhiteSpace(proxy.Route) && !proxy.Route.StartsWith('/'))
                throw new ArgumentException("代理规则的 Route 必须以 '/' 开头。");

            if (!proxy.LocalRemote.StartsWith("http://") && !proxy.LocalRemote.StartsWith("https://") &&
                !proxy.LocalRemote.StartsWith("ws://") && !proxy.LocalRemote.StartsWith("wss://"))
                throw new ArgumentException(
                    "代理规则的 LocalRemote 必须以 http:// / https:// / ws:// / wss:// 开头。");
        }
    }

    private static string NormalizeServerUrl(string url, bool isHttp2)
    {
        url = url.TrimEnd('/');
        return isHttp2
            ? url.Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            : url.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     确定本地 YARP 监听端口；未指定时自动选择空闲端口
    /// </summary>
    public int ResolveLocalPort()
    {
        if (Port > 0) return Port;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return Port;
    }

    public class TunnelProxy
    {
        /// <summary>
        ///     为空则默认
        /// </summary>
        public string? Host { get; set; }

        /// <summary>
        ///     域名匹配
        /// </summary>
        public string[] Domains { get; set; } = [];

        /// <summary>
        ///     拦截网格路由
        /// </summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>
        ///     代理到本地服务
        /// </summary>
        public string LocalRemote { get; set; } = string.Empty;

        /// <summary>
        ///     描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        ///     是否启用
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
