import { memo } from "react";
import { motion } from "motion/react";
import { cn } from "@/lib/utils";

export type StatusType = "online" | "offline" | "warning" | "busy";

export interface StatusIndicatorProps {
  status: StatusType;
  label?: string;
  ping?: boolean;
  className?: string;
  size?: "sm" | "md" | "lg";
}

const STATUS_CONFIG: Record<
  StatusType,
  {
    dot: string;
    pingColor: string;
    text: string;
    defaultLabel: string;
  }
> = {
  online: {
    dot: "bg-emerald-500",
    pingColor: "bg-emerald-400",
    text: "text-emerald-700 dark:text-emerald-400",
    defaultLabel: "在线",
  },
  warning: {
    dot: "bg-amber-500",
    pingColor: "bg-amber-400",
    text: "text-amber-700 dark:text-amber-400",
    defaultLabel: "异常",
  },
  busy: {
    dot: "bg-sky-500",
    pingColor: "bg-sky-400",
    text: "text-sky-700 dark:text-sky-400",
    defaultLabel: "运行中",
  },
  offline: {
    dot: "bg-muted-foreground/50",
    pingColor: "",
    text: "text-muted-foreground",
    defaultLabel: "离线",
  },
};

export const StatusIndicator = memo(function StatusIndicator({
  status,
  label,
  ping = true,
  className,
  size = "md",
}: StatusIndicatorProps) {
  const cfg = STATUS_CONFIG[status] || STATUS_CONFIG.offline;
  const showPing = ping && status !== "offline";

  const sizeClasses = {
    sm: "h-1.5 w-1.5",
    md: "h-2 w-2",
    lg: "h-2.5 w-2.5",
  }[size];

  return (
    <span className={cn("inline-flex items-center gap-1.5 text-xs font-medium", className)}>
      <span className={cn("relative flex shrink-0 items-center justify-center", sizeClasses)}>
        {showPing && (
          <motion.span
            animate={{ scale: [1, 2.2], opacity: [0.75, 0] }}
            transition={{ duration: 1.8, repeat: Infinity, ease: "easeOut" }}
            className={cn("absolute inset-0 rounded-full", cfg.pingColor)}
          />
        )}
        <span className={cn("relative rounded-full", sizeClasses, cfg.dot)} />
      </span>
      {(label || cfg.defaultLabel) && (
        <span className={cfg.text}>{label || cfg.defaultLabel}</span>
      )}
    </span>
  );
});
