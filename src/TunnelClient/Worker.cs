using TunnelClient.Model;
using TunnelClient.Monitor;

namespace TunnelClient;

public partial class Worker : BackgroundService
{
    /// <summary>
    ///     认证失败退避上限（秒）
    /// </summary>
    private const int MaxAuthBackoffSeconds = 300;

    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _services;
    private readonly Tunnel _tunnel;

    public Worker(ILogger<Worker> logger, IServiceProvider services, Tunnel tunnel)
    {
        _logger = logger;
        _services = services;
        _tunnel = tunnel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var monitorServer = new MonitorServer(_services, _tunnel);

        var serverClient = new ServerClient(monitorServer, _tunnel,
            _services.GetRequiredService<ILogger<ServerClient>>());

        // 认证失败使用带上限的指数退避，其他异常按固定重连间隔
        var authFailureCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitorServer.RegisterNodeAsync(_tunnel, stoppingToken);
                authFailureCount = 0;
                Log.Connected(_logger, _tunnel.Name, _tunnel.ServerUrl);

                await serverClient.TransportCoreAsync(_tunnel, stoppingToken);

                Log.Disconnected(_logger, _tunnel.Name, _tunnel.ReconnectInterval);
                await Task.Delay(TimeSpan.FromSeconds(_tunnel.ReconnectInterval), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                authFailureCount++;
                var backoffSeconds = Math.Min(
                    _tunnel.ReconnectInterval * (1 << Math.Min(authFailureCount - 1, 8)),
                    MaxAuthBackoffSeconds);
                Log.AuthFailed(_logger, authFailureCount, backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken);
            }
            catch (Exception e)
            {
                Log.ConnectError(_logger, e.Message, _tunnel.ReconnectInterval);
                await Task.Delay(TimeSpan.FromSeconds(_tunnel.ReconnectInterval), stoppingToken);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "节点 [{nodeName}] 已连接到服务器 {serverUrl}")]
        public static partial void Connected(ILogger logger, string nodeName, string serverUrl);

        [LoggerMessage(LogLevel.Warning, "节点 [{nodeName}] 与服务器断开，{reconnectInterval}s 后重连")]
        public static partial void Disconnected(ILogger logger, string nodeName, int reconnectInterval);

        [LoggerMessage(LogLevel.Error,
            "认证失败（第 {failureCount} 次）：请检查节点名与密钥是否正确、节点是否被禁用；{backoffSeconds}s 后重试")]
        public static partial void AuthFailed(ILogger logger, int failureCount, int backoffSeconds);

        [LoggerMessage(LogLevel.Error, "连接错误：{message}，{reconnectInterval}s 后重连")]
        public static partial void ConnectError(ILogger logger, string message, int reconnectInterval);
    }
}
