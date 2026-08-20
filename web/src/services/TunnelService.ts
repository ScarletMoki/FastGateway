import { del, get, postJson, putJson } from "@/utils/fetch";

const baseUrl = "/api/v1/tunnel";

export interface TunnelProxy {
    id: string;
    host?: string;
    route: string;
    localRemote: string;
    description: string;
    domains: string[];
    /** 客户端上报的启用状态 */
    reportedEnabled: boolean;
    /** 面板覆盖后的生效状态 */
    effectiveEnabled: boolean;
    /** 是否存在面板覆盖 */
    overridden: boolean;
}

export interface TunnelNode {
    name: string;
    description: string;
    enabled: boolean;
    autoRegistered: boolean;
    isOnline: boolean;
    createdAt: string;
    lastConnectTime?: string;
    heartbeatInterval: number;
    proxyCount: number;
    proxies: TunnelProxy[];
}

export interface TunnelNodeKey {
    name: string;
    nodeKey: string;
}

export interface TunnelClientConfig {
    serverUrl: string;
    hasTunnelServer: boolean;
    name: string;
    nodeKey: string;
    command: string;
    tunnelJson: string;
}

export interface CreateTunnelNodeInput {
    name: string;
    description: string;
}

export interface TunnelProxyOverrideInput {
    proxyId: string;
    enabled: boolean;
}

export interface UpdateTunnelNodeInput {
    description?: string;
    enabled?: boolean;
    proxyOverrides?: TunnelProxyOverrideInput[];
}

export interface ApiResponse<T> {
    data: T;
    success: boolean;
    message?: string;
}

/** 获取节点列表 */
export const getTunnelList = (): Promise<ApiResponse<TunnelNode[]>> => {
    return get(baseUrl);
};

/** 获取节点详情 */
export const getTunnelDetail = (name: string): Promise<ApiResponse<TunnelNode>> => {
    return get(`${baseUrl}/${encodeURIComponent(name)}`);
};

/** 创建节点（NodeKey 仅此时完整返回一次） */
export const createTunnelNode = (
    input: CreateTunnelNodeInput,
): Promise<ApiResponse<TunnelNodeKey>> => {
    return postJson(baseUrl, input);
};

/** 更新节点（描述 / 启用状态 / 代理覆盖） */
export const updateTunnelNode = (
    name: string,
    input: UpdateTunnelNodeInput,
): Promise<ApiResponse<void>> => {
    return putJson(`${baseUrl}/${encodeURIComponent(name)}`, input);
};

/** 删除节点（同时踢下线并清理路由） */
export const deleteTunnel = (name: string): Promise<ApiResponse<void>> => {
    return del(`${baseUrl}/${encodeURIComponent(name)}`);
};

/** 重新生成节点密钥（旧密钥立即失效，NodeKey 仅此时完整返回一次） */
export const regenerateTunnelNodeKey = (
    name: string,
): Promise<ApiResponse<TunnelNodeKey>> => {
    return postJson(`${baseUrl}/${encodeURIComponent(name)}/regenerate-key`, {});
};

/** 获取客户端接入配置（一行命令 / tunnel.json） */
export const getTunnelClientConfig = (
    name: string,
): Promise<ApiResponse<TunnelClientConfig>> => {
    return get(`${baseUrl}/${encodeURIComponent(name)}/client-config`);
};
