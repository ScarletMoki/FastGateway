import { useCallback, useEffect, useRef } from "react";
import { animate, motion, useMotionValue, useReducedMotion, useTransform } from "motion/react";
import { TRANSITION } from "./tokens";

export interface AnimatedNumberProps {
  value: number;
  /** 接收已量化的数字，返回展示字符串。默认 toLocaleString */
  format?: (n: number) => string;
  /**
   * 插值中间值保留的小数位，默认 0。
   * 弹簧插值会产生 1234.5678，不量化直接格式化会逐帧抖动。
   */
  round?: number;
  /** 首次挂载从 0 滚上来，默认 true */
  animateOnMount?: boolean;
  className?: string;
}

function quantize(n: number, digits: number) {
  if (digits <= 0) return Math.round(n);
  const factor = 10 ** digits;
  return Math.round(n * factor) / factor;
}

/**
 * 数字滚动。MotionValue 作为 children 时 motion 直接改 textContent，
 * 动画期间零 React 重渲染。
 *
 * 调用方需自带 tabular-nums，否则位数变化会逐帧改变宽度。
 */
export function AnimatedNumber({
  value,
  format,
  round = 0,
  animateOnMount = true,
  className,
}: AnimatedNumberProps) {
  const reduced = useReducedMotion();
  // 初始化器只跑一次：首挂从 0 起，之后 value 变化走 animate()
  const mv = useMotionValue(animateOnMount && !reduced ? 0 : value);

  // 走 ref 让 transformer 引用保持稳定，否则每次渲染都会重建 MotionValue
  const formatRef = useRef(format);
  formatRef.current = format;
  const roundRef = useRef(round);
  roundRef.current = round;

  const transformer = useCallback((n: number) => {
    const q = quantize(n, roundRef.current);
    return formatRef.current ? formatRef.current(q) : q.toLocaleString();
  }, []);

  const text = useTransform(mv, transformer);

  useEffect(() => {
    if (reduced) {
      mv.set(value);
      return;
    }
    const controls = animate(mv, value, TRANSITION.number);
    return () => controls.stop();
  }, [value, reduced, mv]);

  return <motion.span className={className}>{text}</motion.span>;
}
