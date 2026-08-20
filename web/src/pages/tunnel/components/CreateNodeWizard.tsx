import { useCallback, useEffect, useRef, useState } from 'react';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Badge } from '@/components/ui/badge';
import { message } from '@/utils/toast';
import {
    createTunnelNode,
    getTunnelClientConfig,
    getTunnelDetail,
    type TunnelClientConfig,
} from '@/services/TunnelService';
import { CheckCircle2, Download, KeyRound, Loader2 } from 'lucide-react';
import { CopyButton, StepTransition, StatusIndicator } from '@/components/motion';

interface CreateNodeWizardProps {
    open: boolean;
    onClose: () => void;
    onCreated: () => void;
}

/**
 * 创建节点向导：
 * 第 1 步：填写名称/描述；第 2 步：一次性展示 NodeKey 与三种接入方式，并等待节点上线。
 */
export const CreateNodeWizard = ({ open, onClose, onCreated }: CreateNodeWizardProps) => {
    const [step, setStep] = useState<1 | 2>(1);
    const [name, setName] = useState('');
    const [description, setDescription] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [nodeKey, setNodeKey] = useState('');
    const [clientConfig, setClientConfig] = useState<TunnelClientConfig | null>(null);
    const [online, setOnline] = useState(false);
    const pollTimer = useRef<ReturnType<typeof setInterval> | null>(null);

    const stopPolling = useCallback(() => {
        if (pollTimer.current) {
            clearInterval(pollTimer.current);
            pollTimer.current = null;
        }
    }, []);

    // 关闭时重置向导状态
    useEffect(() => {
        if (!open) {
            stopPolling();
            setStep(1);
            setName('');
            setDescription('');
            setNodeKey('');
            setClientConfig(null);
            setOnline(false);
        }
        return stopPolling;
    }, [open, stopPolling]);

    // 第 2 步：每 3 秒轮询节点是否上线
    useEffect(() => {
        if (step !== 2 || online || !name) return;

        pollTimer.current = setInterval(async () => {
            try {
                const response = await getTunnelDetail(name);
                if (response.data?.isOnline) {
                    setOnline(true);
                    stopPolling();
                }
            } catch {
                // 轮询失败静默重试
            }
        }, 3000);

        return stopPolling;
    }, [step, online, name, stopPolling]);

    const handleCreate = async () => {
        const trimmedName = name.trim();
        if (!trimmedName) {
            message.error('请输入节点名称');
            return;
        }
        if (!/^[a-zA-Z0-9_-]{1,64}$/.test(trimmedName)) {
            message.error('节点名仅支持字母、数字、-、_，最长 64 字符');
            return;
        }

        setSubmitting(true);
        try {
            const created = await createTunnelNode({
                name: trimmedName,
                description: description.trim(),
            });
            setName(created.data.name);
            setNodeKey(created.data.nodeKey);

            const config = await getTunnelClientConfig(created.data.name);
            setClientConfig(config.data);

            setStep(2);
            onCreated();
        } catch (error) {
            message.error(`创建节点失败: ${error instanceof Error ? error.message : String(error)}`);
        } finally {
            setSubmitting(false);
        }
    };

    const handleDownloadJson = () => {
        if (!clientConfig) return;
        const blob = new Blob([clientConfig.tunnelJson], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'tunnel.json';
        link.click();
        URL.revokeObjectURL(url);
    };

    return (
        <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
            <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
                <StepTransition currentStep={step} direction="forward">
                    {step === 1 ? (
                        <div className="space-y-4">
                            <DialogHeader>
                                <DialogTitle>创建节点</DialogTitle>
                                <DialogDescription>
                                    创建后将生成节点密钥，客户端凭 服务器地址 + 节点名 + 密钥 即可接入。
                                </DialogDescription>
                            </DialogHeader>
                            <div className="space-y-4 py-2">
                                <div className="space-y-2">
                                    <Label htmlFor="node-name">节点名称</Label>
                                    <Input
                                        id="node-name"
                                        placeholder="例如：home-server"
                                        value={name}
                                        onChange={(e) => setName(e.target.value)}
                                    />
                                    <p className="text-xs text-muted-foreground">
                                        仅支持字母、数字、-、_，最长 64 字符；创建后不可修改。
                                    </p>
                                </div>
                                <div className="space-y-2">
                                    <Label htmlFor="node-description">描述（可选）</Label>
                                    <Textarea
                                        id="node-description"
                                        placeholder="例如：家里的开发机"
                                        value={description}
                                        onChange={(e) => setDescription(e.target.value)}
                                    />
                                </div>
                            </div>
                            <div className="flex justify-end gap-2 pt-2">
                                <Button variant="outline" onClick={onClose}>取消</Button>
                                <Button onClick={handleCreate} disabled={submitting}>
                                    {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                                    创建并生成密钥
                                </Button>
                            </div>
                        </div>
                    ) : (
                        <div className="space-y-4">
                            <DialogHeader>
                                <DialogTitle className="flex items-center gap-2">
                                    <span>节点 {name} 已创建</span>
                                    {online ? (
                                        <Badge className="bg-emerald-500 text-emerald-50">
                                            <CheckCircle2 className="mr-1 h-3 w-3" />
                                            已上线
                                        </Badge>
                                    ) : (
                                        <div className="flex items-center gap-1.5">
                                            <StatusIndicator status="busy" label="等待客户端接入..." size="sm" />
                                        </div>
                                    )}
                                </DialogTitle>
                                <DialogDescription>
                                    请立即保存节点密钥：出于安全考虑，密钥仅此处完整展示一次。
                                </DialogDescription>
                            </DialogHeader>

                            <div className="space-y-4 py-2">
                                <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3">
                                    <div className="mb-2 flex items-center gap-2 text-sm font-medium text-amber-700 dark:text-amber-400">
                                        <KeyRound className="h-4 w-4" />
                                        节点密钥（NodeKey）
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <code className="flex-1 break-all rounded bg-background/80 px-2.5 py-1.5 font-mono text-xs border border-border/60">
                                            {nodeKey}
                                        </code>
                                        <CopyButton text={nodeKey} variant="outline" size="sm" successMessage="已复制节点密钥" />
                                    </div>
                                </div>

                                {clientConfig && !clientConfig.hasTunnelServer && (
                                    <div className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
                                        当前没有已启用隧道的网关服务：请先在「服务管理」中为某个服务开启「启用隧道」并重启该服务，客户端才能接入。
                                    </div>
                                )}

                                <Tabs defaultValue="command">
                                    <TabsList className="grid w-full grid-cols-3">
                                        <TabsTrigger value="command">一行命令</TabsTrigger>
                                        <TabsTrigger value="json">tunnel.json</TabsTrigger>
                                        <TabsTrigger value="binary">下载客户端</TabsTrigger>
                                    </TabsList>
                                    <TabsContent value="command" className="space-y-2 mt-3">
                                        <p className="text-xs text-muted-foreground">
                                            下载 TunnelClient 后执行以下命令即可接入（可用 --proxy 域名=本地地址 追加代理规则）：
                                        </p>
                                        <div className="flex items-start gap-2">
                                            <code className="flex-1 break-all rounded bg-muted/70 px-2.5 py-2 font-mono text-xs border border-border/50">
                                                {clientConfig?.command ?? ''}
                                            </code>
                                            <CopyButton
                                                text={clientConfig?.command ?? ''}
                                                variant="outline"
                                                size="sm"
                                                successMessage="已复制启动命令"
                                            />
                                        </div>
                                    </TabsContent>
                                    <TabsContent value="json" className="space-y-2 mt-3">
                                        <p className="text-xs text-muted-foreground">
                                            下载配置文件，按需修改 Proxy 规则后执行：./TunnelClient -c ./tunnel.json
                                        </p>
                                        <pre className="max-h-48 overflow-auto rounded bg-muted/70 p-2.5 font-mono text-xs border border-border/50">
                                            {clientConfig?.tunnelJson ?? ''}
                                        </pre>
                                        <Button variant="outline" size="sm" onClick={handleDownloadJson}>
                                            <Download className="mr-2 h-4 w-4" />
                                            下载 tunnel.json
                                        </Button>
                                    </TabsContent>
                                    <TabsContent value="binary" className="space-y-2 text-xs text-muted-foreground mt-3">
                                        <p>
                                            前往{' '}
                                            <a
                                                className="text-primary underline font-medium"
                                                href="https://github.com/AIDotNet/FastGateway/releases"
                                                target="_blank"
                                                rel="noreferrer"
                                            >
                                                GitHub Releases
                                            </a>{' '}
                                            下载对应平台的 tunnelclient 压缩包（Linux/Windows/macOS），解压后配合上方命令或配置文件启动。
                                        </p>
                                        <p>Linux 可参考仓库内 tunnel.service 将其注册为 systemd 服务。</p>
                                    </TabsContent>
                                </Tabs>
                            </div>

                            <div className="flex justify-end pt-2">
                                <Button onClick={onClose}>完成并关闭</Button>
                            </div>
                        </div>
                    )}
                </StepTransition>
            </DialogContent>
        </Dialog>
    );
};
