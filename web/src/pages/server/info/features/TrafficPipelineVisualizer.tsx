import { memo } from "react";
import { Globe, Shield, Route as RouteIcon, Server as ServerIcon, CheckCircle2, AlertCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { FlowStreamLine, StatusIndicator } from "@/components/motion";
import { Server } from "@/types";

interface TrafficPipelineVisualizerProps {
  server?: Server;
  domainCount: number;
  healthyCount: number;
  unhealthyCount: number;
  totalDestinations: number;
}

export const TrafficPipelineVisualizer = memo(function TrafficPipelineVisualizer({
  server,
  domainCount,
  healthyCount,
  unhealthyCount,
  totalDestinations,
}: TrafficPipelineVisualizerProps) {
  const isOnline = server?.onLine ?? false;

  return (
    <div className="relative overflow-hidden rounded-xl border border-border/70 bg-card/60 p-4 shadow-sm backdrop-blur md:p-5">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2 border-b border-border/50 pb-3">
        <div className="flex items-center gap-2">
          <span className="flex h-2 w-2 rounded-full bg-primary" />
          <h3 className="text-sm font-semibold tracking-tight text-foreground">
            流量处理管道 (Traffic Pipeline)
          </h3>
        </div>
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <StatusIndicator
            status={isOnline ? "online" : "offline"}
            label={isOnline ? "网关实时转发中" : "网关监听已停止"}
            size="sm"
          />
        </div>
      </div>

      {/* 管道流程节点 */}
      <div className="flex flex-col items-center justify-between gap-3 lg:flex-row lg:gap-0">
        {/* 节点 1: 入站监听 */}
        <div className="flex w-full flex-1 flex-col rounded-lg border border-border/60 bg-background/80 p-3.5 shadow-2xs transition-all hover:border-primary/40 lg:w-auto">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-muted-foreground">1. 入站监听</span>
            <Globe className="h-4 w-4 text-primary" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="font-mono text-lg font-bold">:{server?.listen ?? "—"}</span>
            <Badge variant="outline" className="text-[10px] font-normal">
              {server?.isHttps ? "HTTPS / TLS" : "HTTP"}
            </Badge>
          </div>
          <p className="mt-1 text-[11px] text-muted-foreground truncate">
            {server?.isHttps ? "已启用 TLS 终止" : "明文 HTTP 监听"}
          </p>
        </div>

        {/* 连接流光 1 */}
        <FlowStreamLine active={isOnline} className="hidden lg:flex" />

        {/* 节点 2: 域名与路径匹配 */}
        <div className="flex w-full flex-1 flex-col rounded-lg border border-border/60 bg-background/80 p-3.5 shadow-2xs transition-all hover:border-primary/40 lg:w-auto">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-muted-foreground">2. 路由分发</span>
            <RouteIcon className="h-4 w-4 text-sky-500" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="font-mono text-lg font-bold">{domainCount}</span>
            <span className="text-xs text-muted-foreground">条转发规则</span>
          </div>
          <p className="mt-1 text-[11px] text-muted-foreground truncate">
            YARP 动态路由 & SNI 匹配
          </p>
        </div>

        {/* 连接流光 2 */}
        <FlowStreamLine active={isOnline} className="hidden lg:flex" />

        {/* 节点 3: 安全与访问策略 */}
        <div className="flex w-full flex-1 flex-col rounded-lg border border-border/60 bg-background/80 p-3.5 shadow-2xs transition-all hover:border-primary/40 lg:w-auto">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-muted-foreground">3. 安全策略</span>
            <Shield className="h-4 w-4 text-violet-500" />
          </div>
          <div className="mt-2 flex flex-wrap gap-1">
            {server?.enableBlacklist && (
              <Badge variant="outline" className="text-[10px] text-destructive border-destructive/30">
                黑名单
              </Badge>
            )}
            {server?.enableWhitelist && (
              <Badge variant="outline" className="text-[10px] text-emerald-600 border-emerald-500/30">
                白名单
              </Badge>
            )}
            {server?.enableTunnel && (
              <Badge variant="outline" className="text-[10px] text-sky-600 border-sky-500/30">
                穿透隧道
              </Badge>
            )}
            {!server?.enableBlacklist && !server?.enableWhitelist && !server?.enableTunnel && (
              <span className="text-xs text-muted-foreground">标准防护</span>
            )}
          </div>
          <p className="mt-1 text-[11px] text-muted-foreground truncate">
            IP 审计与访问控制
          </p>
        </div>

        {/* 连接流光 3 */}
        <FlowStreamLine active={isOnline} className="hidden lg:flex" />

        {/* 节点 4: 后端上游集群 */}
        <div className="flex w-full flex-1 flex-col rounded-lg border border-border/60 bg-background/80 p-3.5 shadow-2xs transition-all hover:border-primary/40 lg:w-auto">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-muted-foreground">4. 上游负载集群</span>
            <ServerIcon className="h-4 w-4 text-emerald-500" />
          </div>
          <div className="mt-2 flex items-center gap-2">
            <span className="font-mono text-lg font-bold">{totalDestinations}</span>
            <span className="text-xs text-muted-foreground">实例</span>
            {unhealthyCount === 0 ? (
              <Badge variant="outline" className="text-[10px] text-emerald-600 border-emerald-500/30">
                <CheckCircle2 className="mr-0.5 h-3 w-3 inline" /> 全部健康
              </Badge>
            ) : (
              <Badge variant="destructive" className="text-[10px]">
                <AlertCircle className="mr-0.5 h-3 w-3 inline" /> {unhealthyCount} 异常
              </Badge>
            )}
          </div>
          <p className="mt-1 text-[11px] text-muted-foreground truncate">
            健康 {healthyCount} · 异常 {unhealthyCount}
          </p>
        </div>
      </div>
    </div>
  );
});
