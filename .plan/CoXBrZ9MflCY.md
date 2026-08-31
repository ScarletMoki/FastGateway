# FastGateway 机器人认证实施计划

## 一、现有架构结论

- 控制面在 `src/FastGateway/Program.cs` 中配置 JWT Bearer，并通过 `UseAuthentication()` / `UseAuthorization()` 保护 API；大部分管理 API 组使用 `.RequireAuthorization()`。
- 登录接口 `src/FastGateway/Services/AuthorizationService.cs` 的 `/api/v1/authorization` 当前是匿名入口，密码通过 query string 传递后直接签发 JWT。
- 管理台 `web/src/pages/login/index.tsx` 调用 `web/src/services/AuthorizationService.ts`，当前密码也拼接到 URL；`web/src/utils/fetch.ts` 将 JWT 放在 `localStorage` 并自动附加 Bearer。
- 每个 `Server` 会在 `src/FastGateway/Gateway/Gateway.cs` 中创建独立的 Kestrel/YARP 子应用。业务 HTTP 流量在客户端 IP 解析、统计、限流、黑白名单、故障转移等中间件后进入 `MapReverseProxy()`；这些子应用不会自动继承主控制面的 JWT 管道。
- `src/FastGateway/Gateway/StreamProxyManager.cs` 是独立的裸 TCP/UDP socket 转发器，只能在连接/首个 UDP 数据报阶段做 IP 访问控制，无法显示浏览器挑战页。
- 现有 `BlacklistAndWhitelistService` 已提供可复用的 L4 来源 IP 判断，`RateLimitService` 已提供 HTTP 按 IP 限流。
- 项目默认 Native AOT，新增 JSON DTO、配置 DTO 和第三方验证响应类型都必须加入 `AppJsonContext` / `ConfigJsonContext`，不能依赖运行时反射序列化。

## 二、目标方案

采用“提供商适配层 + 同源 clearance + 两个接入点”的设计：

1. 管理台登录：React 挑战组件取得一次性 challenge token，服务端先向机器人认证提供商验证，成功后才校验管理员密码并签发 JWT。
2. 业务 HTTP/HTTPS：在每个业务网关子应用的 YARP 前增加 BotProtection middleware。未携带有效 clearance 的浏览器 GET/HEAD 请求跳转到同源挑战页；挑战成功后服务端验证 token，签发仅对当前业务 Host 生效的 HttpOnly clearance cookie，再放行原始请求。
3. L4 TCP/UDP：不接入浏览器挑战。对任意 TCP/UDP 客户端只能继续使用 IP 黑白名单、限流/连接数控制、预共享访问令牌、mTLS 或上游协议认证；若目标协议本身是 HTTP，应配置为 HTTP 域名代理而不是 `StreamForward`。
4. 首期实现 Cloudflare Turnstile provider，但核心接口不绑定 Turnstile，后续可接入 hCaptcha 或其他 provider。

## 三、后端实施步骤

### 1. 增加通用机器人认证模型和提供商接口

新增 `src/FastGateway/Options/BotProtectionOptions.cs`、`src/FastGateway/Infrastructure/BotProtection/` 下的接口、DTO 和 provider 实现：

- `BotChallengeProvider` 枚举：首期 `Turnstile`，保留扩展值。
- `BotProtectionOptions`：启用开关、provider、公开 site key、secret key、challenge/clearance 有效期、管理台登录是否启用等配置。
- `IBotChallengeProvider`：接收 token、请求 Host、客户端上下文和 action，返回成功标记、hostname/action 校验结果及失败原因。
- `TurnstileProvider`：服务端通过 `IHttpClientFactory` 调用 Siteverify；secret key 只存在服务端配置，不下发前端。
- 使用强类型 `TurnstileVerifyRequest` / `TurnstileVerifyResponse`，并注册 AOT JSON 元数据。
- provider 返回值统一化，middleware 不直接依赖第三方响应字段。

配置建议放在环境变量、Secret 或部署配置中，不把 secret key 写入 `gateway.config` 或下发到管理台；公开 site key 可以通过匿名配置接口或登录页初始配置注入。

### 2. 抽取 clearance 签发和校验服务

新增 `BotClearanceService`，使用独立于 JWT 的 HMAC 密钥签发短期、无状态票据：

