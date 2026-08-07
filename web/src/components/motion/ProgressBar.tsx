import { motion } from "motion/react";
import { cn } from "@/lib/utils";
import { TRANSITION, delayFor } from "./tokens";
import { useStaggerContext } from "./stagger-context";

export interface ProgressBarProps {
  /** 0..1，内部会钳到 [0.02, 1] 保证极小值仍可见 */
  ratio: number;
  color: string;
  /** track 高度类，如 h-1.5 / h-1 */
  heightClass?: string;
  /** 首挂时的 stagger 序号 */
  index?: number;
  className?: string;
}

/**
 * 排行条。用 scaleX 而非 width —— width 每帧触发 layout，
 * 首页同时有近 30 根条，会和 echarts-gl 争主线程。
 *
 * 内条不带圆角：scaleX 会把圆角横向压成椭圆，圆角统一由 track 的
 * overflow-hidden 裁剪提供（右端因此是方角，但条高只有 4~6px，不可辨）。
 */
export function ProgressBar({
  ratio,
  color,
  heightClass = "h-1.5",
  index = 0,
  className,
}: ProgressBarProps) {
  const { firstMount } = useStaggerContext();
  const clamped = Math.min(Math.max(Number.isFinite(ratio) ? ratio : 0, 0.02), 1);

  return (
    <div className={cn("overflow-hidden rounded-full bg-muted/60", heightClass, className)}>
      <motion.div
        className="h-full w-full origin-left"
        style={{ backgroundColor: color }}
        initial={firstMount ? { scaleX: 0 } : false}
        animate={{ scaleX: clamped }}
        transition={firstMount ? { ...TRANSITION.bar, delay: delayFor(index) } : TRANSITION.bar}
      />
    </div>
  );
}
