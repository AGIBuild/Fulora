# Fulora 兼容矩阵提案（Baseline / Extended）

> 本文是“可验证、可测试、可验收”的兼容矩阵提案，用于替代“完全模拟官方 WebView”的不可验证目标。
> 核心原则：任何“支持/一致”的声明都必须落到矩阵条目 + 明确语义 + 对应测试用例。
> 当前生效的 capability contract 与 release-line 状态请以 `framework-capabilities.json` 和 `platform-status.md` 为准；本文保留为历史设计提案与背景材料。

---

## 1. 术语与范围

### 1.1 平台与模式

- **Windows**：WebView2
- **macOS/iOS**：WKWebView
- **Android**：Android WebView
- **Linux**：仅 `WebDialog`（不提供嵌入式 Baseline）

模式（Mode）：
- **Embedded**：嵌入式控件（`IWebView`）
- **Dialog**：弹窗模式（`IWebDialog`）
- **Auth**：认证模式（`IWebAuthBroker`，通常基于 Dialog + 隔离策略）

### 1.2 支持等级

- **Baseline（必达）**：库对外承诺的“默认可用能力”，必须有跨平台一致的契约语义与测试覆盖。
- **Extended（尽力）**：可用但允许平台差异；仍需矩阵标注与差异条目（Platform Notes）。
- **Not Supported**：明确不支持，不以“未来可能”模糊处理。

### 1.3 “支持”的可验证定义

每个能力条目必须至少包含：
- **语义**：成功/失败/取消/超时/线程语义、事件顺序等
- **覆盖**：在哪些平台+模式支持（Embedded/Dialog/Auth）
- **验收**：对应的测试类型与最小用例集合

测试类型（用于矩阵标注）：
- **CT（Contract Test）**：平台无关、基于 MockAdapter，验证契约语义/状态机/事件顺序
- **IT（Integration Test / Smoke）**：平台最小冒烟，验证“能跑 + 能回调 + 不崩溃”

---

## 2. 兼容矩阵（提案 v1）

标记说明：
- ✅ 支持（Baseline/Extended 由列“Level”定义）
- ⚠️ 支持但有平台差异（必须在“Platform Notes”中列差异条目）
- ❌ 不支持
- N/A 不适用（例如 Linux Embedded）

### 2.1 Core WebView API（`IWebView`）

