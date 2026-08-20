import { useMemo, useState } from "react";
import { Zap } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { BarChart } from "@/components/ui/bar-chart";
import { AnimatedNumber, StatusIndicator } from "@/components/motion";
import { getQpsData } from "@/services/QpsService";
import { usePolling } from "../hooks/usePolling";
import { formatInt } from "../types";

/**
 * 实时 QPS 迷你卡（3 秒轮询 /api/v1/qps）。
 */
export function QpsSparkCard({ className }: { className?: string }) {
  const [qps, setQps] = useState(0);
  const [history, setHistory] = useState<Array<{ time: string; QPS: number }>>([]);

  usePolling(async () => {
    const data: any = await getQpsData();
    if (!data) return;
    setQps(Number(data.qps) || 0);
    if (Array.isArray(data.qpsHistory))
      setHistory(data.qpsHistory.map((x: any) => ({ time: x.time, QPS: x.qps })));
  }, 3000);

  const peak = useMemo(() => history.reduce((acc, x) => Math.max(acc, x.QPS), 0), [history]);

  return (
    <Card className={`border-border/60 bg-card/80 shadow-sm ${className ?? ""}`}>
      <CardHeader className="pb-1">
        <CardTitle className="flex items-center justify-between text-sm font-medium">
          <span className="flex items-center gap-2">
            <Zap className="h-4 w-4 text-amber-500" />
            实时 QPS
          </span>
          <StatusIndicator status={qps > 0 ? "busy" : "online"} label={`峰值 ${peak}`} size="sm" />
        </CardTitle>
      </CardHeader>
      <CardContent className="pt-0">
        <div className="flex items-baseline gap-1.5">
          {/* 3s 轮询 > 600ms 弹簧，两次更新之间动画能跑完，不会互相打断 */}
          <AnimatedNumber
            value={qps}
            format={formatInt}
            className="text-3xl font-semibold tabular-nums"
          />
          <span className="text-xs text-muted-foreground">req/s</span>
        </div>
        <div className="mt-2 h-[64px]">
          <BarChart
            data={history}
            categories={["QPS"]}
            colors={["#eda100"]}
            index="time"
            showXAxis={false}
            showYAxis={false}
            valueFormatter={(v) => `${v} req/s`}
            className="h-full w-full"
          />
        </div>
      </CardContent>
    </Card>
  );
}
