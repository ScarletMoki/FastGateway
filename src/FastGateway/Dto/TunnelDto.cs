namespace FastGateway.Dto;

public class TunnelNodeDto
{
    /// <summary>
    /// 节点名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 节点描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否由旧客户端携带全局 Token 自动注册
    /// </summary>
    public bool AutoRegistered { get; set; }

    /// <summary>
    /// 节点状态（控制连接是否在线）
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后连接时间
    /// </summary>
    public DateTime? LastConnectTime { get; set; }

    /// <summary>
    /// 心跳间隔（秒，客户端上报值）
    /// </summary>
    public int HeartbeatInterval { get; set; }

    /// <summary>
    /// 代理数量
    /// </summary>
    public int ProxyCount { get; set; }

    /// <summary>
    /// 代理配置列表（客户端上报 + 面板覆盖后的生效状态）
    /// </summary>
    public TunnelProxyDto[] Proxies { get; set; } = [];
}

public class TunnelProxyDto
{
    /// <summary>
    /// 稳定标识（内容哈希）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 主机头
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 路由
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// 本地服务地址
    /// </summary>
    public string LocalRemote { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 域名列表
    /// </summary>
    public string[] Domains { get; set; } = [];

    /// <summary>
    /// 客户端上报的启用状态
    /// </summary>
    public bool ReportedEnabled { get; set; }

    /// <summary>
    /// 覆盖后的生效状态
    /// </summary>
    public bool EffectiveEnabled { get; set; }

    /// <summary>
    /// 是否存在面板覆盖
    /// </summary>
    public bool Overridden { get; set; }
}

/// <summary>
/// 创建节点入参
/// </summary>
public class CreateTunnelNodeInput
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 创建节点/重置密钥结果：NodeKey 仅在此时完整返回一次
/// </summary>
public class TunnelNodeKeyDto
{
    public string Name { get; set; } = string.Empty;

    public string NodeKey { get; set; } = string.Empty;
}

/// <summary>
/// 更新节点入参
/// </summary>
public class UpdateTunnelNodeInput
{
    public string? Description { get; set; }

    public bool? Enabled { get; set; }

    /// <summary>
    /// 面板对代理规则启用状态的覆盖（全量替换；null 表示不修改）
    /// </summary>
    public List<TunnelProxyOverrideInput>? ProxyOverrides { get; set; }
}

public class TunnelProxyOverrideInput
{
    public string ProxyId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

/// <summary>
/// 客户端接入配置（面板"接入引导"使用）
/// </summary>
public class TunnelClientConfigDto
{
    /// <summary>
    /// 建议的服务器地址（根据面板访问域名与已启用隧道的网关推导，可能需要用户按实际入口修改）
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// 是否存在已启用隧道的网关服务
    /// </summary>
    public bool HasTunnelServer { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NodeKey { get; set; } = string.Empty;

    /// <summary>
    /// 一行启动命令
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 可下载的 tunnel.json 内容
    /// </summary>
    public string TunnelJson { get; set; } = string.Empty;
}
