import type { Transition, Variants } from "motion/react";

/** framer-motion 未 re-export Easing 类型，这里本地定义 */
export type Bezier = [number, number, number, number];

/** 时长，motion 用秒制 */
export const DURATION = {
  fast: 0.12,
  base: 0.24,
  bar: 0.5,
  number: 0.6,
} as const;

export const EASE = {
  /** 起步快、收尾稳 */
  out: [0.16, 1, 0.3, 1] as Bezier,
  standard: [0.4, 0, 0.2, 1] as Bezier,
} as const;

export const TRANSITION = {
  enter: { duration: DURATION.base, ease: EASE.out } satisfies Transition,
  exit: { duration: DURATION.fast, ease: EASE.standard } satisfies Transition,
  hover: { duration: DURATION.fast, ease: EASE.standard } satisfies Transition,
  bar: { duration: DURATION.bar, ease: EASE.out } satisfies Transition,
  /** 临界阻尼，无过冲 */
  number: { type: "spring", visualDuration: DURATION.number, bounce: 0 } satisfies Transition,
  layout: { type: "spring", visualDuration: 0.3, bounce: 0 } satisfies Transition,
} as const;

export const STAGGER = { step: 0.04, maxItems: 8 } as const;

export const OFFSET = { y: 8 } as const;

/** index → delay，超过 maxItems 后钳制，避免第 20 项延迟 800ms */
export function delayFor(index = 0, step: number = STAGGER.step, max: number = STAGGER.maxItems) {
  return Math.min(index, max - 1) * step;
}

export const revealVariants: Variants = {
  hidden: { opacity: 0, y: OFFSET.y },
  visible: { opacity: 1, y: 0, transition: TRANSITION.enter },
  exit: { opacity: 0, transition: TRANSITION.exit },
};
