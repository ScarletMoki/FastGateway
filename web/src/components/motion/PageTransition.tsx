import type { RefObject } from "react";
import { AnimatePresence, motion } from "motion/react";
import { useLocation, useOutlet } from "react-router-dom";
import { OFFSET, TRANSITION } from "./tokens";

export interface PageTransitionProps {
  /** 滚动容器（通常是 <main>），退场完成后回顶 */
  scrollRef?: RefObject<HTMLElement | null>;
}

/**
 * 路由切换动画。
 *
 * 必须用 useOutlet() 而不是 <Outlet />：后者是组件，自己订阅 route context，
 * location 变化时原地渲染新页面，React 认为是同一个元素，AnimatePresence
 * 看不到任何 key 变化。useOutlet() 返回的是当前 location 对应的元素快照。
 *
 * key 用 pathname 而非 location.key —— 后者每次导航（含 replace、纯 query
 * 变化）都变，会让分页操作整页重播。
 */
export function PageTransition({ scrollRef }: PageTransitionProps) {
  const location = useLocation();
  const outlet = useOutlet();

  return (
    <AnimatePresence
      mode="wait"
      initial={false}
      onExitComplete={() => scrollRef?.current?.scrollTo({ top: 0 })}
    >
      <motion.div
        key={location.pathname}
        initial={{ opacity: 0, y: OFFSET.y }}
        animate={{ opacity: 1, y: 0 }}
        // 退场只淡出、不位移：加位移会让点侧边栏时有卡顿感
        exit={{ opacity: 0 }}
        transition={TRANSITION.enter}
        className="min-h-full"
      >
        {outlet}
      </motion.div>
    </AnimatePresence>
  );
}
