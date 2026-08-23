import type {
    ClusterHealth,
    DestinationHealth,
    DestinationHealthInfo,
    ServerHealthSnapshot,
} from "@/types";

export function parseDestinationHealth(value: unknown): DestinationHealth {
    if (value === "Healthy" || value === 1) return "Healthy";
    if (value === "Unhealthy" || value === 2) return "Unhealthy";
    return "Unknown";
}

export function computeEffectiveHealth(
    active: DestinationHealth,
    passive: DestinationHealth
): DestinationHealth {
    if (active === "Unhealthy" || passive === "Unhealthy") return "Unhealthy";
    if (active === "Healthy" || passive === "Healthy") return "Healthy";
    return "Unknown";
}

export function getProbeLabel(
    healthCheckEnabled: boolean,
    probe: DestinationHealth
): string {
    if (!healthCheckEnabled) return "未检查";
    if (probe === "Healthy") return "探测正常";
    if (probe === "Unhealthy") return "探测失败";
    return "探测中";
}

export function getProxyLabel(proxy: DestinationHealth): string {
    return proxy === "Unhealthy" ? "代理异常" : "代理正常";
}

export function takeDestination(
    cluster: ClusterHealth | undefined,
    service: string,
    used: Set<string>
): DestinationHealthInfo | undefined {
    if (!cluster || !service) return undefined;

    const destination = cluster.destinations.find((item) => {
        if (used.has(item.destinationId)) return false;
        const address = item.address || item.destinationId;
        return (
            address === service ||
            item.destinationId === service ||
            item.destinationId.startsWith(`${service}#`)
        );
    });

    if (destination) used.add(destination.destinationId);
    return destination;
}

export function getClusterHealthMap(
    health: ServerHealthSnapshot | null | undefined
): Map<string, ClusterHealth> {
    const map = new Map<string, ClusterHealth>();
    for (const cluster of health?.clusters ?? []) {
        map.set(cluster.clusterId, cluster);
    }
    return map;
}