- 票据至少绑定：版本、业务 Host、签发时间、过期时间、provider/action、随机 nonce。
- cookie 使用 `HttpOnly`、`Secure`，按当前 Host/path 限定；cookie 名称使用项目专用前缀。
- 默认不强绑定完整 IP，避免移动网络和代理出口变化导致误杀；只有确认上游代理可信时才启用 IP 摘要绑定。
- 签名密钥单独配置，不能复用 JWT secret；支持轮换时保留当前/上一把密钥短期验签。
- 校验时检查签名、过期时间、目标 Host、版本和 action；不能只判断 cookie 是否存在。
- 对外部中继请求使用统一的 Host/clearance 语义，内部集群令牌不能被当作访客 clearance。

### 3. 保护管理台登录

修改 `src/FastGateway/Services/AuthorizationService.cs`：

- 将登录输入改为 JSON body DTO，例如 `password` + `challengeToken`，去掉 query string 传密码，避免 URL、代理、访问日志泄漏。
- 保持登录接口匿名，但在密码校验前执行 provider 验证；provider 关闭时兼容本地部署的无挑战模式。
- 校验 provider 返回的 hostname/action，拒绝空 token、跨站 token、过期/重复 token和 provider 失败。
- 增加登录入口按客户端 IP 的失败限流/退避；验证码不是密码爆破防护的替代品。
- 统一 401/429/验证失败响应，避免暴露“密码正确但挑战失败”等可用于枚举的信息。
- 继续签发现有 JWT，以降低一次改动对管理 API 的影响；同时修复 JWT `ClockSkew` 不应等于完整 `ExpireDay` 的问题，避免实际有效期被放大。

修改 `web/src/services/AuthorizationService.ts` 和 `web/src/pages/login/index.tsx`：

- 增加 provider 无关的 `BotChallengeWidget`，首期通过 Turnstile adapter 加载脚本和渲染 widget。
- 登录按钮只有在 challenge token 可用时才提交；token 使用一次后清空，失败时允许重新获取。
- 登录请求改为 `postJson` body，不再拼接密码 URL。
- 保持现有登录成功后的 JWT 流程；后续可单独规划 HttpOnly session cookie，避免 `localStorage` 遭受 XSS 读取，不与本次业务挑战改造混在一起。
- `web/src/utils/fetch.ts` 不再在没有 token 时发送 `Authorization: Bearer null`，并保留 401 跳转逻辑。

### 4. 增加业务域名级策略

扩展 `src/Core/Entities/DomainName.cs`，新增默认关闭的 BotProtection 配置，建议至少包括：

- 是否启用。
- 挑战模式：关闭、浏览器请求挑战、始终挑战或后续的可疑请求挑战。
- 可选 action/策略标识。
- 不在 `DomainName` 中保存 provider secret/site key，避免配置同步和 API 返回时泄漏。

原因是一个 `Server` 可能承载多个域名和路径，机器人策略应跟随 YARP 路由，而不是粗粒度挂在整个端口上。新增字段会随现有 MessagePack 集群配置同步，并通过默认值兼容旧配置。

### 5. 在每个 HTTP 网关子应用中增加 middleware

在 `src/FastGateway/Gateway/Gateway.cs` 的现有管道中新增 `UseBotProtection(...)`，位置放在：

- `UseClientIpResolution` / `UseInitGatewayMiddleware` 之后，确保拿到统一客户端 IP和 Host；
- 现有限流、黑白名单之后，避免挑战页绕过已有访问控制；
- `MapReverseProxy()` 之前，确保请求不会先到上游。

middleware 行为：

- 按当前 Host、Path、Method 匹配 `DomainName` 的 BotProtection 策略，支持现有域名通配符和路径语义。
- 放行 ACME、集群中继、隧道内部端点、OPTIONS、已配置的健康检查/内部路径；可信集群中继不能在每一跳重复触发访客挑战。
- 有效 clearance 直接放行；无 clearance 时，浏览器 GET/HEAD 重定向到同源 `/_fastgateway/challenge?return=...`。
- API、非幂等方法、非浏览器客户端不重定向，返回机器可读的 403/挑战必需响应；客户端在完成挑战后带 clearance 重试原请求。
- challenge endpoint 本身必须 bypass BotProtection，且严格校验 return URL 只能回到当前 Host，防止开放重定向。
- provider 验证成功后设置 clearance cookie并重定向回原路径。
- 不把内部 clearance cookie转发给真实上游；必要的客户端 IP、风险结果使用受控的内部 header，并在直连上游前清理。
- 返回清晰的 Cache-Control，避免 CDN/浏览器缓存带有用户状态的挑战响应。

