import { useMemo, type ReactNode } from "react";
import { motion, type HTMLMotionProps } from "motion/react";
import { cn } from "@/lib/utils";
import { OFFSET, STAGGER, TRANSITION } from "./tokens";
import { useFirstMount } from "./useFirstMount";
import { StaggerContext, useStaggerContext } from "./stagger-context";

export interface StaggerProps
  extends Omit<HTMLMotionProps<"div">, "variants" | "initial" | "animate"> {
  /** 相邻子项的间隔（秒） */
  step?: number;
  /** 整组开始前的延迟（秒） */
  delayChildren?: number;
  children: ReactNode;
}

export function Stagger({
  step = STAGGER.step,
  delayChildren = 0,
  children,
  ...rest
}: StaggerProps) {
  const firstMount = useFirstMount();
  const value = useMemo(() => ({ firstMount }), [firstMount]);

  return (
    <StaggerContext.Provider value={value}>
      <motion.div
        initial={firstMount ? "hidden" : false}
        animate="visible"
        variants={{
          hidden: {},
          visible: { transition: { staggerChildren: step, delayChildren } },
        }}
        {...rest}
      >
        {children}
      </motion.div>
    </StaggerContext.Provider>
  );
}

export interface StaggerItemProps extends Omit<HTMLMotionProps<"div">, "variants"> {
  y?: number;
  /** hover 时轻微抬起：scale 1.005 + shadow */
  hoverLift?: boolean;
}

export function StaggerItem({
  y = OFFSET.y,
  hoverLift = false,
  className,
  children,
  ...rest
}: StaggerItemProps) {
  const { firstMount } = useStaggerContext();

  return (
    <motion.div
      variants={{
        hidden: { opacity: 0, y },
        visible: { opacity: 1, y: 0, transition: TRANSITION.enter },
      }}
      // undefined = 继承父级 variant 编排；false = 直接落到 visible 不播，
      // 用于轮询后新挂载的条目
      initial={firstMount ? undefined : false}
      whileHover={hoverLift ? { scale: 1.005 } : undefined}
      transition={TRANSITION.hover}
      // boxShadow 是 paint 属性，交给 CSS 比逐帧插值划算
      className={cn(hoverLift && "transition-shadow duration-150 hover:shadow-md", className)}
      {...rest}
    >
      {children}
    </motion.div>
  );
}
