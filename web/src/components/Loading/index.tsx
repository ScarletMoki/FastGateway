import { Loader2 } from 'lucide-react';
import { memo } from 'react';
import { motion } from 'motion/react';
import { cn } from '@/lib/utils';

interface LoadingProps {
    /**
     * 全屏模式（应用启动、登录页）。子路由的 Suspense fallback 必须传 false ——
     * 它会被渲染进已经是 100vh-48px 的 <main> 里，h-screen 会撑出多余滚动。
     */
    fullscreen?: boolean;
    /** 延迟多少毫秒才淡入，避免 chunk 命中缓存时闪一下「加载中」 */
    delayMs?: number;
}

const Loading = memo(({ fullscreen = true, delayMs = 150 }: LoadingProps) => {
    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.2, delay: delayMs / 1000 }}
            className={cn(
                'flex w-full items-center justify-center',
                fullscreen ? 'h-screen' : 'min-h-[40vh]'
            )}
        >
            <div className="flex flex-col items-center space-y-4">
                {fullscreen && (
                    <h1 className="text-4xl font-bold tracking-tight">
                        Fast Gateway
                    </h1>
                )}
                <div className="flex items-center space-x-2 text-muted-foreground">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    <span className="text-sm">加载中...</span>
                </div>
            </div>
        </motion.div>
    );
})

export default Loading;
