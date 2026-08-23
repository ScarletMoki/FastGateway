import { memo, useCallback, useEffect, useMemo, useState } from "react";
import { useParams } from "react-router-dom";
import { HeartPulse, Loader2, RefreshCw, ShieldAlert, ShieldCheck } from "lucide-react";
import { toast } from "sonner";

import DomainNamesList from "./features/DomainNamesList";
import Header from "./features/Header";
import { TrafficPipelineVisualizer } from "./features/TrafficPipelineVisualizer";
import { getServerHealth } from "@/services/ServerService";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Reveal } from "@/components/motion";
import { cn } from "@/lib/utils";
import { useDomainStore, useServerStore } from "@/store/server";
import type { DestinationHealthInfo, ServerHealthSnapshot } from "@/types";
import {
    computeEffectiveHealth,
    getProbeLabel,
    getProxyLabel,
    parseDestinationHealth,
} from "./health";

const ServerInfoPage = memo(() => {
    const { id } = useParams<{ id: string }>();
    const [health, setHealth] = useState<ServerHealthSnapshot | null>(null);
    const [healthLoading, setHealthLoading] = useState(false);
    const { servers } = useServerStore();
    const { domains } = useDomainStore();

    const currentServer = useMemo(() => {
        return servers.find((s) => s.id === id);
    }, [servers, id]);

    const loadHealth = useCallback(() => {
        if (!id) return;

        setHealthLoading(true);
        getServerHealth(id)
            .then((res) => {
                setHealth(res.data ?? null);
            })
            .catch((error) => {
                console.error(error);
                toast.error("获取健康状态失败");
            })
            .finally(() => {
                setHealthLoading(false);
            });
    }, [id]);

    useEffect(() => {
        loadHealth();

        const timer = window.setInterval(() => {
            loadHealth();
        }, 10_000);

        return () => window.clearInterval(timer);
    }, [loadHealth]);

    const summary = useMemo(() => {
        const clusters = health?.clusters ?? [];
        const enabledClusters = clusters.filter((cluster) => cluster.healthCheck?.enabled);
        const enabledClusterCount = enabledClusters.length;
        const clusterCount = clusters.length;
        const enabledPaths = Array.from(
            new Set(enabledClusters.map((cluster) => cluster.healthCheck?.path).filter(Boolean))
        );

        const destinations = clusters.flatMap((cluster) => cluster.destinations ?? []);
        const total = destinations.length;

        let healthy = 0;
        let unhealthy = 0;
        let unknown = 0;

        const withStatus = destinations.map((destination) => {
            const probe = parseDestinationHealth(destination.health?.active);
            const proxy = parseDestinationHealth(destination.health?.passive);
            const effective = computeEffectiveHealth(probe, proxy);
            return { destination, probe, proxy, effective };
        });

        for (const item of withStatus) {
            if (item.effective === "Healthy") healthy += 1;
            else if (item.effective === "Unhealthy") unhealthy += 1;
            else unknown += 1;
        }

        const unhealthyTargets = withStatus
            .filter((item) => item.effective === "Unhealthy")
            .slice(0, 6);

        return {
            total,
            healthy,
            unhealthy,
            unknown,
            unhealthyTargets,
            enabledClusterCount,
            clusterCount,
            enabledPaths,
        };
    }, [health]);

    return (
        <div className="mx-auto max-w-7xl space-y-6 px-4 py-6 md:px-6 lg:px-10">
            <Header />

            {/* 流量处理管道拓扑可视化 */}
            <Reveal>
                <TrafficPipelineVisualizer
                    server={currentServer}
                    domainCount={domains.length}
                    healthyCount={summary.healthy}
                    unhealthyCount={summary.unhealthy}
                    totalDestinations={summary.total}
                />
            </Reveal>

            <Reveal delay={0.06}>
                <Card className="border-border/60">
                    <CardHeader className="space-y-2">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div className="flex items-center gap-2">
                                <HeartPulse className="h-4 w-4 text-muted-foreground" />
                                <CardTitle className="text-base">上游健康检查</CardTitle>
                                <Badge variant="secondary" className="font-normal">
                                    自动探测
                                </Badge>
                            </div>

                            <Button
                                size="sm"
                                variant="outline"
                                onClick={loadHealth}
                                disabled={!id || healthLoading}
                                className="shrink-0"
                            >
                                {healthLoading ? (
                                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                                ) : (
                                    <RefreshCw className="mr-2 h-4 w-4" />
                                )}
                                刷新
                            </Button>
                        </div>

                        <CardDescription>
                            当路由启用健康检查时，网关会定期探测上游节点，并按实际转发失败摘除异常代理（无可用节点时返回 503）。
                        </CardDescription>
                    </CardHeader>

                    <CardContent className="space-y-3">
                        {!id ? (
                            <div className="text-sm text-muted-foreground">服务 ID 无效</div>
                        ) : !health ? (
                            <div className="text-sm text-muted-foreground">暂无健康数据</div>
                        ) : !health.online ? (
                            <div className="flex items-center gap-2 text-sm text-muted-foreground">
                                <ShieldAlert className="h-4 w-4" />
                                网关未在线
                            </div>
                        ) : (
                            <>
                                <div className="flex flex-wrap items-center gap-2">
                                    <Badge
                                        variant="default"
                                        className={cn(
                                            "font-normal",
                                            summary.unhealthy === 0
                                                ? "bg-emerald-500 hover:bg-emerald-500/90"
                                                : "bg-amber-500 hover:bg-amber-500/90"
                                        )}
                                    >
                                        {summary.unhealthy === 0 ? (
                                            <ShieldCheck className="mr-1 h-3.5 w-3.5" />
                                        ) : (
                                            <ShieldAlert className="mr-1 h-3.5 w-3.5" />
                                        )}
                                        {summary.unhealthy === 0 ? "全部健康" : "有异常节点"}
                                    </Badge>
                                    <Badge variant="secondary" className="font-normal">
                                        总实例 {summary.total}
                                    </Badge>
                                    <Badge variant="secondary" className="font-normal">
                                        健康检查 {summary.enabledClusterCount}/{summary.clusterCount}
                                    </Badge>
                                    {summary.enabledPaths.length === 1 ? (
                                        <Badge variant="outline" className="font-normal">
                                            {summary.enabledPaths[0]}
                                        </Badge>
                                    ) : null}
                                    <Badge variant="secondary" className="font-normal">
                                        健康 {summary.healthy}
                                    </Badge>
                                    <Badge variant="secondary" className="font-normal">
                                        异常 {summary.unhealthy}
                                    </Badge>
                                    <Badge variant="secondary" className="font-normal">
                                        探测中 {summary.unknown}
                                    </Badge>
                                </div>

                                {summary.unhealthyTargets.length > 0 ? (
                                    <div className="space-y-2 rounded-lg border bg-muted/20 px-3 py-2">
                                        <div className="text-xs text-muted-foreground">
                                            异常节点（最多显示 6 条）
                                        </div>
                                        <div className="space-y-1">
                                            {summary.unhealthyTargets.map(({ destination, probe, proxy }) => (
                                                <UnhealthyTargetRow
                                                    key={destination.destinationId}
                                                    destination={destination}
                                                    probe={probe}
                                                    proxy={proxy}
                                                />
                                            ))}
                                        </div>
                                    </div>
                                ) : null}
                            </>
                        )}
                    </CardContent>
                </Card>
            </Reveal>

            <Reveal delay={0.12}>
                <DomainNamesList health={health} healthLoading={healthLoading} />
            </Reveal>
        </div>
    );
});

function UnhealthyTargetRow({
    destination,
    probe,
    proxy,
}: {
    destination: DestinationHealthInfo;
    probe: ReturnType<typeof parseDestinationHealth>;
    proxy: ReturnType<typeof parseDestinationHealth>;
}) {
    return (
        <div className="flex flex-wrap items-center justify-between gap-2">
            <code className="min-w-0 flex-1 truncate rounded bg-muted px-2 py-1 font-mono text-xs text-foreground">
                {destination.address}
            </code>
            <div className="flex shrink-0 flex-wrap gap-1">
                <Badge variant="outline" className="font-normal">
                    {getProbeLabel(true, probe)}
                </Badge>
                <Badge
                    variant="outline"
                    className={cn(
                        "font-normal",
                        proxy === "Unhealthy" &&
                            "border-red-500/40 text-red-700 dark:text-red-300"
                    )}
                >
                    {getProxyLabel(proxy)}
                </Badge>
            </div>
        </div>
    );
}

export default ServerInfoPage;
