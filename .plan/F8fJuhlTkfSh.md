# FastGateway 高并发 fd/socket 优化实施计划

## 目标与基线

目标：在保持普通 HTTP、L4 转发、WebSocket/隧道和集群中继功能的前提下，建立“出站连接上限 + 入站/长连接准入 + UDP 会话上限 + 生命周期释放 + 可观测”的资源保护链，避免再次出现 `SocketException (23): Too many open files in system`。

稳定基线使用本地提交 `4b54993c12062a710e6d0dba4d88937dd386484e`（`aidotnet/fast-gateway:sha-4b54993`），不是 GitHub `v2.0.3`：

- `4b54993` 的普通 YARP Handler 设置 `MaxConnectionsPerServer = 1024`，同时启用多条 HTTP/2 连接。
- 当前 `HEAD = 2afee30` 删除了普通 Handler 和故障转移 Handler 的连接上限。
- `4b54993` 之后新增了 TCP/UDP L4、集群中继和新的隧道路径；当前 UDP 建连失败还存在未释放 socket 的确定缺陷。
- GitHub `v2.0.3` 使用旧项目结构、YARP 2.2.0/net9.0，源码中没有 `MaxConnectionsPerServer`，不能作为连接上限已存在的基线。

计划遵循两个原则：先恢复已验证的稳定行为，再增加跨多个网关子应用的资源预算；不通过无上限提高 `nofile` 掩盖应用层泄漏或无限建连。

## 阶段 0：先建立可回归的观测基线

### 任务

1. 固定对照版本：`sha-4b54993`、当前 `HEAD`，以及修复后的候选版本。
2. 为相同流量记录以下指标：
   - 进程 fd 数：`ls /proc/<PID>/fd | wc -l`；
   - 宿主机文件表：`/proc/sys/fs/file-nr`、`/proc/sys/fs/file-max`；
   - 进程 `RLIMIT_NOFILE`；
   - TCP `ESTABLISHED`、`TIME_WAIT`、监听 socket；
   - UDP 会话数量；
   - 普通转发、故障转移、隧道、TCP、UDP 的活动数；
   - `ENFILE`/`EMFILE`、连接失败、重试和 5xx。
3. 记录每个网关子应用、每个上游 origin、每个 L4 规则的资源分布，避免只看进程总 fd 数。

### 验收

- 能区分系统级 `ENFILE` 和进程级 `EMFILE`。
- 压测过程中能确认 fd 增长来自普通 HTTP、故障转移、UDP、隧道或宿主机其它进程。
- 不修改宿主机 `file-max`；Docker `nofile=65535` 只保留为容器进程上限，另行核对宿主机全局上限。

## 阶段 1：P0 修复确定的回归与泄漏

### 1. 恢复普通 YARP 出站连接上限

涉及：

- [`src/FastGateway/Gateway/FastGatewayForwarderHttpClientFactory.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Gateway/FastGatewayForwarderHttpClientFactory.cs)
- [`src/FastGateway/Options/FastGatewayOptions.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Options/FastGatewayOptions.cs)
- 如有对应配置 DTO/JSON 上下文，同步加入类型元数据。

实现：

- 保留进程级共享 `SocketsHttpHandler`，不恢复每个请求或每个 cluster 创建 Handler 的模式。
- 增加 `MaxConnectionsPerUpstream` 配置，默认 `1024`，环境变量建议使用 `MAX_CONNECTIONS_PER_UPSTREAM`；启动时校验为正数并限制合理最大值。
- 在共享 Handler 创建时设置 `MaxConnectionsPerServer = FastGatewayOptions.MaxConnectionsPerUpstream`。
- 保持稳定基线的 `ConnectTimeout = 1s`、连接池生命周期和 `EnableMultipleHttp2Connections` 行为；不把池空闲超时误当作活动连接上限。
- 在注释中明确：该值是每个 destination endpoint 的上限，不是整个进程的总 fd 上限；HTTP/2 与 HTTP/1.1 的连接含义不同。

初始值与调参：

- 第一轮使用 `1024`，确保与 `sha-4b54993` 对齐。
- 压测后再评估 `256/512/1024/2048`；不得直接恢复为 `int.MaxValue`。
- 如果业务需要更高并发，优先启用 HTTP/2 多路复用或增加实例，而不是无限增加单实例连接数。

### 2. 对故障转移客户端实施同一连接上限

涉及：

