import { memo } from "react";
import { motion } from "motion/react";
import { cn } from "@/lib/utils";

export interface FlowStreamLineProps {
  active?: boolean;
  orientation?: "horizontal" | "vertical";
  className?: string;
  length?: number | string;
}

export const FlowStreamLine = memo(function FlowStreamLine({
  active = true,
  orientation = "horizontal",
  className,
}: FlowStreamLineProps) {
  const isHoriz = orientation === "horizontal";

  return (
    <div
      className={cn(
        "relative flex shrink-0 items-center justify-center overflow-hidden",
        isHoriz ? "h-6 w-10 sm:w-14" : "h-10 sm:h-14 w-6",
        className
      )}
    >
      {/* 基础轨道线 */}
      <div
        className={cn(
          "rounded-full bg-border/80 dark:bg-border/60",
          isHoriz ? "h-[2px] w-full" : "h-full w-[2px]"
        )}
      />

      {/* 动态脉冲流光光斑 */}
      {active && (
        <motion.div
          animate={
            isHoriz
              ? { x: ["-100%", "100%"], opacity: [0, 1, 0] }
              : { y: ["-100%", "100%"], opacity: [0, 1, 0] }
          }
          transition={{
            duration: 1.6,
            repeat: Infinity,
            ease: "easeInOut",
          }}
          className={cn(
            "absolute rounded-full bg-primary shadow-[0_0_8px_rgba(var(--primary),0.8)]",
            isHoriz ? "h-1.5 w-4" : "h-4 w-1.5"
          )}
        />
      )}
    </div>
  );
});
