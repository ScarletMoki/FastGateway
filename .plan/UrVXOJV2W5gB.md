# FastGateway 接入 Cloudflare Turnstile 机器人认证

## 现状与架构结论

- `src/FastGateway/Program.cs:43-57` 只为主宿主注册 JWT，`/api/v1/*` 管理接口通过 `RequireAuthorization()` 保护；JWT 不适合作为业务访客的人机认证。
- `src/FastGateway/Services/AuthorizationService.cs:17-28` 当前登录接口是匿名密码接口，密码通过 query string 传输；需要改为 JSON body，并增加 Turnstile token。
- `src/FastGateway/Gateway/Gateway.cs:362-748` 每个启用的 `Server` 都会创建独立的 Kestrel/YARP `WebApplication`。业务请求依次经过 IP 解析、统计、超时、限流、黑白名单、故障转移和异常监控，最后进入 `MapReverseProxy()`。
- `src/FastGateway/Gateway/Gateway.cs:932-1101` `DomainName` 会被编译为 YARP `RouteConfig`；路由支持热重载，因此机器人认证开关应放在 `DomainName` 并编译为路由 metadata。
- `src/Core/Entities/DomainName.cs` 是域名/路径级配置；`src/FastGateway/Services/ConfigurationService.cs:529-539` 的 `GatewayConfig` 会持久化它，集群快照也会携带该字段。
- `web/src/utils/fetch.ts:3-61` 是管理 API 的统一请求入口，`web/src/pages/login/index.tsx` 是管理员登录页面；前端目前没有业务域名访问拦截层，因此业务挑战页应由网关直接返回 HTML，而不是复用 React 控制台路由。

## 目标方案

采用 Cloudflare Turnstile 的服务端校验模式：

1. 浏览器访问启用保护的业务路由。
2. 网关在转发到上游前检查签名 clearance cookie。
3. 未通过时返回同源挑战页，页面加载 Turnstile widget。
4. 浏览器把一次性 token 提交到网关内部 verify endpoint。
5. 网关使用服务端 secret 调用 Turnstile `siteverify`，校验成功后签发短期、带签名的 clearance cookie，并重定向回原始 URL。
6. 后续请求验证 cookie 后才进入 YARP；内部 cookie 不转发给上游。
7. 管理员登录单独要求 Turnstile token，不签发业务 clearance cookie。

默认策略：

- 业务访问：按 `DomainName` 开关控制，GET/HEAD 可返回浏览器挑战页，非幂等请求不缓存或重放 request body，返回结构化 403 及挑战地址，客户端完成挑战后自行重试。
- 管理员登录：全局开关控制，登录接口服务端强制校验 token。
- Turnstile 校验失败或 Cloudflare 验证服务不可用时，受保护请求 fail-closed；已签发且仍有效的 clearance 不需要每次访问 Cloudflare。

## 实施步骤

### 1. 增加机器人认证配置与运行时服务

新增 `BotProtectionOptions` 和独立的运行时组件，建议放在 `src/FastGateway/Options/`、`src/FastGateway/Infrastructure/` 或 `src/FastGateway/Services/`：

- 配置项：
  - `EnabledForAdminLogin`
  - `SiteKey`：可公开下发给浏览器
  - `SecretKey`：仅服务端，禁止写入 API 响应、日志和前端 bundle
  - `CookieSigningKey`：独立于 JWT 的高熵 HMAC 密钥，集群节点必须一致
  - `ClearanceLifetimeMinutes`
  - `VerifyEndpoint`，默认 Cloudflare siteverify 地址
- 通过环境变量或 `appsettings` 注入 secret；不要把 secret 放进 `GatewayConfig` 或可由管理面导出的 `Setting`。
- 在主宿主和每个 Server 子应用分别注册验证服务及短超时 `HttpClient`，因为 Server 子应用使用独立 DI 容器。
- 增加 AOT 需要的请求/响应 DTO 到 `AppJsonContext`，避免匿名对象和运行时反射序列化。
- 验证器校验：`success`、action、当前请求 host（按配置决定是否严格校验）、客户端 IP；不记录 token 和 secret。

### 2. 扩展域名配置与 YARP 路由 metadata

修改：

- `src/Core/Entities/DomainName.cs`
- `src/Core/Entities/Server.cs`（如需要增加服务级默认开关）
- `src/FastGateway/Infrastructure/AppJsonContext.cs`
- `web/src/types/index.ts`
- `web/src/pages/server/info/features/CreateDomain.tsx`
- `web/src/pages/server/info/features/UpdateDomain.tsx`

建议第一版只增加：

- `DomainName.EnableBotProtection: bool = false`