- [`src/FastGateway/Middleware/ClusterRequestFailoverMiddleware.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Middleware/ClusterRequestFailoverMiddleware.cs)

实现：

- `GetOrCreateClient` 创建 `SocketsHttpHandler` 时设置同一个 `MaxConnectionsPerUpstream`。
- 保留按 `connectTimeoutMs` 缓存的兼容行为，但增加清理机制；后续将其改为绑定网关生命周期的客户端池，停止网关时逐个 Dispose。
- 限制故障转移的最大候选尝试次数为 `2~3`，避免一个请求在多上游故障时产生大量建连 churn。
- 保证失败重试遵循请求取消、总预算和剩余超时，不因为重试延长请求持有入站连接的时间。

### 3. 修复 UDP 建连异常路径的 socket 泄漏

涉及：

- [`src/FastGateway/Gateway/StreamProxyManager.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Gateway/StreamProxyManager.cs)

实现：

- 在 `GetOrCreateUdpSessionAsync` 中先以 nullable 局部变量持有 socket；`ConnectAsync`、DNS 或其它异常时执行 `Dispose`，再返回 null。
- 保持成功路径由 `UdpSession` 接管所有权；并发竞争时继续 Dispose 未加入字典的候选 socket。
- 检查 `RemoveUdpSession`、`UdpSession.Dispose`、runner `Dispose` 的幂等性，确保回收会话时同时从字典移除并关闭上游 socket。
- 为异常路径和会话回收补充计数日志/指标，区分“连接失败后已释放”和“正常过期回收”。

### 阶段 1 验收

- 在普通 HTTP/1.1 慢上游压测下，单 origin 的活动出站连接不超过配置上限；超额请求表现为排队或准入失败，不再无限建连。
- UDP 上游拒绝连接/DNS 失败时，重复压测后 fd 数不再单调增长；会话回收后 fd 回落。
- 故障转移的重试次数和连接数均受限。
- `dotnet build FastGateway.sln` 通过，已有用户改动不被覆盖。

## 阶段 2：P1 建立分层准入和生命周期治理

### 1. 增加进程级资源预算

新增建议组件：`SocketResourceBudget` 或等价的进程级并发预算服务，注册在主应用容器中，由网关子应用和 L4 管理器共享。

预算至少拆分为：

- 普通 HTTP/代理请求；
- SSE/慢响应/长响应；
- WebSocket/HTTP 隧道/集群中继；
- TCP L4 活动连接；
- UDP 会话；
- 故障转移额外尝试。

实现要求：

- 预算不是 fd 精确计数，而是对会创建/长期持有 socket 的操作进行保守准入。
- 无可用额度时立即返回 `429` 或 `503`，不让请求无限等待在连接池或连接建立阶段。
- 所有 acquire 必须放在 `try/finally` 释放；取消、异常、客户端断开都要释放。
- 配额按进程总量计算，再按网关/协议保留预留额度，避免多个独立 `WebApplication` 的上限简单相加后超过 fd 预算。
- 记录拒绝原因、当前使用量、配置额度和等待时间。

建议起步方式：先对普通代理和 L4 加入有限额度；SSE/隧道使用单独池，避免长连接挤占普通 HTTP 配额。

### 2. 配置 Kestrel 入站连接上限

涉及：

- [`src/FastGateway/Gateway/Gateway.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Gateway/Gateway.cs)
- [`src/FastGateway/Options/FastGatewayOptions.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Options/FastGatewayOptions.cs)

实现：

- 配置 `KestrelServerOptions.Limits.MaxConcurrentConnections`，并设置合理的 `MaxConcurrentUpgradedConnections`，替换当前显式的 `null`。
- 该值必须参与进程级预算计算；不能给每个网关子应用设置相同的大值后忽略实例数量。
- 保留 `KeepAliveTimeout` 作为空闲连接回收策略，但不要把它当作活动请求或 WebSocket 时长限制。
- 对升级连接、WebSocket、CONNECT/隧道分别统计，必要时在中间件中提前拒绝。

### 3. 为 L4 规则增加 TCP/UDP 会话上限

涉及：

