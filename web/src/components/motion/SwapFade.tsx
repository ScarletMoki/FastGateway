import type { ReactNode } from "react";
import { AnimatePresence, motion } from "motion/react";
import { TRANSITION } from "./tokens";

export interface SwapFadeProps {
  /** true 时显示 skeleton */
  loading: boolean;
  skeleton: ReactNode;
  children: ReactNode;
  className?: string;
}

/**
 * 骨架屏 ⇄ 数据的交叉淡出。
 *
 * mode="popLayout"：退场元素被提为 position:absolute，新内容同帧占位，
 * 不会出现 mode="wait" 的高度塌陷。
 * 因此**父容器必须是 relative**（或其它定位祖先），否则绝对定位会挂到更外层。
 */
export function SwapFade({ loading, skeleton, children, className }: SwapFadeProps) {
  return (
    <AnimatePresence mode="popLayout" initial={false}>
      {loading ? (
        <motion.div
          key="skeleton"
          exit={{ opacity: 0 }}
          transition={TRANSITION.exit}
          className={className}
        >
          {skeleton}
        </motion.div>
      ) : (
        <motion.div
          key="data"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={TRANSITION.enter}
          className={className}
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>
  );
}
