import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import { ExpandablePanel } from "./ExpandablePanel";

export function MetricGroup({
  title,
  columns,
  children,
  detail,
  open,
  className,
}: {
  title: string;
  columns: string;
  children: ReactNode;
  detail?: ReactNode;
  open?: boolean;
  className?: string;
}) {
  return (
    <section className={cn("overflow-hidden rounded-xl border border-border/60 bg-card/80", className)}>
      <div className="flex items-center justify-between border-b border-border/40 px-4 py-2">
        <h3 className="text-[11px] font-medium uppercase tracking-[0.16em] text-muted-foreground">{title}</h3>
        <span className="text-[10px] text-muted-foreground">点击指标展开</span>
      </div>
      <div className={cn("grid divide-y divide-border/40 sm:divide-y-0 sm:divide-x", columns)}>{children}</div>
      <ExpandablePanel open={!!open}>{detail}</ExpandablePanel>
    </section>
  );
}
