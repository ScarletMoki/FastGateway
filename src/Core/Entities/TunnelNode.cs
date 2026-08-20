using MessagePack;

namespace Core.Entities;

/// <summary>
///     隧道节点：面板创建后持久化，客户端凭 节点名 + NodeKey 接入。
/// </summary>
[MessagePackObject(true)]
public sealed class TunnelNode
{
    /// <summary>
    ///     节点名称（唯一标识，用于构建内部主机名 node_{Name}）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     节点描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     节点级密钥，客户端接入凭据
    /// </summary>
    public string NodeKey { get; set; } = string.Empty;

    /// <summary>
    ///     是否启用；禁用后拒绝该节点注册与连接
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     是否由旧客户端携带全局 TunnelToken 自动注册
    /// </summary>
    public bool AutoRegistered { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     最近一次控制连接建立/断开时间
    /// </summary>
    public DateTime? LastConnectTime { get; set; }

    /// <summary>
    ///     客户端上报的心跳间隔（秒），服务端钳制后生效
    /// </summary>
    public int HeartbeatInterval { get; set; }

    /// <summary>
    ///     客户端最近一次上报的代理规则（用于面板展示与重启后回显）
    /// </summary>
    public List<TunnelNodeProxy> ReportedProxies { get; set; } = new();

    /// <summary>
    ///     面板对代理规则启用状态的覆盖（按代理稳定 Id）
    /// </summary>
    public List<TunnelProxyOverride> ProxyOverrides { get; set; } = new();
}

/// <summary>
///     节点上报的单条代理规则快照
/// </summary>
[MessagePackObject(true)]
public sealed class TunnelNodeProxy
{
    /// <summary>
    ///     稳定 Id：由 Host/Route/Domains/LocalRemote 内容哈希生成，跨注册保持一致
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string? Host { get; set; }

    public string Route { get; set; } = string.Empty;

    public string LocalRemote { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string[] Domains { get; set; } = [];

    /// <summary>
    ///     客户端上报的启用状态
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
///     面板对单条代理规则的启用状态覆盖；存在覆盖时以覆盖为准
/// </summary>
[MessagePackObject(true)]
public sealed class TunnelProxyOverride
{
    public string ProxyId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}
