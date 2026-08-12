import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Reveal } from '@/components/motion';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table';
import { message } from '@/utils/toast';
import {
    ClusterRole,
    type ClusterState,
    dissolveCluster,
    generateInvite,
    getClusterState,
    joinCluster,
    leaveCluster,
    pushConfig,
    removeNode,
} from '@/services/ClusterService';
import {
    Boxes,
    Copy,
    Crown,
    Link2,
    LogOut,
    RefreshCw,
    Send,
    Trash2,
    Unplug,
} from 'lucide-react';

const formatTime = (value?: string) => (value ? new Date(value).toLocaleString() : '-');

const errMsg = (error: unknown) => (error instanceof Error ? error.message : String(error));

const ClusterPage = () => {
    const [searchParams] = useSearchParams();
    const [state, setState] = useState<ClusterState | null>(null);
    const [loading, setLoading] = useState(false);

    // 主网关：生成接入码
    const [endpoint, setEndpoint] = useState(window.location.origin);
    const [inviteCode, setInviteCode] = useState('');
    const [inviteExpiresAt, setInviteExpiresAt] = useState('');
    const [generating, setGenerating] = useState(false);

    // 从网关：一键加入
    const [joinCode, setJoinCode] = useState(searchParams.get('code') ?? '');
    const [nodeName, setNodeName] = useState('');
    const [joining, setJoining] = useState(false);

    const loadState = useCallback(async () => {
        try {
            setState(await getClusterState());
        } catch (error) {
            message.error(`加载集群状态失败: ${errMsg(error)}`);
        }
    }, []);

    useEffect(() => {
        setLoading(true);
        loadState().finally(() => setLoading(false));

        // 轮询刷新节点在线状态 / 同步进度
        const timer = setInterval(loadState, 5000);
        return () => clearInterval(timer);
    }, [loadState]);

    const handleGenerateInvite = async () => {
        setGenerating(true);
        try {
            const invite = await generateInvite(endpoint);
            setInviteCode(invite.code);
            setInviteExpiresAt(invite.expiresAt);
            message.success('接入码已生成，24 小时内有效');
            loadState();
        } catch (error) {
            message.error(`生成接入码失败: ${errMsg(error)}`);
        } finally {
            setGenerating(false);
        }
    };

    const handleCopy = async (text: string, tips: string) => {
        try {
            await navigator.clipboard.writeText(text);
            message.success(tips);
        } catch {
            message.error('复制失败，请手动复制');
        }
    };

    const handleJoin = async () => {
        if (!joinCode.trim()) {
            message.error('请粘贴主网关生成的接入码');
            return;
        }
        setJoining(true);
        try {
            await joinCluster(joinCode.trim(), nodeName.trim());
            message.success('已加入集群，正在同步主网关配置');
            setJoinCode('');
            loadState();
        } catch (error) {
            message.error(`加入集群失败: ${errMsg(error)}`);
        } finally {
            setJoining(false);
        }
    };

    const handleLeave = async () => {
        try {
            await leaveCluster();
            message.success('已退出集群，本网关恢复独立运行');
            loadState();
        } catch (error) {
            message.error(`退出集群失败: ${errMsg(error)}`);
        }
    };

    const handleRemoveNode = async (id: string, name: string) => {
        try {
            await removeNode(id);
            message.success(`已移除节点 ${name}`);
            loadState();
        } catch (error) {
            message.error(`移除节点失败: ${errMsg(error)}`);
        }
    };

    const handlePush = async () => {
        try {
            await pushConfig();
            message.success('已向所有在线节点推送配置');
        } catch (error) {
            message.error(`推送配置失败: ${errMsg(error)}`);
        }
    };

    const handleDissolve = async () => {
        try {
            await dissolveCluster();
            message.success('集群已解散');
            loadState();
        } catch (error) {
            message.error(`解散集群失败: ${errMsg(error)}`);
        }
    };

    const roleBadge = () => {
        if (!state) return null;
        switch (state.role) {
            case ClusterRole.Master:
                return <Badge className="bg-amber-500 text-amber-50">主网关</Badge>;
            case ClusterRole.Worker:
                return <Badge className="bg-sky-500 text-sky-50">从节点</Badge>;
            default:
                return <Badge variant="secondary">独立运行</Badge>;
        }
    };

    return (
        <div className="p-6 space-y-6">
            <Reveal className="flex items-center justify-between">
                <div>
                    <div className="flex items-center gap-3">
                        <h1 className="text-2xl font-semibold text-foreground">集群管理</h1>
                        {roleBadge()}
                    </div>
                    <p className="text-muted-foreground mt-1">
                        多节点分布式网关：主网关统一管理配置，从节点自动同步并本地转发
                    </p>
                </div>
                <Button
                    onClick={() => loadState()}
                    disabled={loading}
                    className="flex items-center gap-2 bg-primary text-primary-foreground hover:bg-primary/90"
                >
                    <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
                    刷新
                </Button>
            </Reveal>

            <Separator />

            {!state ? (
                <div className="flex justify-center items-center h-64 text-muted-foreground">加载中...</div>
            ) : state.role === ClusterRole.Worker ? (
                /* ===== 从节点视图 ===== */
                <Reveal>
                    <Card className="max-w-2xl">
                        <CardHeader>
                            <div className="flex items-center justify-between">
                                <CardTitle className="flex items-center gap-2">
                                    <Boxes className="h-5 w-5" />
                                    已加入集群
                                </CardTitle>
                                <Badge
                                    variant={state.connected ? 'default' : 'secondary'}
                                    className={state.connected ? 'bg-green-500 text-green-50' : 'bg-muted text-muted-foreground'}
                                >
                                    {state.connected ? '同步通道在线' : '连接中...'}
                                </Badge>
                            </div>
                            <CardDescription>
                                本网关的配置由主网关统一下发，本地修改会在下次同步时被覆盖
                            </CardDescription>
                        </CardHeader>
                        <CardContent className="space-y-4">
                            <div className="grid grid-cols-2 gap-4 text-sm">
                                <div>
                                    <p className="text-muted-foreground">节点名称</p>
                                    <p className="font-medium text-foreground">{state.nodeName || '-'}</p>
                                </div>
                                <div>
                                    <p className="text-muted-foreground">主网关地址</p>
                                    <p className="font-mono text-xs bg-muted px-2 py-1 rounded inline-block text-foreground">
                                        {state.masterEndpoint}
                                    </p>
                                </div>
                                <div>
                                    <p className="text-muted-foreground">已同步配置版本</p>
                                    <p className="font-medium text-foreground">{state.syncedVersion || '-'}</p>
                                </div>
                                <div>
                                    <p className="text-muted-foreground">最近同步时间</p>
                                    <p className="font-medium text-foreground">{formatTime(state.lastSyncTime)}</p>
                                </div>
                            </div>
                            <Separator />
                            <Button variant="destructive" onClick={handleLeave} className="flex items-center gap-2">
                                <LogOut className="h-4 w-4" />
                                退出集群
                            </Button>
                            <p className="text-xs text-muted-foreground">
                                退出后保留最后一次同步的配置，本网关继续独立提供转发服务
                            </p>
                        </CardContent>
                    </Card>
                </Reveal>
            ) : (
                <div className="space-y-6">
                    {/* ===== 主网关 / 独立：生成接入码 ===== */}
                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        <Reveal>
                            <Card className="h-full">
                                <CardHeader>
                                    <CardTitle className="flex items-center gap-2">
                                        <Crown className="h-5 w-5 text-amber-500" />
                                        生成节点接入码
                                    </CardTitle>
                                    <CardDescription>
                                        在其它 FastGateway 的集群页粘贴接入码，即可一键加入本网关并自动同步全部配置
                                    </CardDescription>
                                </CardHeader>
                                <CardContent className="space-y-4">
                                    <div className="space-y-2">
                                        <Label htmlFor="endpoint">本网关对外地址（从节点回连用）</Label>
                                        <Input
                                            id="endpoint"
                                            value={endpoint}
                                            onChange={(e) => setEndpoint(e.target.value)}
                                            placeholder="https://gw-a.example.com:8080"
                                        />
                                    </div>
                                    <Button
                                        onClick={handleGenerateInvite}
                                        disabled={generating}
                                        className="flex items-center gap-2"
                                    >
                                        <Link2 className="h-4 w-4" />
                                        {generating ? '生成中...' : '生成接入码'}
                                    </Button>

                                    {inviteCode && (
                                        <div className="space-y-2 rounded-lg border border-border bg-muted/40 p-3">
                                            <div className="flex items-center justify-between">
                                                <span className="text-sm font-medium text-foreground">接入码</span>
                                                <span className="text-xs text-muted-foreground">
                                                    有效期至 {formatTime(inviteExpiresAt)}
                                                </span>
                                            </div>
                                            <p className="font-mono text-xs break-all bg-background border border-border rounded p-2 text-foreground">
                                                {inviteCode}
                                            </p>
                                            <div className="flex gap-2">
                                                <Button
                                                    variant="outline"
                                                    size="sm"
                                                    onClick={() => handleCopy(inviteCode, '接入码已复制')}
                                                    className="flex items-center gap-1"
                                                >
                                                    <Copy className="h-3.5 w-3.5" />
                                                    复制接入码
                                                </Button>
                                                <Button
                                                    variant="outline"
                                                    size="sm"
                                                    onClick={() =>
                                                        handleCopy(`/cluster?code=${inviteCode}`, '链接后缀已复制，拼接到从网关地址后打开即可')
                                                    }
                                                    className="flex items-center gap-1"
                                                >
                                                    <Copy className="h-3.5 w-3.5" />
                                                    复制一键加入链接后缀
                                                </Button>
                                            </div>
                                            <p className="text-xs text-muted-foreground">
                                                在从网关地址后拼接该后缀打开（如 http://gw-b:8080/cluster?code=...），接入码会自动填入
                                            </p>
                                        </div>
                                    )}
                                </CardContent>
                            </Card>
                        </Reveal>

                        {/* ===== 独立：加入其它集群 ===== */}
                        {state.role === ClusterRole.Standalone && (
                            <Reveal>
                                <Card className="h-full">
                                    <CardHeader>
                                        <CardTitle className="flex items-center gap-2">
                                            <Unplug className="h-5 w-5 text-sky-500" />
                                            加入其它集群
                                        </CardTitle>
                                        <CardDescription>
                                            粘贴主网关生成的接入码，本网关将成为从节点并接管主网关下发的配置
                                        </CardDescription>
                                    </CardHeader>
                                    <CardContent className="space-y-4">
                                        <div className="space-y-2">
                                            <Label htmlFor="joinCode">接入码</Label>
                                            <Input
                                                id="joinCode"
                                                value={joinCode}
                                                onChange={(e) => setJoinCode(e.target.value)}
                                                placeholder="粘贴主网关生成的接入码"
                                            />
                                        </div>
                                        <div className="space-y-2">
                                            <Label htmlFor="nodeName">节点名称（可选，默认主机名）</Label>
                                            <Input
                                                id="nodeName"
                                                value={nodeName}
                                                onChange={(e) => setNodeName(e.target.value)}
                                                placeholder="如 gw-b"
                                            />
                                        </div>
                                        <Button onClick={handleJoin} disabled={joining} className="flex items-center gap-2">
                                            <Boxes className="h-4 w-4" />
                                            {joining ? '加入中...' : '一键加入集群'}
                                        </Button>
                                        <p className="text-xs text-destructive">
                                            注意：加入后本网关现有配置将被主网关配置覆盖
                                        </p>
                                    </CardContent>
                                </Card>
                            </Reveal>
                        )}
                    </div>

                    {/* ===== 主网关：节点列表 ===== */}
                    {state.role === ClusterRole.Master && (
                        <Reveal>
                            <Card>
                                <CardHeader>
                                    <div className="flex items-center justify-between">
                                        <div>
                                            <CardTitle>从节点列表</CardTitle>
                                            <CardDescription>配置变更后自动推送到所有在线节点</CardDescription>
                                        </div>
                                        <div className="flex gap-2">
                                            <Button variant="outline" onClick={handlePush} className="flex items-center gap-2">
                                                <Send className="h-4 w-4" />
                                                立即推送配置
                                            </Button>
                                            <Button variant="destructive" onClick={handleDissolve} className="flex items-center gap-2">
                                                <Trash2 className="h-4 w-4" />
                                                解散集群
                                            </Button>
                                        </div>
                                    </div>
                                </CardHeader>
                                <CardContent>
                                    {state.nodes.length === 0 ? (
                                        <div className="flex flex-col items-center justify-center h-32 text-muted-foreground">
                                            <Boxes className="h-8 w-8 mb-2 opacity-50" />
                                            <p>暂无从节点，生成接入码后在其它网关加入</p>
                                        </div>
                                    ) : (
                                        <Table>
                                            <TableHeader>
                                                <TableRow>
                                                    <TableHead>名称</TableHead>
                                                    <TableHead>状态</TableHead>
                                                    <TableHead>已同步版本</TableHead>
                                                    <TableHead>最后心跳</TableHead>
                                                    <TableHead>注册时间</TableHead>
                                                    <TableHead className="text-right">操作</TableHead>
                                                </TableRow>
                                            </TableHeader>
                                            <TableBody>
                                                {state.nodes.map((node) => (
                                                    <TableRow key={node.id}>
                                                        <TableCell className="font-medium">{node.name}</TableCell>
                                                        <TableCell>
                                                            <Badge
                                                                variant={node.online ? 'default' : 'secondary'}
                                                                className={
                                                                    node.online
                                                                        ? 'bg-green-500 text-green-50'
                                                                        : 'bg-muted text-muted-foreground'
                                                                }
                                                            >
                                                                {node.online ? '在线' : '离线'}
                                                            </Badge>
                                                        </TableCell>
                                                        <TableCell>{node.online ? node.syncedVersion : '-'}</TableCell>
                                                        <TableCell>{node.online ? formatTime(node.lastSeen) : '-'}</TableCell>
                                                        <TableCell>{formatTime(node.registeredAt)}</TableCell>
                                                        <TableCell className="text-right">
                                                            <Button
                                                                variant="ghost"
                                                                size="sm"
                                                                onClick={() => handleRemoveNode(node.id, node.name)}
                                                                className="text-destructive hover:text-destructive/80 hover:bg-destructive/10"
                                                            >
                                                                <Trash2 className="h-4 w-4" />
                                                            </Button>
                                                        </TableCell>
                                                    </TableRow>
                                                ))}
                                            </TableBody>
                                        </Table>
                                    )}
                                </CardContent>
                            </Card>
                        </Reveal>
                    )}
                </div>
            )}
        </div>
    );
};

export default ClusterPage;
