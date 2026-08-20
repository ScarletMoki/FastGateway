import type { ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { ExpandablePanel } from "./ExpandablePanel";

export function ExpandableCard({
  title,
  icon,
  badge,
  extra,
  expanded,
  onToggle,
  children,
  detail,
}: {
  title: string;
  icon?: ReactNode;
  badge?: ReactNode;
  extra?: ReactNode;
  expanded: boolean;
  onToggle: () => void;
  children: ReactNode;
  detail?: ReactNode;
}) {
  return (
    <Card className="border-border/60 bg-card/80 shadow-sm">
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="flex items-center gap-2 text-sm font-medium">
          {icon}
          {title}
          {badge}
        </CardTitle>
        <div className="flex items-center gap-1">
          {extra}
          {detail ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-7 px-2 text-xs text-muted-foreground"
              onClick={onToggle}
            >
              {expanded ? "收起" : "展开"}
              <ChevronDown className={cn("ml-1 h-3.5 w-3.5 transition-transform", expanded && "rotate-180")} />
            </Button>
          ) : null}
        </div>
      </CardHeader>
      <CardContent>
        {children}
        <ExpandablePanel open={expanded} className="mt-3 border-t-0">
          {detail}
        </ExpandablePanel>
      </CardContent>
    </Card>
  );
}
