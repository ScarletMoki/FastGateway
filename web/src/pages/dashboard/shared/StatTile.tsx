import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import { Skeleton } from "@/components/ui/skeleton";
import { AnimatedNumber, SwapFade } from "@/components/motion";

export interface StatTileProps {
  label: string;
  /** number 走滚动动画；string 直出（向后兼容） */
  value: number | string;
  /** value 为 number 时的格式化器，默认 toLocaleString */
  format?: (n: number) => string;
  /** 插值中间值保留的小数位，百分比传 2 */
  round?: number;
  sub?: string;
  tone?: "default" | "danger" | "warning";
  loading?: boolean;
  icon?: ReactNode;
}

export function StatTile({
  label,
  value,
  format,
  round = 0,
  sub,
  tone,
  loading,
  icon,
}: StatTileProps) {
  return (
    // relative：SwapFade 用 popLayout，退场元素会被提为 absolute，需要定位祖先
    <div className="relative min-w-0 px-4 py-3">
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        {icon}
        <span className="truncate">{label}</span>
      </div>
      <SwapFade loading={!!loading} skeleton={<Skeleton className="mt-1.5 h-7 w-16" />}>
        <div
          className={cn(
            // tabular-nums 必须保留：滚动时位数变化会逐帧改宽度，把 divide-x 网格挤得左右晃
            "mt-1 truncate text-2xl font-semibold tabular-nums",
            tone === "danger" && "text-red-500",
            tone === "warning" && "text-amber-500"
          )}
        >
          {typeof value === "number" ? (
            <AnimatedNumber value={value} format={format} round={round} />
          ) : (
            value
          )}
        </div>
      </SwapFade>
      {sub && !loading && <div className="mt-0.5 truncate text-[11px] text-muted-foreground">{sub}</div>}
    </div>
  );
}
