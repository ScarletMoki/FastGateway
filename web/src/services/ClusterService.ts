import { del, get, postJson, post } from "@/utils/fetch";

/** 集群角色：0 独立 / 1 主网关 / 2 从节点 */
export enum ClusterRole {
    Standalone = 0,
    Master = 1,
    Worker = 2,
}

export interface ClusterNode {
    id: string;
    name: string;
    online: boolean;
    lastSeen?: string;
    syncedVersion: number;
    registeredAt: string;
}

export interface ClusterState {
    role: ClusterRole;
    nodeName?: string;
    masterEndpoint?: string;
    syncedVersion: number;
    lastSyncTime?: string;
    connected: boolean;
    nodes: ClusterNode[];
}

export interface GenerateInviteResult {
    code: string;
    endpoint: string;
    expiresAt: string;
}

interface ApiResponse<T> {
    data: T;
    success: boolean;
    message?: string;
}

/** 后端 ResultFilter 把业务失败包装成 200 + success=false，这里统一解包并抛错 */
const unwrap = async <T,>(promise: Promise<ApiResponse<T>>): Promise<T> => {
    const res = await promise;
    if (res && res.success === false) throw new Error(res.message || '操作失败');
    return res?.data as T;
};

/** 获取集群状态 */
export const getClusterState = (): Promise<ClusterState> => {
    return unwrap(get('/api/v1/cluster/state'));
};

/** 生成节点接入码（本网关将成为主网关） */
export const generateInvite = (endpoint: string): Promise<GenerateInviteResult> => {
    return unwrap(postJson('/api/v1/cluster/invite', { endpoint }));
};

/** 凭接入码加入集群（本网关将成为从节点） */
export const joinCluster = (code: string, nodeName: string): Promise<void> => {
    return unwrap(postJson('/api/v1/cluster/join', { code, nodeName }));
};

/** 退出集群 */
export const leaveCluster = (): Promise<void> => {
    return unwrap(post('/api/v1/cluster/leave'));
};

/** 移除从节点 */
export const removeNode = (id: string): Promise<void> => {
    return unwrap(del(`/api/v1/cluster/nodes/${encodeURIComponent(id)}`));
};

/** 立即推送配置到所有节点 */
export const pushConfig = (): Promise<void> => {
    return unwrap(post('/api/v1/cluster/push'));
};

/** 解散集群 */
export const dissolveCluster = (): Promise<void> => {
    return unwrap(post('/api/v1/cluster/dissolve'));
};
