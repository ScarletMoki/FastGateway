import { createContext, useContext } from "react";

/**
 * 单独成文件是为了让 Stagger.tsx 只导出组件，满足 react-refresh 的要求。
 *
 * firstMount 由 <Stagger> 通过 useFirstMount 下发：父级第二次渲染后变 false，
 * 之后新挂载的 StaggerItem / ProgressBar 会直接落位而不补播入场动画
 * （例如轮询后排行榜洗牌换进新条目）。
 */
export const StaggerContext = createContext<{ firstMount: boolean }>({ firstMount: true });

export const useStaggerContext = () => useContext(StaggerContext);
