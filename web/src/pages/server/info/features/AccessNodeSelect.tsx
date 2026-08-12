import { useEffect, useState } from "react";

import { ClusterRole, ClusterState, getClusterState } from "@/services/ClusterService";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/components/ui/select";

/** Select 组件不允许空字符串 item value，用哨兵值表示「本节点直连」 */
const DIRECT = "__direct__";

interface AccessNodeSelectProps {
    value?: string | null;
    onChange: (accessNodeId: string | null) => void;
}

/**
 * 集群「访问节点」下拉：仅主网关且已有从节点时显示。
 * 独立部署 / 尚未接入任何节点时不渲染，避免干扰原有表单。
 */
export default function AccessNodeSelect({ value, onChange }: AccessNodeSelectProps) {
    const [state, setState] = useState<ClusterState | null>(null);

    useEffect(() => {
        let cancelled = false;
        getClusterState()
            .then((res) => {
                if (!cancelled) setState(res);
            })
            .catch(() => {
                if (!cancelled) setState(null);
            });
        return () => {
            cancelled = true;
        };
    }, []);

    // 独立网关、从节点、或主网关尚无成员：都没有可选的访问节点
    if (!state || state.role !== ClusterRole.Master || state.nodes.length === 0) return null;

    return (
        <Card>
            <CardHeader>
                <CardTitle className="text-base">访问节点</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2">
                <Label className="text-sm font-medium">由哪个网关访问上游</Label>
                <Select
                    value={value || DIRECT}
                    onValueChange={(next) => onChange(next === DIRECT ? null : next)}
                >
                    <SelectTrigger>
                        <SelectValue placeholder="本节点直连（默认）" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value={DIRECT}>本节点直连（默认）</SelectItem>
                        <SelectItem value="master">主网关</SelectItem>
                        {state.nodes.map((node) => (
                            <SelectItem key={node.id} value={node.id}>
                                {node.name}
                                {node.online ? "" : "（离线）"}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
                <div className="text-xs text-muted-foreground">
                    上游只在某个节点内网可达时指定该节点；请求到达其他节点会自动中继过去。默认由收到请求的节点直连。
                </div>
            </CardContent>
        </Card>
    );
}