| Capability | Level | Windows (Embedded) | macOS/iOS (Embedded) | Android (Embedded) | Linux (Embedded) | Dialog | Test | Platform Notes |
|---|---|---:|---:|---:|---:|---:|---|---|
| `Source` get/set | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Source=last requested URI (not necessarily loaded) |
| `NavigateAsync(Uri)` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Must emit Started/Completed exactly-once per NavigationId |
| `NavigateToStringAsync(string html)` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Encoding/baseUri differences must be documented if any |
| `InvokeScriptAsync(string script)` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Return value normalized to string? define per contract |
| `CanGoBack/CanGoForward` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Update timing and event for capability change |
| `GoBack()/GoForward()` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Sync API: UI-thread only; illegal call semantics fixed in contract |
| `Refresh()/Stop()` | Baseline | ✅ | ✅ | ✅ | N/A | ✅ | CT+IT | Stop cancels current navigation (Completed=Canceled/Superseded) |
| `TryGetCookieManager()` | Extended | ⚠️ | ⚠️ | ⚠️ | N/A | ⚠️ | CT+IT | Platform storage model differs; must list supported ops |
| `TryGetCommandManager()` | Extended | ✅ | ✅ | ✅ | N/A | ✅ | CT | Copy/Cut/Paste/SelectAll/Undo/Redo via native editing commands |
| `CaptureScreenshotAsync()` | Extended | ✅ | ✅ | ✅ | N/A | ✅ | CT | Returns PNG bytes; all 5 platforms supported |
| `PrintToPdfAsync()` | Extended | ✅ | ✅ | ❌ | ❌ | ❌ | CT | Windows/macOS/iOS: PDF bytes; Android: `NotSupportedException`; GTK: not implemented (WebKitGTK lacks PDF export API) |
| `Rpc` (JS ↔ C# RPC) | Extended | ✅ | ✅ | ✅ | N/A | ✅ | CT | JSON-RPC 2.0 over WebMessage bridge; requires bridge enabled |
| `INativeWebViewHandleProvider` | Extended | ✅ | ✅ | ✅ | N/A | ✅ | IT | Handle type differs per platform; document stability & lifetime |

#### 2.1.1 macOS WKWebView（Embedded）M0 验收标准（CT + IT 对齐）

- **目标**：在不要求消费方使用 `net10.0-macos` 的前提下，macOS 上默认启用 WKWebView 适配器；并且满足 v1 契约语义的关键不变量（导航拦截/取消、事件一致性、最小脚本/消息闭环）。
- **CT（Contract Tests）**：以 `tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1*.cs` 为准，覆盖 v1 语义（`NavigationCompleted` exactly-once、supersede/cancel、线程/事件顺序、脚本/消息语义等）。
- **IT（Integration Smoke - macOS）**：在 `tests/Agibuild.Fulora.Integration.Tests` 的 UI 页签 **macOS WK Smoke** 中提供最小冒烟闭环，覆盖：
  - **Link click**：DOM 点击链接触发主框架导航
  - **302 redirect**：重定向链路（若平台回调可见）要求同一链路复用 `CorrelationId`
  - **`window.location`**：脚本触发导航
  - **Cancel/deny**：host 返回 deny（`IsAllowed=false`）时应取消导航并得到可观测的 Completed
  - **Script**：`InvokeScriptAsync` 最小返回值验证
  - **WebMessage**：最小 JS→C# 消息闭环（`WebMessageReceived` 可触发）
- **IT 自动运行入口**：桌面集成测试 App 支持 `--wk-smoke` 自动运行并以退出码表示成功/失败。

#### 2.1.2 macOS WKWebView（Embedded）M1 验收标准（CT + IT 对齐）

- **目标**：将 macOS adapter 从 M0 基线提升到生产就绪，覆盖 cookie 管理、new-window 回退、原生句柄访问、错误分类和 baseUrl 支持。
- **CT（Contract Tests）**：新增 `ContractSemanticsV1CookieTests`、`ContractSemanticsV1NewWindowFallbackTests`、`ContractSemanticsV1ErrorCategorizationTests`、`ContractSemanticsV1BaseUrlTests`，覆盖：
  - `TryGetCookieManager()` 在有/无 `ICookieAdapter` 时返回值
  - Cookie CRUD（set/get/delete/clear）通过 mock 内存存储
  - Cookie 操作在 dispose 后抛出 `ObjectDisposedException`
  - `NewWindowRequested` 未处理时回退导航、`Handled=true` 时无回退、null URI 无动作
  - `WebViewNetworkException`/`WebViewSslException`/`WebViewTimeoutException` 类型保留
  - `NavigateToStringAsync(html, baseUrl)` 的 `Source` 和 `RequestUri` 语义
- **IT（Integration Smoke - macOS）**：扩展冒烟场景，覆盖：
  - Cookie set → 页面读取 `document.cookie` 验证
  - Cookie get → 页面设置 cookie 后 `GetCookiesAsync` 验证
  - Cookie delete/clear 验证
  - `TryGetWebViewHandle()` 返回非 null 且 descriptor=`"WKWebView"`
  - 导航到无效主机 → `NavigationCompleted.Error` 为 `WebViewNetworkException`
  - `NavigateToStringAsync(html, baseUrl)` 相对资源解析验证

#### 2.1.3 Windows WebView2（Embedded）M0 验收标准（CT + IT 对齐）

- **目标**：实现 Windows 平台 WebView2 适配器，覆盖全部 v1 契约语义（导航拦截/关联、脚本、消息桥接）及 M1 扩展能力（cookie 管理、错误分类、原生句柄、baseUrl 支持），实现与 macOS adapter 的完整对等。
- **CT（Contract Tests）**：复用 M0/M1 全部 CT 用例（196 项），因 CT 为平台无关（MockAdapter），无需新增。
- **IT（Integration Smoke - Windows）**：在 Windows 集成测试 App 中提供冒烟闭环，覆盖：
  - **Link click**：DOM 点击链接触发主框架导航，验证 `NavigationStarted` + `NavigationCompleted` 同一 `NavigationId`
  - **302 redirect**：重定向链路复用 `CorrelationId`，exactly-once completion
  - **`window.location`**：脚本触发导航，验证原生拦截 + 成功完成
  - **Cancel/deny**：handler 设置 `Cancel=true` → deny native step + `NavigationCompleted` status=`Canceled`
  - **Script**：`InvokeScriptAsync` 执行 + `WebMessageReceived` 消息接收闭环
  - **Cookie CRUD**：通过 `ICookieManager` 的 set/get/delete 验证（backed by `CoreWebView2CookieManager`）
  - **Error categorization**：导航到不可达主机 → `WebViewNetworkException`
  - **Native handle**：`TryGetWebViewHandle()` 返回非 null 且 descriptor=`"WebView2"`
  - **baseUrl**：`NavigateToStringAsync(html, baseUrl)` 资源解析验证

##### Cookie 管理

| Capability | Windows/WebView2 | macOS/WKWebView | 其他平台 | 说明 |
|---|---|---|---|---|
| `ICookieManager.GetCookiesAsync` | ✅ M0 | ✅ M1 | ❌ (null) | Windows: `CoreWebView2CookieManager`; macOS: `WKHTTPCookieStore` |
| `ICookieManager.SetCookieAsync` | ✅ M0 | ✅ M1 | ❌ (null) | |
| `ICookieManager.DeleteCookieAsync` | ✅ M0 | ✅ M1 | ❌ (null) | |
| `ICookieManager.ClearAllCookiesAsync` | ✅ M0 | ✅ M1 | ❌ (null) | Windows: `DeleteAllCookies` 同步+fire-and-forget |
| Cookie 隔离模型 | 基于 `CoreWebView2Profile` | 基于 `defaultDataStore` | - | 平台差异：Windows 按 profile 隔离，macOS 共享 defaultDataStore |

##### 错误分类

| 错误类型 | Windows `CoreWebView2WebErrorStatus` 映射 | macOS `NSURLError` 映射 |
|---|---|---|
| `WebViewTimeoutException` | `Timeout` | `-1001` (NSURLErrorTimedOut) |
| `WebViewNetworkException` | `ConnectionAborted`, `ConnectionReset`, `Disconnected`, `CannotConnect`, `HostNameNotResolved` | `-1003`, `-1004`, `-1005`, `-1009` |
| `WebViewSslException` | `CertificateCommonNameIsIncorrect`, `CertificateExpired`, `ClientCertificateContainsErrors`, `CertificateRevoked`, `CertificateIsInvalid` | `-1201` ~ `-1204` |
| `WebViewNavigationException` (base) | 其他所有非成功状态 | 其他所有 |

### 2.2 Events（契约语义优先）

| Event | Level | Windows | macOS/iOS | Android | Linux (Dialog) | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| `NavigationStarted` | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Always on UI thread; Cancel prevents any main-frame navigation (including redirects) |
| `NavigationCompleted` | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Exactly-once per NavigationId; includes Status (Success/Failure/Canceled/Superseded) |
| `NewWindowRequested` | Extended | ⚠️ | ⚠️ | ⚠️ | ⚠️ | IT | Popup policy differs; must document mapping rules |
| `WebMessageReceived` | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Only when bridge enabled + policy passes; otherwise never fires |
| `WebResourceRequested` | Extended | ⚠️ | ⚠️ | ⚠️ | ⚠️ | IT | Interception capability differs; baseline only guarantees “observe” if supported |
| `EnvironmentRequested` | Extended | ✅ | ✅ | ✅ | ✅ | CT | Allows customizing environment/options before init |

### 2.3 Environment / Options（`IWebViewEnvironmentOptions`）

| Option | Level | Windows | macOS/iOS | Android | Linux (Dialog) | Test | Notes |
|---|---|---:|---:|---:|---:|---|---|
| `EnableDevTools` | Extended | ✅ | ⚠️ | ⚠️ | ✅ | IT | Platform-specific behavior; macOS: `OpenDevTools()`/`CloseDevTools()` are no-ops (WKWebView has no public API), Web Inspector accessible via right-click → Inspect Element when enabled; GTK: functional via WebKitGTK inspector |
| `IAsyncPreloadScriptAdapter` | Extended | ✅ | ❌ | ❌ | ❌ | CT | Windows-only (WebView2 `AddScriptToExecuteOnDocumentCreatedAsync`); other platforms fall back to sync `IPreloadScriptAdapter` wrapped in `Task.FromResult` |
| `ContextMenuRequested` | Extended | ✅ | ⚠️ | ⚠️ | ✅ | IT | GTK: wired via WebKitGTK `context-menu` signal; macOS: no-op (WKWebView lacks public context menu interception API); Android: event declared but not raised |
| UserAgent override | Extended | ✅ | ⚠️ | ⚠️ | ❌ | IT | Some platforms restrict UA; document exact effect |
| Persistent storage profile / user data dir | Extended | ✅ | ⚠️ | ⚠️ | ❌ | IT | Windows WebView2 supports explicit user data folder |
| Private/Ephemeral mode | Extended | ⚠️ | ✅ | ⚠️ | ✅ | IT | AuthFlow prefers ephemeral by default |

### 2.4 WebMessage Bridge（JS ↔ C#）

| Capability | Level | Windows | macOS/iOS | Android | Linux (Dialog) | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| Bridge disabled by default | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Default must not emit `WebMessageReceived` |
| Enable bridge explicitly | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Requires explicit opt-in before use |
| Origin allowlist policy | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Non-allowlisted origin must be dropped + observable |
| Protocol/version check | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Unknown protocol version must be rejected |
| Channel isolation per WebView instance | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Message must bind to instance/channel |

### 2.5 Bridge V2 Capabilities

| Capability | Level | Windows | macOS/iOS | Android | Linux (Dialog) | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| Binary payload (`byte[]` ↔ `Uint8Array`) | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Base64 transport; `_encodeBinaryPayload` / `_decodeBinaryResult` round-trip |
| CancellationToken (`AbortSignal`) | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | `$/cancelRequest` → `-32800`; handler CTS cancellation propagation |
| IAsyncEnumerable streaming | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Pull-based `$/enumerator/next` + `$/enumerator/abort`; token-gated |
| Method overloads (`$N` suffix) | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Fewest-param keeps original name; others get `$N` suffix by param count |
| Generic interface rejection (`AGBR006`) | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Open generic `[JsExport]` reports deterministic diagnostic |

### 2.6 Shell Activation（`IDeepLinkRegistrationService`）

| Capability | Level | Windows | macOS/iOS | Android | Linux | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| Deep-link route registration | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Typed declaration accepted/rejected deterministically |
| Activation normalization | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Equivalent URI variants normalize to same canonical route |
| Activation policy admission | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Policy deny blocks dispatch deterministically |
| Activation idempotency | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Duplicate within replay window suppressed |
| Orchestration dispatch | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Primary receives exactly once; no-primary returns failure |
| Platform entrypoint mapping | Extended | ✅ | ✅ | ✅ | ✅ | IT | OS protocol handler → runtime ingress; descriptor per platform |
| Activation diagnostics | Extended | ✅ | ✅ | ✅ | ✅ | CT+IT | Structured events with correlation ID, event type, outcome |

### 2.7 SPA Asset Hot Update

| Capability | Level | Windows | macOS/iOS | Android | Linux | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| Signed package install | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | RSA signature verification before extraction; rejected on mismatch |
| Version activation | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Atomic pointer swap; SpaHostingService serves active version |
| Rollback to previous | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Restores previous activation pointer; fails gracefully if missing |
| Path traversal protection | Baseline | ✅ | ✅ | ✅ | ✅ | CT | External asset paths validated against root boundary |

### 2.8 WebDialog（`IWebDialog`）

| Capability | Level | Windows | macOS/iOS | Android | Linux | Test | Notes |
|---|---|---:|---:|---:|---:|---|---|
| `Show()/Close()` | Baseline | ✅ | ✅ | ✅ | ✅ | IT | Ownership model differs; contract defines minimal guarantees |
| `Show(owner)` | Baseline | ✅ | ✅ | ✅ | ✅ | IT | Owner handle mapping must be documented |
| `Title` | Extended | ✅ | ⚠️ | ⚠️ | ⚠️ | IT | Some platforms may not support native title |
| `CanUserResize` | Extended | ✅ | ⚠️ | ⚠️ | ⚠️ | IT | Best-effort mapping |
| `Resize/Move` | Extended | ✅ | ⚠️ | ⚠️ | ⚠️ | IT | Not guaranteed; return false when not supported |
| `Closing` event | Baseline | ✅ | ✅ | ✅ | ✅ | IT | Must fire once; order vs Close() specified in contract |

### 2.9 AuthFlow（`IWebAuthBroker`）

| Capability | Level | Windows | macOS/iOS | Android | Linux | Test | Contract Requirements |
|---|---|---:|---:|---:|---:|---|---|
| `AuthenticateAsync(owner, options)` | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | CallbackUri required; strict match rules |
| Ephemeral session default | Baseline | ✅ | ✅ | ✅ | ✅ | CT+IT | Default isolates cookies/storage from embedded webviews |
| Timeout & user-cancel result | Baseline | ✅ | ✅ | ✅ | ✅ | CT | Must produce distinct result codes |
| Shared session (opt-in) | Extended | ⚠️ | ⚠️ | ⚠️ | ⚠️ | IT | Platform-dependent; explicitly marked |

---

## 3. 平台差异条目（模板）

任何 ⚠️ 条目都必须落到差异条目（可用于 issue/规范/测试豁免说明）：

- **Capability**：
- **Platforms impacted**：
- **Observed limitation**：
- **User-visible behavior**：
- **Security implications**：
- **Test impact**：哪些 CT/IT 用例需要条件化或替换为平台冒烟
- **Decision**：Baseline/Extended/Not Supported（必须二选一/三选一）

---

## 4. 验收与发布门禁（提案）

- **Baseline 发布门禁**
  - 所有 Baseline 条目必须有 **CT** 覆盖
  - 每个平台至少通过一组 **IT 冒烟**（创建、导航、脚本、消息、关闭）
  - 关键不变量：事件在 UI 线程；`NavigationCompleted` exactly-once；Cancel 行为一致

- **Extended 发布门禁**
  - 必须在矩阵中明确标注 ⚠️ 并提供差异条目
  - 不允许“默默失败”；需要可观测（日志/错误码/返回 false）

---

## 5. 下一步（用于落地的产出物）

- 输出 **契约语义规范 v1**：线程模型、生命周期状态机、事件顺序、取消/超时/失败语义
- 输出 **Contract Tests 清单 v1**：对齐本矩阵 Baseline 条目逐条给出最小用例
- 输出 **平台 IT 冒烟清单 v1**：每个平台最小可运行闭环
