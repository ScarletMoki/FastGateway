import { memo, useEffect, useMemo, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { AnimatePresence, motion } from "motion/react";
import { AppSidebar } from "@/components/app-sidebar";
import { EASE, PageTransition } from "@/components/motion";
import { useTheme } from "@/components/theme-provider";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { ThemeSwitch } from "@/components/ui/theme-switch";
import { useUserStore } from "@/store/user";
import type { ThemeMode } from "antd-style";

const PAGE_TITLES: Record<string, string> = {
    dashboard: "统计报表",
    server: "服务管理",
    tunnel: "节点管理",
    cert: "证书管理",
    "security/overview": "安全总览",
    "security/access": "访问控制",
    "security/rate-limit": "限流策略",
    "security/threats": "威胁检测",
    "security/logs": "拦截日志",
    filestorage: "文件管理",
    setting: "系统设置",
    about: "系统信息",
};

function getPageTitle(pathname: string) {
    const key = pathname.replace(/^\/+/, "");
    if (!key || key === "dashboard") return PAGE_TITLES.dashboard;

    if (key.startsWith("server/")) return PAGE_TITLES.server;
    if (key.startsWith("security/")) {
        const parts = key.split("/").slice(0, 2).join("/");
        return PAGE_TITLES[parts] || "安全中心";
    }

    const base = key.split("/")[0];
    return PAGE_TITLES[key] || PAGE_TITLES[base] || "控制台";
}

const DesktopLayout = memo(() => {
    const location = useLocation();
    const navigate = useNavigate();
    const userStore = useUserStore();
    const mainRef = useRef<HTMLElement>(null);

    const { theme, setTheme: setCtxTheme } = useTheme();

    useEffect(() => {
        const t = userStore.theme;
        setCtxTheme(t === "auto" ? "system" : t);
    }, [userStore.theme, setCtxTheme]);

    useEffect(() => {
        const token = localStorage.getItem("token");
        if (!token) {
            navigate("/login");
        }
    }, [navigate]);

    const handleLogout = () => {
        localStorage.removeItem("token");
        navigate("/login");
    };

    const pageTitle = useMemo(() => getPageTitle(location.pathname), [location.pathname]);

    return (
        <SidebarProvider defaultOpen>
            <AppSidebar onLogout={handleLogout} />
            <SidebarInset>
                <header className="flex h-12 shrink-0 items-center gap-2 border-b bg-background/60 backdrop-blur">
                    <div className="flex flex-1 items-center gap-2 px-4">
                        <SidebarTrigger className="-ml-1" />
                        <Separator orientation="vertical" className="mx-2 data-[orientation=vertical]:h-4" />
                        {/* key 用 pageTitle 而非 pathname：/server 与 /server/:id 同标题，
                            进详情页时标题不该闪。总时长 240ms，比页面切换的 360ms 短，
                            让标题先落定、内容随后到位 */}
                        <AnimatePresence mode="wait" initial={false}>
                            <motion.h1
                                key={pageTitle}
                                initial={{ opacity: 0, y: 4 }}
                                animate={{ opacity: 1, y: 0 }}
                                exit={{ opacity: 0, y: -4 }}
                                transition={{ duration: 0.12, ease: EASE.out }}
                                className="text-sm font-medium"
                            >
                                {pageTitle}
                            </motion.h1>
                        </AnimatePresence>
                    </div>

                    <div className="flex items-center gap-2 px-4">
                        <ThemeSwitch
                            onThemeSwitch={(v) => {
                                userStore.setTheme(v);
                                setCtxTheme(v === "auto" ? "system" : v);
                            }}
                            themeMode={theme === "system" ? "auto" : (theme as ThemeMode)}
                        />
                    </div>
                </header>

                {/* scrollbar-gutter:stable —— 路由切换时 mode="wait" 会有一帧内容塌陷，
                    滚动条消失会让整块横向抖 15px；入场的 y 位移也会短暂计入滚动溢出 */}
                <main
                    ref={mainRef}
                    className="flex-1 overflow-auto bg-muted/20 p-4 [scrollbar-gutter:stable]"
                >
                    <PageTransition scrollRef={mainRef} />
                </main>
            </SidebarInset>
        </SidebarProvider>
    );
});

export default DesktopLayout;
