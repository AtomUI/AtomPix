# AtomPix Desktop × City MVVM 迁移设计

> 设计状态：Design Frozen
>
> 交付状态：Verified；City 前置门、AtomPix ownership map 与职责拆分均已通过验证

## 1. 基类、激活与通知

- 可绑定 ViewModel 使用 City `ViewModelBase`/激活合同，不再继承 AtomPix `ObservableObject`。
- 构造函数只接收依赖并建立纯内存初值，不执行 I/O、启动任务或建立长生命周期订阅。
- 展示期订阅和任务属于 activation scope；deactivation 取消并 drain，dispose 是最终幂等兜底。
- 对象生命周期与 activation 生命周期必须分开。Browser workspace 对象可跨 Settings 存活，但旧 scope 必须结束，返回时建立新 scope。
- `PropertyChanged` 和命令状态通知经 Presentation UI dispatcher；业务计算不整体搬到 UI 线程。

## 2. 命令合同与共享执行域

City 的 `Reject`、`Allow`、`RejectByKey` 只对单个 Command 实例生效；固定 SHA `c2e41761` 包含最小 `CommandExecutionDomain` 来协调不同 Command：

```text
ActivationScope / BrowserWorkspace owner
└── CommandExecutionDomain
    ├── Command A
    ├── Command B
    └── current execution lease/generation + cancellation ownership
```

本轮合同只包括：多个 Command 显式加入同一域；原子取得/替换当前执行；取消前一执行；判断 execution lease 是否仍为 current；与 activation stop/drain 组合；资源恰好释放一次。单 Command 的现有 policy 继续存在。

本轮明确不设计 Queue/FIFO、Priority、Retry、Debounce/Throttle、持久化队列、跨进程调度、DAG 或自动业务回滚。AtomPix 决定哪些命令共享 domain，并继续保留 generation/version guard，阻止不响应取消的底层操作迟到提交或重复外部副作用。

命令统一通过 `IUiCommandFactory` 创建；异常进入 `ICommandExceptionHandler` 或 Workflow `OperationResult` 映射。City Public API release gate 已通过；AtomPix 本地 `ObservableObject` 与 Commands 实现已删除。Browser 的选择、Previous、Next 明确使用 `Allow` 的单命令准入并共享 latest-wins domain，加载期允许新的导航意图接管。

## 3. 逐类型生命周期地图

| ViewModel/对象 | Owner / 对象生命周期 | Acquisition / activation | 停用与最终释放 | 命令与竞态重点 |
| --- | --- | --- | --- | --- |
| `ShellViewModel` | `WindowSession` | City composition root | 关闭时 leaf-to-root drain/dispose；不再拥有所有页面对象 | 窗口 chrome、导航投影、close guard；不实现对象页面切换 |
| `HomePageViewModel` | 单次 Home route | EntryOwned | 离开 route 释放 | picker/drag-drop；删除全部 Recent 命令和异步残留 |
| `SettingsPageViewModel` | 单次 Settings route | EntryOwned | 离开丢弃未保存草稿并释放 | load/save/retry；只读订阅 AppSettings State；Store-first 保存 |
| `BrowserWorkspaceSession` | 一次 Browser 工作会话 | 非视觉 owner | 返回 Home、替换会话或窗口关闭时释放 | 持有 workspace domain、稳定 workspace ID 和可恢复状态 |
| `ImageBrowserViewModel` | Browser workspace | route 使用 Borrowed lease；每次新 activation scope | Settings 往返只停用；workspace 终止才 dispose | Probe/preview/load 共享 domain；保留业务 generation guard |
| 四个 Editor ViewModel | Browser workspace | borrowed composition child；活动工具获得展示期 activation | 停用取消展示期任务但保留草稿；workspace 终止释放 | 输入同步、提交和预览按真实冲突关系分 domain |
| `BatchTaskViewModel` | Browser workspace | 单一 composition activation | Tool/结果视觉不得重复激活同一对象 | 批量 generation、进度和取消；提交快照不受设置更新影响 |
| `ToolDrawerSessionViewModel` | 单次 Browser 展示期 | 临时 composition child | 离开 Browser 释放；按 `ActiveTool` 重建 | 只做视觉组合和订阅，不拥有 workspace 草稿 |
| `OutputPolicyEditorViewModel` | 所属工具草稿 | 显式 composition child | 随父激活/停用，对象可由 workspace 保留 | 校验和通知；不独立提交业务任务 |
| Browser/Batch item、反馈值投影 | 所属集合/工具 | 被动对象 | 随父集合释放 | 不建立独立 activation scope；命令尽量提升到父 VM |
| `DiagnosticErrorViewModel` | 所属页面/工具 | 显式 child 或重构为被动状态 | 随 owner 释放 | 禁止持有 City command 却从未激活 |

逐类型施工 PR 必须再列出每个命令使用的 factory 重载、单命令 policy、domain、异常处理、dispatcher 和测试；上表不允许被机械基类替换所代替。

## 4. City composition 发布前置门

City composition 在固定 SHA `c2e41761` 已从 `Release Validation Pending` 提升为 `Release Verified`，完成了以下验证：

1. root/child/grandchild leaf-to-root 释放与 task drain。
2. 任一 child 激活失败后的 sibling、parent 和候选 lease 回滚。
3. 单个清理失败时其余对象仍释放，异常正确聚合。
4. attach、显式 dispose、Window close 与 Runtime stop 竞态。
5. RouteOutlet entry 与 composition tree 在取消、失败和并发关闭时隔离。
6. Borrowed 实例停用后用新 scope 重激活，以及跨 owner 冲突。
7. cleanup/diagnostic handler 自身失败时不丢 registry、不挂死共享关闭任务。
8. 真实 `PresentationRuntime`、`WindowSession`、RouteOutlet 和 activation scope 集成测试。

AtomPix 已使用 `WindowSession -> Shell root -> editors/Batch -> ToolDrawer session` 结构。Home/Settings 由 transient `DesktopEntryViewModelFactory` 按 route 获取，City plan 使用 `EntryOwned` lease；Headless UI 已证明进入 Settings 时旧 Home 被释放、返回时生成新 Home/View/activation scope、离开时 Settings 被释放，同时 Browser workspace 保持同一对象。导航意图由 `DesktopNavigationIntentHub` 发出，前台任务互斥由独立 `DesktopForegroundTaskGate` 决策；编辑器分别注入 `IDesktopNavigator` 与 `IDesktopForegroundTaskGate`，不再存在混合职责的生产 coordinator。

## 5. 删除与验收

最终删除 AtomPix `ObservableObject`、`RelayCommand<T>`、`AsyncCommand`、`AsyncCommand<T>`、无 owner 的 `_ = XxxAsync()` 和非事件型 `async void`。Core 的 `OperationResult`/错误模型保持不变。

验收覆盖激活/停用/再次激活、订阅计数、迟到提交、domain 跨命令替换、用户取消、route leave、host shutdown、命令异常、CanExecute 线程、参数校验、dispose 后拒绝执行和错误只展示一次。

2026-10-21 Release 验证：Desktop 无窗口测试 `77/77`、Desktop Headless UI 非压力测试 `18/18`、UI virtualization 压力测试 `1/1`；完整非压力基线 `410`、压力基线 `4`，Release build `0` 警告/`0` 错误。`APX-CITY-MVVM-002` 据此转为 `Verified`。
