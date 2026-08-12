using System.Net.WebSockets;
using FastGateway.Services;

namespace FastGateway.Cluster;

/// <summary>
///     从节点代理：角色为 Worker 时持续维护到主网关的同步 WebSocket，
///     接收配置快照并应用，断线自动重连。加入/退出集群无需重启进程。
/// </summary>
public class ClusterNodeAgentService(
    ClusterStateService stateService,
    ConfigurationService configService) : BackgroundService
{
    private static volatile bool _connected;
    private static CancellationTokenSource? _connectionCts;

    /// <summary>ClientWebSocket 不允许并发发送：心跳与配置 ACK 可能同时触发，必须串行化</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>当前与主网关的同步通道是否在线（供状态接口展示）</summary>
    public static bool Connected => _connected;

    /// <summary>退出集群时立即断开当前连接</summary>
    public static void RequestDisconnect()
    {
        _connectionCts?.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var state = stateService.Get();

            if (state.Role != ClusterRole.Worker
                || string.IsNullOrEmpty(state.MasterEndpoint)
                || string.IsNullOrEmpty(state.NodeId)
                || string.IsNullOrEmpty(state.NodeToken))
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                continue;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _connectionCts = cts;

            try
            {
                await ConnectAndSyncAsync(state, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 停机或主动断开
            }
            catch (Exception ex)
            {
                Console.WriteLine($"集群同步连接中断：{ex.Message}");
            }
            finally
            {
                _connected = false;
                _connectionCts = null;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ConnectAndSyncAsync(ClusterState state, CancellationToken cancellationToken)
    {
        var endpoint = state.MasterEndpoint!.TrimEnd('/');
        var wsScheme = endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var uri = new Uri(
            $"{wsScheme}{endpoint[endpoint.IndexOf("://", StringComparison.Ordinal)..]}/api/v1/cluster/ws" +
            $"?nodeId={Uri.EscapeDataString(state.NodeId!)}&token={Uri.EscapeDataString(state.NodeToken!)}");

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await socket.ConnectAsync(uri, cancellationToken);
        _connected = true;
        Console.WriteLine($"已连接主网关：{endpoint}");

        // 应用层心跳：维持 LastSeen，同时探测半开连接
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pingTask = Task.Run(async () =>
        {
            while (!pingCts.Token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), pingCts.Token);
                await SendAsync(socket, new ClusterMessage { Type = ClusterMessage.TypePing }, pingCts.Token);
            }
        }, pingCts.Token);

        try
        {
            await ReceiveLoopAsync(socket, cancellationToken);
        }
        finally
        {
            pingCts.Cancel();
            try
            {
                await pingTask;
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            stream.SetLength(0);

            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var message = ClusterProtocol.Decode(stream);
            if (message == null) continue;

            switch (message.Type)
            {
                case ClusterMessage.TypeConfig when message.Config != null:
                    await HandleConfigAsync(socket, message, cancellationToken);
                    break;

                case ClusterMessage.TypeRemoved:
                    // 主网关移除了本节点：自动退出集群，保留最后一份配置继续服务
                    Console.WriteLine("本节点已被主网关移除，自动退出集群");
                    stateService.Update(s =>
                    {
                        s.Role = ClusterRole.Standalone;
                        s.MasterEndpoint = null;
                        s.NodeId = null;
                        s.NodeToken = null;
                    });
                    return;
            }
        }
    }

    private async Task HandleConfigAsync(ClientWebSocket socket, ClusterMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await ClusterConfigApplier.ApplyAsync(message.Config!, configService);

            stateService.Update(s =>
            {
                s.SyncedVersion = message.Version;
                s.LastSyncTime = DateTime.Now;
            });

            await SendAsync(socket, new ClusterMessage { Type = ClusterMessage.TypeAck, Version = message.Version },
                cancellationToken);

            Console.WriteLine($"已应用主网关配置（版本 {message.Version}）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"应用主网关配置失败：{ex}");
        }
    }

    private async Task SendAsync(ClientWebSocket socket, ClusterMessage message,
        CancellationToken cancellationToken)
    {
        var bytes = ClusterProtocol.Encode(message);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