- [`src/Core/Entities/StreamForward.cs`](/Users/token/Desktop/code/FastGateway/src/Core/Entities/StreamForward.cs)
- [`src/FastGateway/Gateway/StreamProxyManager.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Gateway/StreamProxyManager.cs)
- `src/FastGateway/Dto/`、对应 Service/API、`web/src/` 管理页面以及 AOT MessagePack/JSON 元数据。

实现：

- 增加向后兼容的 `MaxTcpConnections` 和 `MaxUdpSessions` 字段，旧配置缺失时使用默认值。
- TCP 在接受客户端或创建上游前做原子准入；超限立即关闭客户端，避免先创建上游再发现没有额度。
- UDP 新会话使用原子计数/信号量，不能只依赖 `_udpSessions.Count` 做非原子判断；超限丢弃首包并记录指标。
- UDP 会话继续使用现有 idle sweep，但将空闲超时纳入有效范围校验；高风险场景可配置更短回收时间。
- 增加可选的单源 IP UDP 会话上限，防止单一来源耗尽整个规则额度。
- runner 停止、规则重载、应用退出时，先取消接受/接收循环，再等待或回收所有 TCP/UDP 子任务和 socket。

默认值应通过实际 fd 预算和压测确定；计划实施时先采用保守值并提供环境/管理 API 可调，不硬编码与机器规格无关的无限值。

### 4. 完善隧道和 WebSocket 释放

涉及：

- [`src/FastGateway/Tunnels/WebSocketStream.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Tunnels/WebSocketStream.cs)
- [`src/FastGateway/Tunnels/HttpTunnel.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Tunnels/HttpTunnel.cs)
- [`src/FastGateway/Tunnels/AgentManagerMiddleware.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Tunnels/AgentManagerMiddleware.cs)
- [`src/FastGateway/Tunnels/TunnelClientFactory.cs`](/Users/token/Desktop/code/FastGateway/src/FastGateway/Tunnels/TunnelClientFactory.cs)
- `src/Core/CustomWebSocketStream.cs`

实现：

- 统一服务端 WebSocket stream 的终止路径：正常关闭、取消、异常和 Dispose 都必须最终关闭/Abort 并 Dispose 底层 WebSocket，同时只触发一次完成通知。
- 明确 stream、tunnel、WebSocket 三者的所有权，防止一方 Dispose 后另一方继续使用底层连接。
- `TunnelClientFactory` 实现 `IDisposable`，确保所属网关子应用停止时 Dispose `_handler`；请求创建的 `HttpMessageInvoker` 继续使用 `disposeHandler: false`。
- 隧道的 `MaxConnectionsPerServer = 100` 保留为独立默认值，并纳入进程总预算；多个网关子应用不能无条件按 100 倍增。
- 热重载时保持旧连接可完成，但设置明确的 drain deadline，超过 deadline 强制关闭，避免旧 Handler/旧 WebSocket 永久存活。

### 阶段 2 验收

- 多网关子应用并发压测时，进程总活动连接受全局预算控制，而不是各子应用上限相加后失控。
- 超限请求快速返回，正常请求延迟不因无限排队持续恶化。
- WebSocket/隧道断开、网关重载和应用停止后，底层 socket 在限定时间内释放。
- L4 规则重载不会遗留旧监听器、TCP 子任务或 UDP 会话。

## 阶段 3：P2 可观测、配置治理与性能优化

### 1. 统一指标与诊断接口

复用现有统计/监控结构，增加：

- `fastgateway_fd_open`、fd 使用率；
- 普通上游活动连接/等待请求/连接失败；
- 故障转移客户端数量、缓存 key 数量、尝试次数；
- TCP 活动连接、TCP 拒绝数；
- UDP 会话数、创建失败、达到上限、过期回收；
- WebSocket/隧道活动数、强制关闭数；
- 每个网关/规则/origin 的维度标签。

同时提供一个只读诊断端点或管理页面，显示有效配置、当前计数和最近回收原因；不暴露敏感 token、证书或上游凭据。

### 2. 清理配置与文档

- 把 `MAX_CONNECTIONS_PER_UPSTREAM`、Kestrel 并发、L4 会话上限、故障转移最大尝试次数写入部署文档和 Compose 示例。
- 明确 Docker `ulimits.nofile` 与宿主机 `fs.file-max` 的作用边界。
- 修正普通 Handler 注释，禁止出现“对齐 v2.14.0 但不设上限”这类与基线矛盾的说明。
- 若向 `StreamForward` 增加字段，更新 API、前端表单和向后兼容说明；旧客户端缺字段时必须使用服务端默认值。

### 3. 调整超时与连接策略

