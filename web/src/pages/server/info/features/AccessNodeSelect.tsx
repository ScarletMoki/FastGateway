import { useEffect, useState } from "react";

import { ClusterRole, ClusterState, getClusterState } from "@/services/ClusterService";

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
 * 集群「访问节点」下拉：仅主网关角色下显示。
 * 指定后，请求到达非指定节点时自动经集群链路中继给指定节点访问上游。
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

    if (!state || state.role !== ClusterRole.Master) return null;

    return (
        <div className="space-y-2">
            <Label className="text-sm font-medium">访问节点</Label>
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
                指定后，请求到达其他节点时将经集群链路中继给该节点访问上游；默认由收到请求的节点直连。
            </div>
        </div>
    );
}