由于现有 `Gateway.ReloadGateway()` 只热更新 YARP 路由而不会重建 middleware，不能把域名策略只捕获在启动时的 List 中。应新增每个网关实例的 `BotProtectionPolicyStore`，在 `BuilderGateway()` 初始化，在 `ReloadGateway()` 同步更新，实现配置变更即时生效。

### 6. 提供业务挑战前端

新增可被业务 Host 同源访问的 challenge 页面/资源：

- 页面只展示 provider widget 和安全状态，不暴露 secret。
- challenge 结果 POST 到当前 Host 的内部 endpoint，避免跨域和第三方 cookie问题。
- challenge token、return URL、action 使用短期服务端上下文绑定，不能由客户端任意指定策略。
- 可复用管理台的 provider adapter，但业务 challenge 页面不能依赖管理台 JWT或主站 origin。
- 优先提供小型独立 challenge bundle/HTML，避免把整个 React 管理台路由暴露到业务域名；若复用 React 构建产物，需明确其在每个 gateway 子应用中的静态资源发布方式。

### 7. AOT、配置和集群同步

- 将新增请求/响应 DTO 注册到 `src/FastGateway/Infrastructure/AppJsonContext.cs`。
- 将需要落盘的策略字段加入 `ConfigJsonContext` 覆盖的 `GatewayConfig` 序列化链路。
- provider 注册使用显式 DI 类型，不使用反射扫描或动态 JSON 类型。
- cluster 配置快照只同步业务策略，不同步 secret；所有节点通过部署级配置持有相同 provider secret 和 clearance signing key。
- 对配置缺失、provider 超时、provider 5xx 定义明确 fail-open/fail-closed 策略：管理台登录默认 fail-closed；业务站点可配置，安全敏感站点默认 fail-closed，provider 故障时返回可观测的 503 而不是把所有请求永久放行。

## 四、验证计划

新增 xUnit 测试项目到 `tests/`，覆盖：

- provider 成功、失败、超时、hostname/action 不匹配、重复 token。
- clearance 签发、篡改、过期、错误 Host、密钥轮换和跨节点验签。
- Host/path/wildcard 策略匹配，默认关闭和配置热更新。
- GET/HEAD 重定向、API 403、OPTIONS 放行、ACME/隧道/集群 bypass。
- clearance 不被转发到上游，return URL 不可跳转到外部站点。
- 管理台登录在无 token、无效 token、有效 token/错误密码、有效 token/正确密码下的状态码。
- L4 回归测试：黑名单仍能阻断 TCP 新连接和 UDP 首包；明确验证不会错误地期待浏览器 challenge。

手工/集成验证：

- `dotnet build FastGateway.sln`，同时使用 `-p:PublishAot=false` 验证可移植构建。
- `cd web && npm run build` 与 `npm run lint`。
- 启动主应用和至少一个 HTTP gateway，验证登录、业务 challenge、clearance 续期和配置热更新。
- 使用 curl/非浏览器客户端访问受保护 API，确认返回机器可读挑战响应而不是 HTML 重定向。
- 使用两个集群节点分别完成 challenge，确认同一签名密钥下 clearance 可跨节点使用；验证 relay/tunnel 不被访客策略拦截。
- 检查日志、统计和 403/429 计数，确保 challenge 失败不会泄漏密码/provider secret，且挑战流量不会被错误计入真实上游请求。

## 五、明确不纳入本次实现的相邻事项

- 不尝试对任意 TCP/UDP 流量注入浏览器挑战。
- 不自研声称等价于 Turnstile 的“无感机器人识别”；自建 JS/PoW 只能作为低强度补充，不能替代第三方验证。
- 不把 provider secret 放进前端、`DomainName`、MessagePack 集群快照或静态资源。
- JWT 改为 HttpOnly cookie、CSRF 全面治理、可信代理 IP 白名单和 CORS 收紧属于独立安全加固；本次至少修复登录密码 query 泄漏，并在实现时保留后续迁移边界。
