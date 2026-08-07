import { ProgressBar, Stagger, StaggerItem, SwapFade } from "@/components/motion";
import { Skeleton } from "@/components/ui/skeleton";
import { formatCount } from "../types";

/**
 * Top-N 横条排行（条宽相对第一名）。
 */
export function RankList({
  items,
  loading,
  emptyText = "暂无数据",
  color = "#2a78d6",
  maxRows,
}: {
  items: Array<{ key: string; count: number }>;
  loading?: boolean;
  emptyText?: string;
  color?: string;
  maxRows?: number;
}) {
  const rows = maxRows ? items.slice(0, maxRows) : items;
  const max = Math.max(...rows.map((x) => x.count), 1);

  const skeleton = (
    <div className="space-y-3 py-1">
      {Array.from({ length: 5 }).map((_, i) => (
        <div key={i} className="space-y-1.5">
          <Skeleton className="h-3.5 w-3/4" />
          <Skeleton className="h-1.5 w-full" />
        </div>
      ))}
    </div>
  );

  return (
    // relative：SwapFade 的 popLayout 需要定位祖先
    <div className="relative">
      <SwapFade loading={!!loading} skeleton={skeleton}>
        {rows.length === 0 ? (
          <div className="py-8 text-center text-xs text-muted-foreground">{emptyText}</div>
        ) : (
          <Stagger className="space-y-2.5">
            {rows.map((item, i) => (
              <StaggerItem key={item.key} className="group">
                <div className="flex items-baseline justify-between gap-3">
                  <span className="min-w-0 truncate text-xs text-foreground/90" title={item.key}>
                    {item.key}
                  </span>
                  <span className="shrink-0 text-xs font-semibold tabular-nums text-foreground">
                    {formatCount(item.count)}
                  </span>
                </div>
                <ProgressBar
                  ratio={item.count / max}
                  color={color}
                  heightClass="h-1.5"
                  index={i}
                  className="mt-1"
                />
              </StaggerItem>
            ))}
          </Stagger>
        )}
      </SwapFade>
    </div>
  );
}