- 普通 HTTP、SSE、WebSocket/隧道使用不同的活动超时；不继续把所有请求统一放宽到 10 分钟。
- 普通 HTTP 保留有限 `ResponseDrainTimeout`，避免客户端取消后响应体长时间占用连接。
- 只对明确支持的上游使用 HTTP/2/h2c；普通明文上游不要因协议探测失败重复建连。
- 在 HTTP/2 场景验证 `EnableMultipleHttp2Connections` 与上游流并发限制的关系，避免为追求吞吐无上限创建 HTTP/2 连接。

## 测试与验证矩阵

当前仓库无测试项目；实现时按以下顺序增加最小单元/集成测试，测试项目放在 `tests/`：

1. `MaxConnectionsPerUpstream` 默认值、环境变量覆盖和非法值校验。
2. 普通 Handler 的每 origin 连接上限生效；共享 Handler 不因多个 route 重复创建。
3. 故障转移最大尝试次数、总超时和 Handler 清理。
4. UDP `ConnectAsync` 失败后 socket 已释放；并发创建同一源地址只有一个会话存活。
5. UDP/TCP 达到上限后的拒绝与计数；idle sweep 和 runner Dispose 释放资源。
6. WebSocket 正常关闭、异常、取消、Dispose 的底层释放和幂等性。
7. 网关重载/停止后 Handler、监听 socket、会话和 tunnel 均在 deadline 内释放。

压测场景：

- HTTP/1.1 快上游、高并发短请求；
- HTTP/1.1 慢上游，连接数超过 1024；
- HTTPS HTTP/2 多路复用；
- 明文 HTTP/2/h2c 隧道；
- DNS 失败、拒绝连接、连接超时；
- 故障转移 2~3 个候选全部失败；
- SSE、大响应和客户端中途断开；
- WebSocket/HTTP 隧道长连接及批量断开；
- TCP L4 并发连接上限；
- UDP 大量源端口、上游拒绝、会话过期和规则重载；
- 多个网关子应用同时运行；
- 重复热重载与应用优雅停止。

每个场景都记录 p50/p95/p99、吞吐、5xx/429/503、活动连接、TIME_WAIT、UDP 会话、进程 fd 和宿主机 file table。

## 灰度与回滚标准

### 灰度顺序

1. 先在单实例、低流量环境启用默认 `1024` 的普通出站上限和 UDP 泄漏修复。
2. 再启用故障转移上限和最大尝试次数。
3. 最后启用进程级预算、Kestrel 升级连接上限和 L4 会话上限。
4. 每一步保留配置开关/环境变量，支持只回退新增准入策略，不回退 socket 释放修复。

### 成功标准

- 压测和灰度期间无新增 `ENFILE`/`EMFILE`。
- fd 使用率在设定安全水位以下，负载下降后能回落；不能出现持续单调增长。
- 普通 HTTP 超过上限时服务降级为可控排队或 `429/503`，不影响进程其它网关和控制面。
- UDP 会话、WebSocket、隧道和故障转移客户端在停止/重载后按 deadline 清零或回落。
- p95/p99 和 5xx 在基线允许范围内；任何增加必须能对应到有界准入而不是资源耗尽。

### 回滚条件

- 任一实例再次出现 `ENFILE`/`EMFILE`；
- fd 使用率连续两个观测周期超过安全水位，且负载下降后不回落；
- 普通请求 5xx、连接失败或超时显著高于稳定基线；
- WebSocket/隧道大量异常断开；
- L4 正常流量被错误拒绝或会话回收不符合配置。

回滚优先恢复 `sha-4b54993` 的出站连接上限语义，保留已经验证的 UDP Dispose 修复；不要通过删除所有限制或单纯提高 `nofile` 作为回滚方案。

## 实施顺序与交付物

1. 阶段 0：观测脚本/运行手册和基线数据。
2. 阶段 1：普通 Handler 上限、故障转移上限、UDP 异常 Dispose；配套单元/定向压测。
3. 阶段 2：进程级预算、Kestrel 限制、L4 会话限制、隧道/WebSocket 生命周期；配套 API/配置兼容。
4. 阶段 3：指标、诊断页、文档和完整压测矩阵。
5. 每个阶段独立提交、独立验证，避免把协议调整、连接上限和 L4 重构混成不可回滚的大提交。

本计划只涉及资源治理；Bot Protection、证书、前端其它未相关改动不纳入范围，工作区已有修改必须保留。