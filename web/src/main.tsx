import { createRoot } from 'react-dom/client'
import { MotionConfig } from 'motion/react'
import App from './App.tsx'
import './index.css'
import { ThemeProvider } from './components/theme-provider.tsx'

createRoot(document.getElementById('root')!).render(
    // reducedMotion="user"：系统开启「减弱动态效果」时位移/缩放/layout 动画降级为
    // 瞬时，淡入淡出保留。刻意不设全局 transition 默认值 —— 那会成为所有未显式传
    // transition 的后代的默认值，统一走 @/components/motion 的 tokens。
    <MotionConfig reducedMotion="user">
        <ThemeProvider defaultTheme="system" storageKey="fastgateway-theme">
            <App />
        </ThemeProvider>
    </MotionConfig>
)
