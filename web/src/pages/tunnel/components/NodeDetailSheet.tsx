import { useCallback, useEffect, useState } from 'react';
import {
    Sheet,
    SheetContent,
    SheetDescription,
    SheetHeader,
    SheetTitle,
} from '@/components/ui/sheet';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';
import { Switch } from '@/components/ui/switch';
import { message } from '@/utils/toast';
import {
    getTunnelClientConfig,
    getTunnelDetail,
    regenerateTunnelNodeKey,
    updateTunnelNode,
    type TunnelNode,
    type TunnelProxyOverrideInput,
} from '@/services/TunnelService';
import { Download, KeyRound, Loader2, RefreshCw } from 'lucide-react';
import { CopyButton, StatusIndicator } from '@/components/motion';

interface NodeDetailSheetProps {
    nodeName: string | null;
    open: boolean;
    onClose: () => void;
    onChanged: () => void;
}

const formatTime = (value?: string) =>
    value ? new Date(value).toLocaleString('zh-CN') : '—';

/**
 * 节点详情抽屉：连接信息、密钥管理、上报的代理规则与逐条启用覆盖。
 */
export const NodeDetailSheet = ({ nodeName, open, onClose, onChanged }: NodeDetailSheetProps) => {
    const [node, setNode] = useState<TunnelNode | null>(null);
    const [loading, setLoading] = useState(false);
    const [command, setCommand] = useState('');
    const [tunnelJson, setTunnelJson] = useState('');
    const [regeneratedKey, setRegeneratedKey] = useState('');
    const [confirmRegenerate, setConfirmRegenerate] = useState(false);
    const [regenerating, setRegenerating] = useState(false);

    const loadDetail = useCallback(async () => {
        if (!nodeName) return;
        try {
            const response = await getTunnelDetail(nodeName);
            setNode(response.data);
        } catch (error) {
            message.error(`加载节点详情失败: ${error instanceof Error ? error.message : String(error)}`);
        }
    }, [nodeName]);

    useEffect(() => {
        if (!open || !nodeName) {
            setNode(null);
            setCommand('');
            setTunnelJson('');
            setRegeneratedKey('');
            setConfirmRegenerate(false);
            return;
        }

        setLoading(true);
        Promise.all([
            loadDetail(),
            getTunnelClientConfig(nodeName)
                .then((response) => {
                    setCommand(response.data.command);
                    setTunnelJson(response.data.tunnelJson);
                })
                .catch(() => undefined),
        ]).finally(() => setLoading(false));
    }, [open, nodeName, loadDetail]);

    const handleToggleEnabled = async (enabled: boolean) => {
        if (!node) return;
        try {
            await updateTunnelNode(node.name, { enabled });
            message.success(enabled ? '节点已启用' : '节点已禁用并踢下线');
            await loadDetail();
            onChanged();
        } catch (error) {
            message.error(`更新失败: ${error instanceof Error ? error.message : String(error)}`);
        }
    };

    const handleToggleProxy = async (proxyId: string, enabled: boolean) => {
        if (!node) return;

        // 覆盖列表全量替换：与上报状态一致的条目移除覆盖，其余保留
        const overrides: TunnelProxyOverrideInput[] = node.proxies
            .map((proxy) => {
                const nextEnabled = proxy.id === proxyId ? enabled : proxy.effectiveEnabled;
                return { proxy, nextEnabled };
            })
            .filter(({ proxy, nextEnabled }) => nextEnabled !== proxy.reportedEnabled)
            .map(({ proxy, nextEnabled }) => ({ proxyId: proxy.id, enabled: nextEnabled }));

        try {
            await updateTunnelNode(node.name, { proxyOverrides: overrides });
            message.success('代理规则状态已更新');
            await loadDetail();
            onChanged();
        } catch (error) {
            message.error(`更新失败: ${error instanceof Error ? error.message : String(error)}`);
        }
    };

    const handleRegenerateKey = async () => {
        if (!node) return;
        setRegenerating(true);
        try {
            const response = await regenerateTunnelNodeKey(node.name);
            setRegeneratedKey(response.data.nodeKey);
            setConfirmRegenerate(false);
            message.success('密钥已重置，旧密钥立即失效');
            // 重置后接入命令随之变化，刷新接入信息
            const config = await getTunnelClientConfig(node.name);
            setCommand(config.data.command);
            setTunnelJson(config.data.tunnelJson);
            onChanged();
        } catch (error) {
            message.error(`重置密钥失败: ${error instanceof Error ? error.message : String(error)}`);
        } finally {
            setRegenerating(false);
        }
    };

    const handleDownloadJson = () => {
        if (!tunnelJson) return;
        const blob = new Blob([tunnelJson], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'tunnel.json';
        link.click();
        URL.revokeObjectURL(url);
    };

    return (
        <Sheet open={open} onOpenChange={(next) => !next && onClose()}>
            <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-xl">
                <SheetHeader>
                    <SheetTitle className="flex items-center gap-2">
                        <span>{nodeName}</span>
                        {node && (
                            <StatusIndicator
                                status={node.isOnline ? "online" : "offline"}
                                label={node.isOnline ? "在线" : "离线"}
                                size="sm"
                            />
                        )}
                        {node?.autoRegistered && <Badge variant="outline" className="text-xs">自动注册</Badge>}
                    </SheetTitle>
                    <SheetDescription>{node?.description || '暂无描述'}</SheetDescription>
                </SheetHeader>

                {loading || !node ? (
                    <div className="flex h-40 items-center justify-center text-muted-foreground">
                        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                        加载中...
                    </div>
                ) : (
                    <div className="space-y-6 py-4">
                        {/* 连接信息 */}
                        <div className="space-y-3 rounded-lg border border-border/60 bg-muted/20 p-3 text-sm">
                            <div className="flex items-center justify-between">
                                <span className="text-muted-foreground">节点启用</span>
                                <Switch checked={node.enabled} onCheckedChange={handleToggleEnabled} />
                            </div>
                            <div className="flex items-center justify-between">
                                <span className="text-muted-foreground">最近连接时间</span>
                                <span className="font-medium text-foreground">{formatTime(node.lastConnectTime)}</span>
                            </div>
                            <div className="flex items-center justify-between">
                                <span className="text-muted-foreground">创建时间</span>
                                <span className="text-foreground">{formatTime(node.createdAt)}</span>
                            </div>
                            <div className="flex items-center justify-between">
                                <span className="text-muted-foreground">心跳间隔</span>
                                <span className="text-foreground">{node.heartbeatInterval > 0 ? `${node.heartbeatInterval}s` : '—'}</span>
                            </div>
                        </div>

                        <Separator />

                        {/* 密钥管理 */}
                        <div className="space-y-3">
                            <div className="flex items-center gap-2 text-sm font-medium">
                                <KeyRound className="h-4 w-4 text-amber-500" />
                                密钥管理
                            </div>
                            {regeneratedKey ? (
                                <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3">
                                    <p className="mb-2 text-xs text-muted-foreground">
                                        新密钥仅此处完整展示一次，请立即更新客户端配置：
                                    </p>
                                    <div className="flex items-center gap-2">
                                        <code className="flex-1 break-all rounded bg-background px-2 py-1.5 font-mono text-xs border border-border/60">
                                            {regeneratedKey}
                                        </code>
                                        <CopyButton text={regeneratedKey} variant="outline" size="sm" successMessage="已复制新密钥" />
                                    </div>
                                </div>
                            ) : (
                                <p className="text-xs text-muted-foreground">
                                    密钥仅在创建或重置时完整展示一次；如已泄露或遗失，可重置密钥（旧密钥立即失效并踢下线）。
                                </p>
                            )}
                            {confirmRegenerate ? (
                                <div className="flex items-center gap-2">
                                    <span className="text-xs text-destructive font-medium">确认重置？在线客户端将被断开</span>
                                    <Button
                                        variant="destructive"
                                        size="sm"
                                        onClick={handleRegenerateKey}
                                        disabled={regenerating}
                                    >
                                        {regenerating && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
                                        确认重置
                                    </Button>
                                    <Button variant="outline" size="sm" onClick={() => setConfirmRegenerate(false)}>
                                        取消
                                    </Button>
                                </div>
                            ) : (
                                <Button variant="outline" size="sm" onClick={() => setConfirmRegenerate(true)}>
                                    <RefreshCw className="mr-2 h-4 w-4" />
                                    重置密钥
                                </Button>
                            )}
                        </div>

                        <Separator />

                        {/* 接入信息 */}
                        <div className="space-y-3">
                            <div className="text-sm font-medium">接入信息</div>
                            <div className="flex items-start gap-2">
                                <code className="flex-1 break-all rounded bg-muted/70 px-2.5 py-2 font-mono text-xs border border-border/50">
                                    {command || '—'}
                                </code>
                                <CopyButton text={command} variant="outline" size="sm" successMessage="已复制接入命令" />
                            </div>
                            <Button variant="outline" size="sm" onClick={handleDownloadJson} disabled={!tunnelJson}>
                                <Download className="mr-2 h-4 w-4" />
                                下载 tunnel.json
                            </Button>
                        </div>

                        <Separator />

                        {/* 代理规则 */}
                        <div className="space-y-3">
                            <div className="text-sm font-medium">
                                代理规则（客户端上报，共 {node.proxies.length} 条）
                            </div>
                            {node.proxies.length === 0 ? (
                                <p className="text-xs text-muted-foreground">
                                    客户端尚未上报代理规则；请在客户端 tunnel.json 中配置 Proxy 后重启客户端。
                                </p>
                            ) : (
                                node.proxies.map((proxy) => (
                                    <div key={proxy.id} className="rounded-lg border border-border/70 bg-card/60 p-3 shadow-2xs">
                                        <div className="flex items-center justify-between gap-2">
                                            <div className="min-w-0 flex-1">
                                                <div className="flex flex-wrap items-center gap-2 text-sm">
                                                    <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs font-semibold text-primary">
                                                        {proxy.route || '/'}
                                                    </code>
                                                    <span className="text-muted-foreground">→</span>
                                                    <code className="break-all rounded bg-muted px-1.5 py-0.5 font-mono text-xs text-foreground">
                                                        {proxy.localRemote}
                                                    </code>
                                                    {proxy.overridden && (
                                                        <Badge variant="outline" className="text-[10px] text-amber-600 border-amber-500/30">已覆盖</Badge>
                                                    )}
                                                </div>
                                                {proxy.domains.length > 0 && (
                                                    <p className="mt-1.5 truncate text-xs text-muted-foreground">
                                                        域名：{proxy.domains.join('、')}
                                                    </p>
                                                )}
                                                {proxy.description && (
                                                    <p className="mt-1 truncate text-xs text-muted-foreground">
                                                        {proxy.description}
                                                    </p>
                                                )}
                                            </div>
                                            <Switch
                                                checked={proxy.effectiveEnabled}
                                                onCheckedChange={(next) => handleToggleProxy(proxy.id, next)}
                                            />
                                        </div>
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                )}
            </SheetContent>
        </Sheet>
    );
};
