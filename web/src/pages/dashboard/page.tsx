import { Suspense, lazy, useEffect, useState } from "react";
import { motion } from "motion/react";
import { Reveal, StatusIndicator, TRANSITION } from "@/components/motion";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getServers } from "@/services/ServerService";
import { getDomains } from "@/services/DomainNameService";
import { useDashboardStore } from "./store";
import { RANGE_OPTIONS, type StatRange } from "./types";

const TrafficTab = lazy(() => import("./features/traffic"));
const SecurityTab = lazy(() => import("./features/security"));

const ALL_HOSTS = "__all__";

const TABS = [
  { value: "traffic", label: "流量分析" },
  { value: "security", label: "安全态势" },
] as const;

function TabSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-24 w-full" />
      <div className="grid gap-4 md:grid-cols-2">
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    </div>
  );
}

export default function DashboardPage() {
  const { range, host, setRange, setHost } = useDashboardStore();
  const [tab, setTab] = useState("traffic");
  const [domains, setDomains] = useState<string[]>([]);

  useEffect(() => {
    let live = true;
    (async () => {
      try {
        const res: any = await getServers();
        const servers = Array.isArray(res?.data) ? res.data : [];
        const results = await Promise.all(
          servers
            .filter((s: any) => typeof s.id === "string" && s.id)
            .map(async (s: any) => {
              const domainRes: any = await getDomains(s.id);
              const items = Array.isArray(domainRes?.data) ? domainRes.data : [];
              return items.flatMap((d: any) => (Array.isArray(d.domains) ? d.domains : []));
            })
        );
        if (!live) return;
        const unique = [...new Set(results.flat().filter((d: string) => d && d !== "*"))] as string[];
        setDomains(unique.sort());
      } catch (error) {
        console.error(error);
      }
    })();
    return () => {
      live = false;
    };
  }, []);

  return (
    <TooltipProvider>
      <div className="mx-auto max-w-7xl space-y-5 p-4 md:p-6">
        <Tabs value={tab} onValueChange={setTab} className="w-full">
          <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <h1 className="text-xl font-semibold tracking-tight">统计报表</h1>
                <StatusIndicator status="busy" label="30s 刷新" size="sm" />
              </div>
              <TabsList>
                {TABS.map((t) => (
                  <TabsTrigger
                    key={t.value}
                    value={t.value}
                    // 关掉 shadcn 自带的瞬时选中态，交给下面的 layoutId 滑块
                    className="relative data-[state=active]:bg-transparent data-[state=active]:shadow-none"
                  >
                    {tab === t.value && (
                      <motion.span
                        layoutId="dashboard-tabs-indicator"
                        className="absolute inset-0 rounded-md bg-background shadow"
                        transition={TRANSITION.layout}
                      />
                    )}
                    <span className="relative">{t.label}</span>
                  </TabsTrigger>
                ))}
              </TabsList>
            </div>

            <div className="flex flex-wrap items-center gap-2">
                <Select
                  value={host ?? ALL_HOSTS}
                  onValueChange={(value) => setHost(value === ALL_HOSTS ? undefined : value)}
                >
                  <SelectTrigger className="h-8 w-[160px] text-xs">
                    <SelectValue placeholder="全部应用" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_HOSTS}>全部应用</SelectItem>
                    {domains.map((domain) => (
                      <SelectItem key={domain} value={domain}>
                        {domain}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                <ToggleGroup
                  type="single"
                  size="sm"
                  variant="outline"
                  value={range}
                  onValueChange={(value: string) => value && setRange(value as StatRange)}
                  className="hidden sm:flex"
                >
                  {RANGE_OPTIONS.map((option) => (
                    <ToggleGroupItem key={option.value} value={option.value} className="h-8 px-3 text-xs">
                      {option.label}
                    </ToggleGroupItem>
                  ))}
                </ToggleGroup>

                <Select value={range} onValueChange={(value) => setRange(value as StatRange)}>
                  <SelectTrigger className="h-8 w-[120px] text-xs sm:hidden">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {RANGE_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
          </div>

          {/* Radix 切换时会卸载旧 Content、挂载新的，Reveal 因此每次都重播一次入场。
              只做入场不做退场：加 forceMount 才能有退场，但那会让两个 Tab 同时挂载
              并同时轮询，请求数直接翻倍 */}
          <TabsContent value="traffic" className="mt-4">
            <Reveal>
              <Suspense fallback={<TabSkeleton />}>
                <TrafficTab />
              </Suspense>
            </Reveal>
          </TabsContent>
          <TabsContent value="security" className="mt-4">
            <Reveal>
              <Suspense fallback={<TabSkeleton />}>
                <SecurityTab />
              </Suspense>
            </Reveal>
          </TabsContent>
        </Tabs>
      </div>
    </TooltipProvider>
  );
}
