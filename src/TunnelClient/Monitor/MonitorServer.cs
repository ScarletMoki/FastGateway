using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Core;
using TunnelClient.Model;

namespace TunnelClient.Monitor;

public class MonitorServer
{
    private readonly HttpMessageInvoker _httpClient;
    private readonly ILogger<MonitorServer> _logger;
    private readonly IServiceProvider _serviceProvider;

    public MonitorServer(IServiceProvider serviceProvider, Tunnel tunnel)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<MonitorServer>>();
        _httpClient = new HttpMessageInvoker(CreateDefaultHttpHandler(tunnel), true);
    }

    private static SocketsHttpHandler CreateDefaultHttpHandler(Tunnel tunnel)
    {
        var handler = new SocketsHttpHandler
        {
            // 允许多个http2连接
            EnableMultipleHttp2Connections = true,
            // 设置连接超时时间
            ConnectTimeout = TimeSpan.FromSeconds(60)
        };

        // TLS 证书校验默认开启；自签名证书需显式配置 InsecureSkipVerify
        if (tunnel.InsecureSkipVerify)
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };

        return handler;
    }

    public async Task<Stream> CreateTargetTunnelAsync(Tunnel tunnel, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            EndPoint endPoint = new DnsEndPoint("localhost", tunnel.Port);

            using var linkedTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None, cancellationToken);

            await socket.ConnectAsync(endPoint, linkedTokenSource.Token);
            return new NetworkStream(socket);
        }
        catch (Exception ex)
        {
            Log.CreateTargetTunnelFailed(_logger, ex.Message);
            socket.Dispose();
            throw;
        }
    }

    private async Task<Stream> HttpConnectServerCoreAsync(Tunnel tunnel,
        Guid? tunnelId,
        CancellationToken cancellationToken)
    {
        if (tunnel.IsHttp2) return await Http20ConnectServerAsync(tunnel, tunnelId, cancellationToken);

        if (tunnel.IsWebSocket)
            return await HttpWebSocketConnectServerAsync(tunnel, tunnelId, cancellationToken);

        Log.UnsupportedConnectionType(_logger, tunnel.Type);
        throw new NotSupportedException("不支持的连接类型，只支持http2和websocket连接。请检查Tunnel配置。");
    }

    public async Task<ServerConnection> CreateServerConnectionAsync(Tunnel tunnel,
        CancellationToken cancellationToken)
    {
        var stream = await HttpConnectServerCoreAsync(tunnel, null, cancellationToken);
        var safeWriteStream = new SafeWriteStream(stream);
        // 心跳间隔采用配置值（Validate 已钳制），与服务端保持一致
        return new ServerConnection(safeWriteStream, TimeSpan.FromSeconds(tunnel.HeartbeatInterval),
            _serviceProvider.GetRequiredService<ILogger<ServerConnection>>());
    }

    public async Task<Stream> ConnectServerAsync(Tunnel tunnel, Guid? tunnelId, CancellationToken cancellationToken)
    {
        return await HttpConnectServerCoreAsync(tunnel, tunnelId, cancellationToken);
    }

    /// <summary>
    ///     创建到服务器的通道
    /// </summary>
    /// <param name="tunnel"></param>
    /// <param name="tunnelId"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<Stream> CreateServerTunnelAsync(Tunnel tunnel, Guid tunnelId,
        CancellationToken cancellationToken)
    {
        var stream = await ConnectServerAsync(tunnel, tunnelId, cancellationToken);
        return new ForceFlushStream(stream);
    }

    /// <summary>
    ///     创建http2连接
    /// </summary>
    private async Task<Stream> Http20ConnectServerAsync(Tunnel tunnel,
        Guid? tunnelId,
        CancellationToken cancellationToken)
    {
        // 密钥经 Authorization 头传递，不再拼进 query
        var serverUri = tunnelId == null
            ? new Uri($"{tunnel.ServerUrl.TrimEnd('/')}/internal/gateway/Server?nodeName=" +
                      Uri.EscapeDataString(tunnel.Name))
            : new Uri($"{tunnel.ServerUrl.TrimEnd('/')}/internal/gateway/Server?tunnelId=" + tunnelId);

        // 这里我们使用Connect方法，因为我们需要建立一个双工流, 这样我们就可以进行双工通信了。
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);
        // 如果设置了Connect，那么我们需要设置Protocol
        request.Headers.Protocol = Constant.Protocol;
        // 我们需要设置http2的版本
        request.Version = HttpVersion.Version20;

        // 我们需要确保我们的请求是http2的
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        if (tunnelId == null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tunnel.EffectiveKey);

        // 设置一下超时时间，这样我们就可以在超时的时候取消连接了。
        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

        // 发送请求，然后等待响应
        var httpResponse = await _httpClient.SendAsync(request, linkedTokenSource.Token);

        if (httpResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Log.UnauthorizedAccess(_logger);
            throw new UnauthorizedAccessException("认证失败，请检查节点名与密钥是否正确、节点是否被禁用");
        }

        // 返回h2的流，用于传输数据
        return await httpResponse.Content.ReadAsStreamAsync(linkedTokenSource.Token);
    }

    private async Task<Stream> HttpWebSocketConnectServerAsync(Tunnel tunnel,
        Guid? tunnelId,
        CancellationToken cancellationToken)
    {
        // 密钥经 Authorization 头传递，不再拼进 query
        var serverUri = tunnelId == null
            ? new Uri($"{tunnel.ServerUrl.TrimEnd('/')}/internal/gateway/Server?nodeName=" +
                      Uri.EscapeDataString(tunnel.Name))
            : new Uri($"{tunnel.ServerUrl.TrimEnd('/')}/internal/gateway/Server?tunnelId=" + tunnelId);

        var webSocket = new ClientWebSocket();
        webSocket.Options.AddSubProtocol(Constant.Protocol);

        if (tunnel.ServerHttp2Support && serverUri.Scheme == Uri.UriSchemeWss)
        {
            webSocket.Options.HttpVersion = HttpVersion.Version20;
            webSocket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        }
        else
        {
            webSocket.Options.HttpVersion = HttpVersion.Version11;
            webSocket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        }

        if (tunnelId == null)
        {
            webSocket.Options.SetRequestHeader("nodeName", tunnel.Name);
            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {tunnel.EffectiveKey}");
            webSocket.Options.SetRequestHeader("clientType", "TunnelClient");
            webSocket.Options.SetRequestHeader("requestId", Guid.NewGuid().ToString());
            // 增加请求头信息以便于识别websocket
        }

        try
        {
            await webSocket.ConnectAsync(serverUri, _httpClient, cancellationToken);
            return new CustomWebSocketStream(webSocket);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            webSocket.Dispose();
            throw;
        }
        catch (WebSocketException e) when (e.Message.Contains("401") || e.Message.Contains("403"))
        {
            webSocket.Dispose();
            Log.UnauthorizedAccess(_logger);
            throw new UnauthorizedAccessException("认证失败，请检查节点名与密钥是否正确、节点是否被禁用");
        }
        catch (Exception)
        {
            webSocket.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     注册节点：上报节点名与代理规则，密钥经 Authorization 头传递
    /// </summary>
    public async Task RegisterNodeAsync(Tunnel tunnel, CancellationToken cancellationToken)
    {
        var serverUri = new Uri($"{ToHttpScheme(tunnel.ServerUrl)}/internal/gateway/Server/register");

        using var request = new HttpRequestMessage(HttpMethod.Post, serverUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(tunnel, AppContext.Default.Tunnel), Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tunnel.EffectiveKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Log.UnauthorizedAccess(_logger);
            throw new UnauthorizedAccessException("认证失败，请检查节点名与密钥是否正确、节点是否被禁用");
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            Log.RegisterNodeFailed(_logger, error);
            throw new InvalidOperationException($"注册节点失败: {error}");
        }

        Log.RegisterNodeSuccess(_logger, tunnel.Name, tunnel.Proxy.Count(p => p.Enabled));
    }

    /// <summary>
    ///     注册走普通 HTTP 请求：ws(s) scheme 需转换为 http(s)
    /// </summary>
    private static string ToHttpScheme(string serverUrl)
    {
        return serverUrl.TrimEnd('/')
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase);
    }
}

internal static partial class Log
{
    [LoggerMessage(LogLevel.Error, "创建targetTunnel隧道失败。{Message}")]
    public static partial void CreateTargetTunnelFailed(ILogger logger, string message);

    [LoggerMessage(LogLevel.Information, "节点 [{nodeName}] 注册成功，上报 {proxyCount} 条启用的代理规则")]
    public static partial void RegisterNodeSuccess(ILogger logger, string nodeName, int proxyCount);

    [LoggerMessage(LogLevel.Error, "不支持的连接类型，只支持http2和websocket连接。请检查Tunnel配置，当前协议:{type}")]
    public static partial void UnsupportedConnectionType(ILogger logger, string type);

    [LoggerMessage(LogLevel.Error, "注册节点失败: {message}")]
    public static partial void RegisterNodeFailed(ILogger logger, string message);

    [LoggerMessage(LogLevel.Error, "认证失败，请检查节点名与密钥是否正确、节点是否被禁用")]
    public static partial void UnauthorizedAccess(ILogger logger);
}
