using MessagePack;

namespace Core;

/// <summary>
///     Core 程序集的 MessagePack 源生成 Resolver（显式声明为 public，供 FastGateway 集群协议跨程序集组合）。
///     覆盖本程序集所有 [MessagePackObject] 实体，完全 AOT 安全。
/// </summary>
[GeneratedMessagePackResolver]
public partial class CoreMessagePackResolver;
