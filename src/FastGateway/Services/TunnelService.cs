using System.ComponentModel.DataAnnotations;
using Core.Entities;
using FastGateway.Dto;
using FastGateway.Infrastructure;
using FastGateway.Tunnels;

namespace FastGateway.Services;

public static class TunnelService
{
    public static IEndpointRouteBuilder MapTunnel(this IEndpointRouteBuilder app)
    {
        var tunnel = app.MapGroup("/api/v1/tunnel")
            .WithTags("节点管理")
            .WithDescription("隧道节点管理")
            .RequireAuthorization()
            .AddEndpointFilter<ResultFilter>()
            .WithDisplayName("节点管理");

        tunnel.MapGet(string.Empty, () =>
                TunnelNodeStore.GetNodes()
                    .OrderByDescending(n => TunnelClientProxy.IsOnline(n.Name))
                    .ThenBy(n => n.Name)
                    .Select(ToDto)
                    .ToList())
            .WithDescription("获取节点列表")
            .WithDisplayName("获取节点列表")
            .WithTags("节点管理");

        tunnel.MapGet("{name}", (string name) =>
            {
                var node = TunnelNodeStore.GetNode(name);
                if (node == null)
                    throw new ValidationException("节点不存在");

                return ToDto(node);
            })
            .WithDescription("获取节点详情")
            .WithDisplayName("获取节点详情")
            .WithTags("节点管理");

        tunnel.MapPost(string.Empty, (CreateTunnelNodeInput input) =>
            {
                var name = input.Name?.Trim() ?? string.Empty;
                if (!TunnelNodeStore.IsValidNodeName(name))
                    throw new ValidationException("节点名不合法（仅支持字母、数字、-、_，最长 64 字符）");

                if (TunnelNodeStore.GetNode(name) != null)
                    throw new ValidationException("节点名已存在");

                var node = new TunnelNode
                {
                    Name = name,
                    Description = input.Description?.Trim() ?? string.Empty,
                    NodeKey = TunnelNodeStore.GenerateNodeKey(),
                    Enabled = true,
                    AutoRegistered = false,
                    CreatedAt = DateTime.Now
                };
                TunnelNodeStore.AddNode(node);

                // NodeKey 仅在创建时完整返回一次
                return new TunnelNodeKeyDto { Name = node.Name, NodeKey = node.NodeKey };
            })
            .WithDescription("创建节点")
            .WithDisplayName("创建节点")
            .WithTags("节点管理");

        tunnel.MapPut("{name}", async (string name, UpdateTunnelNodeInput input) =>
            {
                var node = TunnelNodeStore.GetNode(name);
                if (node == null)
                    throw new ValidationException("节点不存在");

                if (input.Description != null)
                    node.Description = input.Description.Trim();

                var disabling = input.Enabled == false && node.Enabled;
                if (input.Enabled.HasValue)
                    node.Enabled = input.Enabled.Value;

                if (input.ProxyOverrides != null)
                    node.ProxyOverrides = input.ProxyOverrides
                        .Where(o => !string.IsNullOrEmpty(o.ProxyId))
                        .Select(o => new TunnelProxyOverride { ProxyId = o.ProxyId, Enabled = o.Enabled })
                        .ToList();

                TunnelNodeStore.UpdateNode(node);

                if (disabling)
                {
                    // 禁用即踢下线，断线收尾会清理该节点的 YARP 路由
                    await TunnelClientProxy.KickAsync(node.Name);
                }
                else if (input.ProxyOverrides != null &&
                         TunnelClientProxy.GetState(node.Name) is { Client: not null } state)
                {
                    // 覆盖变更对在线节点立即生效
                    TunnelClientProxy.ReloadServerRoutes(state.ServerId);
                }
            })
            .WithDescription("更新节点")
            .WithDisplayName("更新节点")
            .WithTags("节点管理");

        tunnel.MapDelete("{name}", async (string name) =>
            {
                var node = TunnelNodeStore.GetNode(name);
                if (node == null)
                    throw new ValidationException("节点不存在");

                // 先踢下线并清理路由，再删除持久化记录
                await TunnelClientProxy.RemoveAsync(node.Name);
                TunnelNodeStore.DeleteNode(node.Name);
            })
            .WithDescription("删除节点")
            .WithDisplayName("删除节点")
            .WithTags("节点管理");

        tunnel.MapPost("{name}/regenerate-key", async (string name) =>
            {
                var node = TunnelNodeStore.GetNode(name);
                if (node == null)
                    throw new ValidationException("节点不存在");

                node.NodeKey = TunnelNodeStore.GenerateNodeKey();
                TunnelNodeStore.UpdateNode(node);

                // 旧密钥立即失效：踢下线强制客户端用新密钥重连
                await TunnelClientProxy.KickAsync(node.Name);

                return new TunnelNodeKeyDto { Name = node.Name, NodeKey = node.NodeKey };
            })
            .WithDescription("重新生成节点密钥")
            .WithDisplayName("重新生成节点密钥")
            .WithTags("节点管理");

        tunnel.MapGet("{name}/client-config", (string name, HttpContext context) =>
            {
                var node = TunnelNodeStore.GetNode(name);
                if (node == null)
                    throw new ValidationException("节点不存在");

                return BuildClientConfig(node, context);
            })
            .WithDescription("获取客户端接入配置")
            .WithDisplayName("获取客户端接入配置")
            .WithTags("节点管理");

        return app;
    }

