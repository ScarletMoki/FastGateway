import { useEffect, useRef } from "react";

/**
 * 首次渲染返回 true，之后返回 false。
 *
 * 在 effect 里翻转 ref 不会触发重渲染 —— 这正是需要的：父组件因轮询 setState
 * 重渲染时本 hook 自然返回 false，此时新挂载的子元素拿到 initial={false}，
 * 不会补播入场动画（例如排行榜洗牌换进新条目）。
 *
 * 项目未启用 StrictMode，无双挂载风险。
 */
export function useFirstMount(): boolean {
  const ref = useRef(true);
  useEffect(() => {
    ref.current = false;
  }, []);
  return ref.current;
}
