import { memo } from "react";
import { HeartPulse } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import type { DestinationHealth, DestinationHealthInfo } from "@/types";
import {
    computeEffectiveHealth,
    getProbeLabel,
    getProxyLabel,
    parseDestinationHealth,
} from "../health";

type NodeTone = "healthy" | "unhealthy" | "unknown" | "idle";

interface UpstreamNodeStatusProps {
    address: string;
    destination?: DestinationHealthInfo;
    healthCheckEnabled: boolean;
    gatewayOnline?: boolean;
    loading?: boolean;
}

function resolveTone(args: {
    gatewayOnline: boolean;
    healthCheckEnabled: boolean;
    effective: DestinationHealth;
    loading: boolean;
}): NodeTone {
    if (!args.gatewayOnline || args.loading) return "unknown";
    if (!args.healthCheckEnabled) return "idle";
    if (args.effective === "Healthy") return "healthy";
    if (args.effective === "Unhealthy") return "unhealthy";
    return "unknown";
}

const TONE_CLASS: Record<NodeTone, string> = {
    healthy:
        "border-emerald-500/35 bg-emerald-500/10 text-emerald-800 dark:text-emerald-200",
    unhealthy:
        "border-red-500/40 bg-red-500/10 text-red-800 dark:text-red-200",
    unknown:
        "border-amber-500/35 bg-amber-500/10 text-amber-800 dark:text-amber-200",
    idle: "border-border/70 bg-muted/30 text-muted-foreground",
};

const DOT_CLASS: Record<NodeTone, string> = {
    healthy: "bg-emerald-500",
    unhealthy: "bg-red-500",
    unknown: "bg-amber-500",
    idle: "bg-muted-foreground/50",
};

const UpstreamNodeStatus = memo(function UpstreamNodeStatus({
    address,
    destination,
    healthCheckEnabled,
    gatewayOnline = true,
    loading = false,
}: UpstreamNodeStatusProps) {
    const probe = parseDestinationHealth(destination?.health?.active);
    const proxy = parseDestinationHealth(destination?.health?.passive);
    const effective = computeEffectiveHealth(probe, proxy);
    const tone = resolveTone({
        gatewayOnline,
        healthCheckEnabled,
        effective,
        loading,
    });

    const probeLabel = !gatewayOnline
        ? "网关离线"
        : loading
          ? "探测中"
          : getProbeLabel(healthCheckEnabled, probe);
    const proxyLabel = !gatewayOnline
        ? "—"
        : loading
          ? "—"
          : !healthCheckEnabled
            ? "未监测代理"
            : getProxyLabel(proxy);

    const tooltip = !gatewayOnline
        ? "网关未在线，无法读取节点健康状态"
        : !healthCheckEnabled
          ? "该路由未启用健康检查，节点不会被自动探测或从负载中摘除"
          : `主动探测（健康检查）：${probeLabel}\n被动代理（转发失败率）：${proxyLabel}`;

    return (
        <Tooltip>
            <TooltipTrigger asChild>
                <button
                    type="button"
                    aria-label={`${address}，${probeLabel}，${proxyLabel}`}
                    className={cn(
                        "inline-flex max-w-full min-w-0 flex-col gap-0.5 rounded-md border px-2 py-1.5 text-left shadow-none outline-none focus-visible:ring-2 focus-visible:ring-ring",
                        TONE_CLASS[tone]
                    )}
                >
                        <span className="inline-flex min-w-0 items-center gap-1.5">
                            <span
                                className={cn(
                                    "relative flex h-1.5 w-1.5 shrink-0 items-center justify-center"
                                )}
                            >
                                {tone === "healthy" || tone === "unknown" ? (
                                    <span
                                        className={cn(
                                            "absolute inset-0 animate-ping rounded-full opacity-60",
                                            DOT_CLASS[tone]
                                        )}
                                    />
                                ) : null}
                                <span
                                    className={cn(
                                        "relative h-1.5 w-1.5 rounded-full",
                                        DOT_CLASS[tone]
                                    )}
                                />
                            </span>
                            <code className="min-w-0 truncate font-mono text-xs font-medium text-foreground">
                                {address}
                            </code>
                        </span>
                        <span className="inline-flex flex-wrap items-center gap-x-1.5 pl-3 text-[10px] leading-tight">
                            <span>{probeLabel}</span>
                            <span className="text-muted-foreground">·</span>
                            <span
                                className={cn(
                                    proxy === "Unhealthy" &&
                                        gatewayOnline &&
                                        "font-medium text-red-600 dark:text-red-300"
                                )}
                            >
                                {proxyLabel}
                            </span>
                        </span>
                    </button>
                </TooltipTrigger>
                <TooltipContent
                    side="top"
                    className="whitespace-pre-line max-w-xs text-left"
                >
                    {tooltip}
                </TooltipContent>
            </Tooltip>
    );
});

interface ClusterHealthSummaryProps {
    total: number;
    healthy: number;
    unhealthy: number;
    unknown: number;
    healthCheckEnabled: boolean;
    healthCheckPath?: string | null;
    gatewayOnline?: boolean;
}

export const ClusterHealthSummary = memo(function ClusterHealthSummary({
    total,
    healthy,
    unhealthy,
    unknown,
    healthCheckEnabled,
    healthCheckPath,
    gatewayOnline = true,
}: ClusterHealthSummaryProps) {
    return (
        <div className="flex flex-wrap items-center gap-1.5">
            {healthCheckEnabled ? (
                <Badge variant="outline" className="h-5 gap-1 px-1.5 text-[10px] font-normal">
                    <HeartPulse className="h-3 w-3" />
                    {healthCheckPath || "健康检查"}
                </Badge>
            ) : (
                <Badge variant="secondary" className="h-5 px-1.5 text-[10px] font-normal">
                    未启用健康检查
                </Badge>
            )}
            {!gatewayOnline ? (
                <Badge variant="outline" className="h-5 px-1.5 text-[10px] font-normal">
                    网关离线
                </Badge>
            ) : healthCheckEnabled && total > 0 ? (
                unhealthy > 0 ? (
                    <Badge
                        variant="outline"
                        className="h-5 border-red-500/40 px-1.5 text-[10px] font-normal text-red-700 dark:text-red-300"
                    >
                        {unhealthy} 异常
                    </Badge>
                ) : unknown > 0 && healthy === 0 ? (
                    <Badge
                        variant="outline"
                        className="h-5 border-amber-500/40 px-1.5 text-[10px] font-normal text-amber-700 dark:text-amber-300"
                    >
                        探测中
                    </Badge>
                ) : (
                    <Badge
                        variant="outline"
                        className="h-5 border-emerald-500/40 px-1.5 text-[10px] font-normal text-emerald-700 dark:text-emerald-300"
                    >
                        全部健康
                    </Badge>
                )
            ) : null}
        </div>
    );
});

export default UpstreamNodeStatus;
