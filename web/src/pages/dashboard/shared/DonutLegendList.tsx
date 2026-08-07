import { Stagger, StaggerItem, SwapFade } from "@/components/motion";
import { DonutChart } from "@/components/ui/donut-chart";
import { Skeleton } from "@/components/ui/skeleton";
import { formatCount } from "../types";

/**
 * 环形图 + 右侧图例列表（名称/计数/占比）。
 */
export function DonutLegendList({
  title,
  items,
  colors,
  loading,
  maxRows = 6,
}: {
  title: string;
  items: Array<{ key: string; count: number; percent: number }>;
  colors: string[];
  loading?: boolean;
  maxRows?: number;
}) {
  const rows = items.slice(0, maxRows);
  const data = rows.map((x) => ({ name: x.key, value: x.count }));

  const skeleton = (
    <div className="flex items-center gap-4">
      <Skeleton className="h-28 w-28 rounded-full" />
      <div className="flex-1 space-y-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-3.5 w-full" />
        ))}
      </div>
    </div>
  );

  return (
    // relative：SwapFade 的 popLayout 需要定位祖先
    <div className="relative min-w-0">
      <div className="mb-1 text-xs font-medium text-muted-foreground">{title}</div>
      <SwapFade loading={!!loading} skeleton={skeleton}>
        {rows.length === 0 ? (
          <div className="py-8 text-center text-xs text-muted-foreground">暂无数据</div>
        ) : (
          <div className="flex items-center gap-2">
            <DonutChart
              data={data}
              colors={colors}
              showLegend={false}
              className="w-32 shrink-0"
              chartClassName="aspect-square h-32"
              valueFormatter={formatCount}
            />
            <Stagger className="min-w-0 flex-1 space-y-1.5">
              {rows.map((item, i) => (
                <StaggerItem key={item.key} className="flex items-center gap-2 text-xs">
                  <span
                    className="h-2 w-2 shrink-0 rounded-[2px]"
                    style={{ backgroundColor: colors[i % colors.length] }}
                  />
                  <span className="min-w-0 flex-1 truncate text-foreground/90" title={item.key}>
                    {item.key}
                  </span>
                  <span className="shrink-0 font-semibold tabular-nums">{formatCount(item.count)}</span>
                </StaggerItem>
              ))}
            </Stagger>
          </div>
        )}
      </SwapFade>
    </div>
  );
}
