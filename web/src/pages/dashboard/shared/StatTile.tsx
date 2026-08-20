import type { ReactNode } from "react";
import { ChevronDown } from "lucide-react";
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
  /** 可点开展开明细 */
  expandable?: boolean;
  selected?: boolean;
  onSelect?: () => void;
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
  expandable,
  selected,
  onSelect,
}: StatTileProps) {
  const body = (
    <>
      <div className="flex items-center justify-between gap-2 text-xs text-muted-foreground">
        <span className="flex min-w-0 items-center gap-1.5">
          {icon}
          <span className="truncate">{label}</span>
        </span>
        {expandable ? (
          <ChevronDown
            className={cn(
              "h-3 w-3 shrink-0 transition-transform duration-200",
              selected && "rotate-180 text-foreground"
            )}
          />
        ) : null}
      </div>
      <SwapFade loading={!!loading} skeleton={<Skeleton className="mt-1.5 h-7 w-16" />}>
        <div
          className={cn(
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
    </>
  );

  if (expandable) {
    return (
      <button
        type="button"
        onClick={onSelect}
        aria-expanded={selected}
        className={cn(
          "relative min-w-0 w-full px-4 py-3 text-left transition-colors",
          "hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40",
          selected && "bg-muted/50"
        )}
      >
        {selected && <span className="absolute inset-x-3 bottom-0 h-0.5 rounded-full bg-primary" />}
        {body}
      </button>
    );
  }

  return <div className="relative min-w-0 px-4 py-3">{body}</div>;
}