不要把 Turnstile secret 放进域名实体。前端在创建/编辑路由的“基本”或“安全”区域增加开关和说明，默认关闭以兼容现有配置。

在 `Gateway.BuildConfig()` 中为受保护路由写入稳定 metadata，例如：

- `FastGateway.BotProtection = true`
- `FastGateway.BotRouteId = domainName.Id`
- 必要时记录规范化的 route prefix，用于 clearance cookie 的 Path 限定

relay route 也必须保留同样的保护 metadata。认证应在收到外部请求的第一跳执行；中继请求通过集群内部信任头识别后不得再次被当作新的外部访客挑战。真实上游前必须移除 FastGateway 内部 cookie/header。

域名字段已经属于持久化 `GatewayConfig`，新增 bool 会自动随配置文件和主从 MessagePack 快照同步；无需单独增加集群协议消息。通过 `ReloadGateway()` 热更新路由 metadata，不重启 Server 子应用。

### 3. 实现业务网关机器人认证中间件

新增 `BotProtectionMiddleware` 及扩展方法，挂载在 `Gateway.BuilderGateway()` 的 `MapReverseProxy()` 前。

推荐顺序：

1. HTTPS/ACME 处理
2. `UseClientIpResolution`
3. `UseInitGatewayMiddleware`
4. `UseStatisticsCapture`
5. request timeout
6. 现有限流
7. 黑白名单
8. `BotProtectionMiddleware`
9. 故障转移、代理错误、异常 IP 监控
10. 隧道内部端点与 `MapReverseProxy`

具体规则：

- 通过当前 endpoint 的 `RouteModel` 读取 YARP route metadata；没有受保护 metadata 的请求直接放行。
- 绕过 `/.well-known/acme-challenge`、`/internal/gateway`、机器人认证内部 endpoint、`OPTIONS`，并避免拦截集群/隧道控制通道。
- clearance cookie 使用独立、不可预测的名字，例如 `__FastGateway_BotClearance`；payload 至少包含 host、route id、签发时间、过期时间和随机 nonce，使用 HMAC-SHA256 签名，比较时使用恒定时间比较。
- Cookie 设置 `HttpOnly`、`SameSite=Lax`、短 `Max-Age`；HTTPS 下设置 `Secure`，并按路由前缀设置 Path。多节点必须使用同一个 signing key，否则集群中切换节点会导致反复挑战。
- 默认不绑定客户端 IP，避免移动网络和代理切换造成误伤；可预留配置开关。host 和 route id 必须绑定，防止一个站点的 clearance 解锁另一站点/路由。
- 未通过的安全 GET/HEAD 返回 `Cache-Control: no-store`、`Vary: Cookie` 的挑战 HTML；原始 URL 必须经过同源校验和编码，禁止开放重定向。
- verify 成功后只签发 cookie 并返回 303；不在网关内重放原始 POST/PUT/PATCH/DELETE body。
- 非浏览器或非幂等请求返回 403 JSON，并带稳定的 challenge URL/错误码；WebSocket/升级请求要求客户端提前取得 clearance，或由配置选择不保护该路由。
- 验证 Cloudflare 不可用时返回 503，不把未验证请求转发给上游。
- 在进入 YARP 前移除 clearance cookie 和所有内部 challenge header，防止凭证泄漏到真实上游。
- 受保护路由只建议运行在 HTTPS；对明文业务端口给出明确的配置错误/日志，避免在不安全链路上签发可被窃取的 clearance。

### 4. 增加同源挑战 endpoint 与挑战页

在每个 Server 子应用中挂载内部路径，例如：

- `GET /__fastgateway/bot/challenge`
- `POST /__fastgateway/bot/verify`

要求：

- endpoint 不参与业务 YARP 匹配，也不能被业务 route metadata 再次拦截。
- challenge 页面只输出必要的 HTML/内联脚本，加载 Turnstile 官方脚本；包含 site key、短期签名的 challenge state 和同源 return URL。
- verify endpoint 接收 `response`、challenge state 和 return URL，服务端从 `HttpContext` 读取当前 host/IP，不信任浏览器传入的 IP/host。
- 对 verify 请求设置单 IP 速率限制，并依赖 Turnstile token 的一次性语义防止重放；失败响应不泄漏详细 Cloudflare 错误。
- challenge 页不依赖 React 管理台，保证业务上游故障、管理前端构建失败时仍能工作。
- 为 inline script 和 iframe/connect 域名规划 CSP；至少不能允许 challenge state 被任意 HTML 注入。

### 5. 改造管理员登录流程

修改：