    private static TunnelNodeDto ToDto(TunnelNode node)
    {
        var proxies = node.ReportedProxies.Select(p =>
        {
            var overrideItem = node.ProxyOverrides.FirstOrDefault(o => o.ProxyId == p.Id);
            return new TunnelProxyDto
            {
                Id = p.Id,
                Host = p.Host,
                Route = p.Route,
                LocalRemote = p.LocalRemote,
                Description = p.Description,
                Domains = p.Domains,
                ReportedEnabled = p.Enabled,
                EffectiveEnabled = overrideItem?.Enabled ?? p.Enabled,
                Overridden = overrideItem != null
            };
        }).ToArray();

        return new TunnelNodeDto
        {
            Name = node.Name,
            Description = node.Description,
            Enabled = node.Enabled,
            AutoRegistered = node.AutoRegistered,
            IsOnline = TunnelClientProxy.IsOnline(node.Name),
            CreatedAt = node.CreatedAt,
            LastConnectTime = node.LastConnectTime,
            HeartbeatInterval = node.HeartbeatInterval,
            ProxyCount = proxies.Length,
            Proxies = proxies
        };
    }

    private static TunnelClientConfigDto BuildClientConfig(TunnelNode node, HttpContext context)
    {
        // 推导接入地址：取第一个已启用且开启隧道的网关，主机名沿用面板访问域名
        var tunnelServer = TunnelNodeStore.GetServers()
            .FirstOrDefault(s => s is { Enable: true, EnableTunnel: true });

        var serverUrl = string.Empty;
        if (tunnelServer != null)
        {
            var scheme = tunnelServer.IsHttps ? "wss" : "ws";
            var port = tunnelServer is { IsHttps: true, Listen: 80 } ? 443 : tunnelServer.Listen;
            serverUrl = $"{scheme}://{context.Request.Host.Host}:{port}";
        }

        var command = $"./TunnelClient --server {(string.IsNullOrEmpty(serverUrl) ? "<服务器地址>" : serverUrl)} " +
                      $"--name {node.Name} --key {node.NodeKey}";

        var tunnelJson = $$"""
            {
              "ServerUrl": "{{serverUrl}}",
              "Name": "{{node.Name}}",
              "Key": "{{node.NodeKey}}",
              "Type": "ws",
              "Port": 0,
              "ReconnectInterval": 5,
              "HeartbeatInterval": 30,
              "Proxy": [
                {
                  "Route": "/",
                  "Domains": ["app.example.com"],
                  "LocalRemote": "http://127.0.0.1:8080",
                  "Description": "示例代理规则，请按实际服务修改",
                  "Enabled": true
                }
              ]
            }
            """;

        return new TunnelClientConfigDto
        {
            ServerUrl = serverUrl,
            HasTunnelServer = tunnelServer != null,
            Name = node.Name,
            NodeKey = node.NodeKey,
            Command = command,
            TunnelJson = tunnelJson
        };
    }
}
