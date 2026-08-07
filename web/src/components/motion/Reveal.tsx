import { forwardRef, type ReactNode } from "react";
import { motion, type HTMLMotionProps } from "motion/react";
import { OFFSET, TRANSITION, delayFor } from "./tokens";

export interface RevealProps
  extends Omit<HTMLMotionProps<"div">, "initial" | "animate" | "transition" | "children"> {
  /** 收窄掉 motion 允许的 MotionValue，disabled 时要能落回普通 div */
  children?: ReactNode;
  /** stagger 序号，等价于 delay += min(index, 7) * 0.04 */
  index?: number;
  /** 额外延迟（秒），与 index 叠加 */
  delay?: number;
  /** 位移距离。传 0 = 纯淡入，WebGL / ECharts 容器请用 0 */
  y?: number;
  /** 完全跳过动画（重组件豁免） */
  disabled?: boolean;
}

export const Reveal = forwardRef<HTMLDivElement, RevealProps>(function Reveal(
  { index = 0, delay = 0, y = OFFSET.y, disabled = false, children, ...rest },
  ref
) {
  if (disabled) {
    return (
      <div ref={ref} className={rest.className}>
        {children}
      </div>
    );
  }

  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ ...TRANSITION.enter, delay: delay + delayFor(index) }}
      {...rest}
    >
      {children}
    </motion.div>
  );
});