- `src/FastGateway/Services/AuthorizationService.cs`
- 新增登录请求 DTO，例如 `AuthorizationRequest { Password, TurnstileToken }`
- `src/FastGateway/Infrastructure/AppJsonContext.cs`
- `web/src/services/AuthorizationService.ts`
- `web/src/pages/login/index.tsx`
- 新增无 JWT 的公开配置 endpoint，例如 `/api/v1/authorization/challenge-config`

后端：

- 将当前 `POST /api/v1/authorization?password=...` 改为 JSON POST，避免密码进入 URL、代理访问日志和浏览器历史。
- 当 `EnabledForAdminLogin` 开启时，缺少或无效 Turnstile token 直接拒绝；密码校验仍保持现有语义。
- 增加登录接口按 IP 的失败/请求速率限制，避免把 Turnstile 作为唯一防护。
- 配置 endpoint 只返回 `enabled`、`siteKey` 等公开字段，不返回 secret/signing key；配置缺失时给出可诊断但不含秘密的错误状态。
- 保持 JWT 签发逻辑不变；Turnstile 只负责证明当前登录请求来自较可信的浏览器，不替代 JWT。

前端：

- 新增一个无第三方 React 运行时依赖的 `TurnstileWidget` 包装组件，动态加载官方脚本并清理 widget；补充 TypeScript 全局声明。
- 登录页加载公开 challenge config；启用时在提交按钮前显示 managed widget，拿到 token 后与密码一起 POST。
- token 过期、组件加载失败或验证失败时清空 token，提示用户重新完成挑战；不要把 token 持久化到 localStorage。
- `fetch.ts` 继续只负责管理 API Bearer token，不把业务 clearance 逻辑混入管理 API 请求拦截器。

这是管理登录接口的公开请求契约变更；当前仓库内调用方只有 React 管理台，仍需在发布说明中标注旧 query 参数不再作为安全登录方式。

### 6. 接入统计、日志和配置热更新

修改统计相关代码以区分机器人拦截：

- `src/FastGateway/Services/Statistics/StatisticsCollector.cs` 的 `BlockReason` 增加 `BotChallenge`。
- 检查后台聚合和安全日志展示，将它归入 403 拦截但保留独立原因，便于判断挑战误杀率和攻击流量。
- `StatisticsCaptureMiddleware` 已位于机器人中间件外侧，能够记录 challenge 返回的 403/503；内部 challenge endpoint 应按现有 internal 路径规则排除或单独统计，避免污染业务 PV。
- 配置变化时，域名保护开关随 `ConfigurationService` 的原子落盘、`ClusterConfigApplier` 的快照应用和 `ReloadGateway()` 生效；Turnstile secret/signing key 变化则要求重启或显式刷新运行时凭证，避免旧 key 与新 key 混用。

## 验证计划

因为当前仓库没有测试项目，新增 `tests/FastGateway.Tests`（xUnit）覆盖：

- clearance payload 的签名、过期、host/route 不匹配、篡改和恒定时间校验。
- Turnstile 响应成功、失败、action 不匹配、hostname 不匹配、超时和 Cloudflare 5xx。
- 中间件对未保护路由、ACME、internal gateway、OPTIONS、有效 clearance、无效 clearance 的行为。
- GET/HEAD 返回挑战页并在 verify 后 303；POST/升级请求不重放 body。
- 上游收不到内部 clearance cookie 和 challenge header。
- 域名配置默认关闭、热重载 metadata、生效后不重启 Server；集群快照到 worker 后保持相同保护行为。
- 管理员登录在 Turnstile 开关开启/关闭时的请求契约和响应。

手工 smoke test：

1. 关闭业务保护，确认现有 HTTP/HTTPS、静态文件、隧道和集群中继行为不变。
2. 打开根路由保护，首次访问得到挑战页；有效 token 后回到原 URL 并能访问上游。
3. 修改路由保护开关，确认 YARP 热重载后立即生效，现有连接不被无故断开。
4. 验证过期 cookie、不同 host、不同 route 和伪造 cookie 都重新挑战。
5. 管理员登录分别验证无 token、无效 token、有效 token、错误密码。
6. 测试 Turnstile 服务不可达时受保护业务返回 503，未保护业务仍可用。
7. 集群中在不同 worker 之间切换请求，确认 clearance 可验证且 secret 不会出现在配置同步/日志/上游请求中。

最终运行：

- `dotnet build FastGateway.sln`
- `dotnet test FastGateway.sln`
- `cd web && npm run build`
- `cd web && npm run lint`

## 交付边界

本次实现只增加 HTTP/HTTPS 业务路由和管理员登录的人机认证，不改变 TCP/UDP `StreamForward` 的协议语义。四层转发没有浏览器上下文，若后续要保护它，应另行设计基于预共享密钥、mTLS 或端口级访问策略，而不是复用 Turnstile。
