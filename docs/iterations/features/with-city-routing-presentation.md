# AtomPix × City Routing-Presentation 协调设计

> 设计状态：Design Frozen
>
> 交付状态：Verified；City 通用协调层与 AtomPix 业务适配均已接线并通过 Release/UI 验证

## 1. 责任边界

City 已有 Router、View registry、ViewModel lease、WindowSession 和 RouteOutlet，但固定基线没有把“路由提交”和“Presentation 提交”组成可安全恢复的技术事务。AtomPix 不应复制 AtomBox 的应用级串接方案后再自行操作 Router journal。

City 负责：

- 统一编排 Router commit、Presentation prepare/commit 和候选资源所有权转移。
- 为一次路由提交返回带身份/版本的 receipt。
- Presentation 失败后，仅当该提交仍是 Router 当前 generation 时条件恢复旧 route。
- 返回结构化部分失败结果，并保证候选 View、binding、lease 和 plan 恰好释放一次。

AtomPix 负责：

- route、参数、View/ViewModel 和 workspace 的业务映射。
- 失败后选择恢复、保留已提交 route、重试或提升为结构故障。
- 用户错误展示、Browser workspace 创建/释放和产品级副作用。

City 不承诺 Router、视觉树、activation 和任意业务副作用之间的分布式原子事务。

## 2. City 前置合同

最终名称由 City 设计评审决定，最小形态包含：

```text
IRoutePresentationCoordinator
IRoutePresentationPlanFactory
NavigationCommitReceipt(previous, current, generation)
RoutePresentationResult
Router.TryRestoreAsync(receipt)
```

协调流程：

```text
序列化单个 outlet 的导航意图
-> Router commit，取得 receipt
-> plan factory 获取 ViewModel lease、View、binding 和 commit plan
-> RouteOutlet commit
-> 成功后转移 ownership
-> 失败则释放候选，并以 receipt 尝试条件恢复
-> 返回结构化结果
```

结果至少区分：Routing 未提交；Presentation 失败且路由安全恢复；Presentation 失败且路由仍为已提交状态；恢复因后续新导航而拒绝；结构性故障。普通 `Back` 不能冒充条件恢复，因为它可能产生历史副作用、再次触发 guard 或撤销更晚导航。

City 验收必须故障注入 Router、View create、bind、activate、outlet commit、restore 和 cleanup，覆盖恢复成功、恢复被更新导航拒绝、取消/关闭竞态和资源只释放一次。不得扩展为重试、错误页、工作区回收或业务补偿策略引擎。

## 3. AtomPix route graph

| Route | 参数 | ViewModel target | Lease | History |
| --- | --- | --- | --- | --- |
| `home` | 无 | `HomePageViewModel` | EntryOwned | Replace；作为空工作区根 |
| `browser/open/{sessionKey}` | 一次性 `Guid` | 从 session 建立 workspace 后取得 `ImageBrowserViewModel` | Borrowed | 首次 Browser entry |
| `browser/resume/{workspaceId}` | 稳定 `Guid` | 现存 workspace 的 `ImageBrowserViewModel` | Borrowed | Settings 返回目标 |
| `settings?returnTo=...` | 只引用安全返回目标，不携带业务对象 | `SettingsPageViewModel` | EntryOwned | 临时顶层页面 |

压缩、转换、Resize、Crop 与 Batch 不注册顶层 route。固定 outlet 名为 `primary`，物理 target 是 `MainWindow` 主内容区唯一 `ContentControl`。Avalonia 初始化后 attach Presentation；主窗口 `Show` 前在 UI 线程依次 `RegisterWindow("main")`、注册 `primary` outlet、提交初始 Home route。

View lookup 必须是精确的 `ViewModel Type + ViewKey` 注册，不使用名字猜测、程序集扫描 fallback 或手写路由 DataTemplate。route history、选中态和当前页面真相来自 Router/协调结果，Shell 不再维护第二份 `CurrentPage`。

## 4. AtomPix plan factory 与 ownership

`AtomPixNavigationService` 是很薄的业务 adapter：它请求 City coordinator 导航，并提供 plan factory。plan factory 根据 route target：

1. 校验/解析参数。
2. 对 `browser/open` 原子消费 session key；失败时不创建半成品 workspace。
3. 对 `browser/resume` 查找仍由当前 WindowSession 拥有的 workspace。
4. 按表获取 EntryOwned 或 Borrowed ViewModel lease。
5. 精确查找 View、绑定，并生成只用一次的 `primary` outlet plan。

候选 plan 在 City 接收 ownership 前由 factory 所有；提交成功后由 Presentation entry 所有。任何失败、取消或 close 竞争都只能有一个 owner 执行 cleanup。

`BrowserNavigationSessionStore` 使用有界容量和短 TTL；session 只消费一次，取消/过期/失败创建必须清理。`BrowserWorkspaceSession` 由 WindowSession 持有，Settings 往返不释放；回 Home、替换来源或关闭窗口时释放。

## 5. Settings 返回与失败政策

- 进入 Settings 前记录安全的 `returnTo`：Home 或现存 workspace ID，不保存 View/视觉树/scope。
- Dirty 草稿离开时静默恢复最近已保存快照，再提交返回导航。
- Browser 返回使用 `browser/resume`；同一 VM 以新 Borrowed lease 和新 activation scope 重新呈现。
- route guard 拒绝离开时保留当前页面，不创建候选 Presentation。
- Router 成功而 Presentation 失败时，AtomPix优先使用 City receipt 条件恢复；恢复成功后旧页面保持可用并展示一次操作错误。
- 条件恢复因更新导航被拒绝时，不得撤销新页面；记录结构化诊断，并按当前 Router 真相重新协调 Presentation。
- 无法建立可信 route/presentation 对应关系时提升为 L1 结构故障，不显示半绑定页面。

## 6. Close 与并发顺序

单个 `primary` outlet 的导航由 City coordinator 串行化。重复点击相同目标按 Router 合同去重；新导航可以取消尚未提交的旧候选，但不能抢走已转移 ownership。

正常退出顺序：停止接收新导航 → 完成或取消 close guard → 释放当前 Presentation entry → 释放 Browser workspace → 关闭 WindowSession → 停止模块 → 释放 Host。关闭被拒绝时不得释放当前 entry、workspace 或 activation。

## 7. 验收

- Home → Browser、Browser → Settings → Browser、Browser → Home 正常链路。
- session key 缺失、过期、重复消费和 workspace ID 失效。
- Router 失败以及 View create/bind/activate/outlet commit 各阶段失败。
- receipt 恢复成功、被后续导航拒绝、失败后旧页面仍可操作。
- 迟到失败不能撤销更新导航；候选和已提交资源各释放一次。
- close 拒绝、route leave 拒绝、导航与 Host stop 竞争。
- Settings 往返保留 workspace 状态但重建 View/visual tree/activation scope。
