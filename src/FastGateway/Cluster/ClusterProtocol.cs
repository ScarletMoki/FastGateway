using MessagePack;
using MessagePack.Resolvers;

namespace FastGateway.Cluster;

/// <summary>
///     节点间同步通道的二进制编码：[1 字节协议版本] + MessagePack(LZ4BlockArray)。
///     序列化经由源生成器 Resolver（Core / FastGateway 两个程序集），完全 AOT 安全；
///     NativeDateTimeResolver 保留 DateTime 的 Kind 与本地时间，避免跨节点出现 UTC 偏移。
/// </summary>
public static class ClusterProtocol
{
    /// <summary>协议版本号，编码不兼容变更时递增，双端不匹配直接丢弃消息</summary>
    private const byte Version = 2;

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(
            NativeDateTimeResolver.Instance,
            Core.CoreMessagePackResolver.Instance,
            GeneratedMessagePackResolver.Instance,
            StandardResolver.Instance))
        .WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] Encode(ClusterMessage message)
    {
        var body = MessagePackSerializer.Serialize(message, Options);

        var frame = new byte[body.Length + 1];
        frame[0] = Version;
        body.CopyTo(frame, 1);
        return frame;
    }

    public static ClusterMessage? Decode(MemoryStream received)
    {
        if (received.Length < 2) return null;

        var buffer = received.GetBuffer();
        if (buffer[0] != Version) return null;

        try
        {
            var body = new ReadOnlyMemory<byte>(buffer, 1, (int)received.Length - 1);
            return MessagePackSerializer.Deserialize<ClusterMessage>(body, Options);
        }
        catch
        {
            // 损坏或不完整的帧：丢弃，由上层协议（全量快照 + 心跳）自愈
            return null;
        }
    }
}
