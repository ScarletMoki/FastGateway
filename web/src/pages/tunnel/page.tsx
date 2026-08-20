import { useCallback, useEffect, useMemo, useState } from 'react';
import { Reveal, Stagger, StaggerItem, StatusIndicator, CopyButton, TRANSITION } from '@/components/motion';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Separator } from '@/components/ui/separator';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/dialog';
import { message } from '@/utils/toast';
import { deleteTunnel, getTunnelList, type TunnelNode } from '@/services/TunnelService';
import { Clock, Eye, Globe, Network, Plus, RefreshCw, Search, Trash2 } from 'lucide-react';
import { CreateNodeWizard } from './components/CreateNodeWizard';
import { NodeDetailSheet } from './components/NodeDetailSheet';

const POLL_INTERVAL_MS = 10_000;

const formatTime = (value?: string) =>
    value ? new Date(value).toLocaleString('zh-CN') : '从未连接';

const TunnelPage = () => {
    const [nodes, setNodes] = useState<TunnelNode[]>([]);
    const [initialLoading, setInitialLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);
    const [search, setSearch] = useState('');
    const [createOpen, setCreateOpen] = useState(false);
    const [detailNode, setDetailNode] = useState<string | null>(null);
    const [deleteTarget, setDeleteTarget] = useState<TunnelNode | null>(null);
    const [deleting, setDeleting] = useState(false);

    const loadNodes = useCallback(async (silent = false) => {
        if (!silent) setRefreshing(true);
        try {
            const response = await getTunnelList();
            setNodes(response.data || []);
        } catch (error) {
            if (!silent)
                message.error(`加载节点列表失败: ${error instanceof Error ? error.message : String(error)}`);
        } finally {
            setInitialLoading(false);
            if (!silent) setRefreshing(false);
        }
    }, []);

    // 初次加载 + 10s 轮询在线状态
    useEffect(() => {
        loadNodes(true);
        const timer = setInterval(() => loadNodes(true), POLL_INTERVAL_MS);
        return () => clearInterval(timer);
    }, [loadNodes]);

    const filteredNodes = useMemo(() => {
        const keyword = search.trim().toLowerCase();
        if (!keyword) return nodes;
        return nodes.filter(
            (node) =>
                node.name.toLowerCase().includes(keyword) ||
                node.description.toLowerCase().includes(keyword),
        );
    }, [nodes, search]);

    const handleDelete = async () => {
        if (!deleteTarget) return;
        setDeleting(true);
        try {
            await deleteTunnel(deleteTarget.name);
            message.success(`节点 ${deleteTarget.name} 已删除`);
            setDeleteTarget(null);
            loadNodes(true);
        } catch (error) {
            message.error(`删除节点失败: ${error instanceof Error ? error.message : String(error)}`);
        } finally {
            setDeleting(false);
        }
    };

    return (
        <div className="space-y-6 p-6">
            {/* 页面头部 */}
            <Reveal className="flex flex-wrap items-center justify-between gap-3">
                <div className="space-y-1">
                    <div className="flex items-center gap-2">
                        <Network className="h-6 w-6 text-primary" />
                        <h1 className="text-2xl font-bold tracking-tight text-foreground">节点管理</h1>
                    </div>
                    <p className="text-sm text-muted-foreground">
                        创建隧道节点、生成接入密钥，管理客户端上报的代理规则
                    </p>
                </div>
                <div className="flex items-center gap-2">
                    <div className="relative">
                        <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                        <Input
                            className="w-48 pl-8"
                            placeholder="搜索节点..."
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                        />
                    </div>
                    <Button variant="outline" size="icon" onClick={() => loadNodes()} disabled={refreshing}>
                        <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
                    </Button>
                    <Button onClick={() => setCreateOpen(true)}>
                        <Plus className="mr-1.5 h-4 w-4" />
                        创建节点
                    </Button>
                </div>
            </Reveal>

            <Separator />

            {/* 节点列表 */}
            {initialLoading ? (
                <div className="flex h-64 items-center justify-center">
                    <div className="text-muted-foreground">加载中...</div>
                </div>
            ) : nodes.length === 0 ? (
                <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border py-16 text-center">
                    <Network className="mb-4 h-12 w-12 text-muted-foreground opacity-50" />
                    <h2 className="text-lg font-medium text-foreground">还没有隧道节点</h2>
                    <p className="mt-2 max-w-md text-sm text-muted-foreground">
                        隧道用于把内网服务安全地暴露到网关：在此创建节点并获取密钥，
                        然后在内网机器上运行 TunnelClient 即可接入。
                        接入入口需要某个服务开启「启用隧道」。
                    </p>
                    <Button className="mt-6" onClick={() => setCreateOpen(true)}>
                        <Plus className="mr-2 h-4 w-4" />
                        创建第一个节点
                    </Button>
                </div>
            ) : filteredNodes.length === 0 ? (
                <div className="flex h-64 flex-col items-center justify-center text-muted-foreground">
                    <Search className="mb-4 h-10 w-10 opacity-50" />
                    <p>没有匹配“{search}”的节点</p>
                </div>
            ) : (
                <Stagger className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
                    {filteredNodes.map((node) => (
                        <StaggerItem
                            key={node.name}
                            layout
                            hoverLift
                            transition={TRANSITION.layout}
                            className="h-full rounded-xl"
                        >
                            <Card className="group h-full border-border/70 bg-card/90 transition-all hover:border-primary/40 hover:shadow-md">
                                <CardHeader className="pb-3">
                                    <div className="flex items-center justify-between gap-2">
                                        <CardTitle className="flex min-w-0 items-center gap-2 text-base font-semibold text-foreground">
                                            <StatusIndicator
                                                status={node.isOnline ? "online" : "offline"}
                                                size="sm"
                                            />
                                            <span className="truncate group-hover:text-primary transition-colors">{node.name}</span>
                                            <CopyButton text={node.name} className="h-6 w-6 text-muted-foreground" successMessage="已复制节点名称" />
                                        </CardTitle>
                                        <Button
                                            variant="ghost"
                                            size="sm"
                                            onClick={() => setDeleteTarget(node)}
                                            className="h-8 w-8 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                                        >
                                            <Trash2 className="h-4 w-4" />
                                        </Button>
                                    </div>
                                    <p className="truncate text-xs text-muted-foreground">
                                        {node.description || '暂无描述'}
                                    </p>
                                </CardHeader>

                                <CardContent className="pt-0 space-y-4">
                                    <div className="space-y-1.5 rounded-lg border border-border/50 bg-muted/20 p-2.5 text-xs">
                                        <div className="flex items-center justify-between">
                                            <span className="flex items-center gap-1.5 text-muted-foreground">
                                                <Clock className="h-3.5 w-3.5" />
                                                最近连接
                                            </span>
                                            <span className="font-mono text-foreground">
                                                {formatTime(node.lastConnectTime)}
                                            </span>
                                        </div>
                                        <div className="flex items-center justify-between">
                                            <span className="flex items-center gap-1.5 text-muted-foreground">
                                                <Globe className="h-3.5 w-3.5" />
                                                代理规则
                                            </span>
                                            <Badge variant="outline" className="border-border font-mono text-[11px]">
                                                {node.proxyCount} 条
                                            </Badge>
                                        </div>
                                    </div>

                                    <div className="flex items-center justify-between pt-1">
                                        <div className="flex items-center gap-1.5">
                                            <Badge
                                                variant={node.isOnline ? 'default' : 'secondary'}
                                                className={
                                                    node.isOnline
                                                        ? 'bg-emerald-500 text-emerald-50 h-5 text-[11px]'
                                                        : 'bg-muted text-muted-foreground h-5 text-[11px]'
                                                }
                                            >
                                                {node.isOnline ? '在线' : '离线'}
                                            </Badge>
                                            {!node.enabled && <Badge variant="destructive" className="h-5 text-[11px]">已禁用</Badge>}
                                            {node.autoRegistered && <Badge variant="outline" className="h-5 text-[11px]">自动注册</Badge>}
                                        </div>
                                        <Button
                                            variant="ghost"
                                            size="sm"
                                            onClick={() => setDetailNode(node.name)}
                                            className="h-7 text-xs text-primary hover:bg-primary/10 hover:text-primary"
                                        >
                                            <Eye className="mr-1 h-3.5 w-3.5" />
                                            详情与规则
                                        </Button>
                                    </div>
                                </CardContent>
                            </Card>
                        </StaggerItem>
                    ))}
                </Stagger>
            )}

            {/* 创建节点向导 */}
            <CreateNodeWizard
                open={createOpen}
                onClose={() => setCreateOpen(false)}
                onCreated={() => loadNodes(true)}
            />

            {/* 详情抽屉 */}
            <NodeDetailSheet
                nodeName={detailNode}
                open={detailNode !== null}
                onClose={() => setDetailNode(null)}
                onChanged={() => loadNodes(true)}
            />

            {/* 删除二次确认 */}
            <Dialog open={deleteTarget !== null} onOpenChange={(next) => !next && setDeleteTarget(null)}>
                <DialogContent className="sm:max-w-md">
                    <DialogHeader>
                        <DialogTitle>删除节点</DialogTitle>
                        <DialogDescription>
                            确认删除节点「{deleteTarget?.name}」？在线客户端将被断开，其代理路由会立即清理，且节点密钥不可恢复。
                        </DialogDescription>
                    </DialogHeader>
                    <div className="flex justify-end gap-2">
                        <Button variant="outline" onClick={() => setDeleteTarget(null)}>
                            取消
                        </Button>
                        <Button variant="destructive" onClick={handleDelete} disabled={deleting}>
                            {deleting ? '删除中...' : '确认删除'}
                        </Button>
                    </div>
                </DialogContent>
            </Dialog>
        </div>
    );
};

export default TunnelPage;
