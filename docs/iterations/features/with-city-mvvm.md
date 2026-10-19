# AtomPix Desktop × City MVVM 迁移设计

> 状态：设计冻结 / City Blocked

## 1. 基类与通知

- 所有可绑定 ViewModel 使用 City 的 `ViewModelBase`/激活合同，不再继承 AtomPix 自有 `ObservableObject`。
- 构造函数只接收依赖并建立纯内存初值，不执行 I/O、不启动任务、不订阅长生命周期事件。
- 订阅、预加载和定时工作在 activation scope 中建立；deactivation 必须取消并等待所有者任务，dispose 作为最终幂等兜底。
- PropertyChanged 和命令状态通知使用 City Presentation 提供的 UI dispatcher；业务计算不整体搬到 UI 线程。

## 2. 命令合同

- Avalonia ViewModel 统一注入 `IUiCommandFactory`，同步、异步、泛型命令均由该入口创建。
- 长任务使用 `CommandConcurrencyPolicy.Reject`；搜索、探测或预览刷新等 latest-wins 操作使用 City 支持的替换/取消语义，并以版本号防止迟到提交。
- 取消令牌使用 City 的 effective token，不再由每个 AtomPix 命令维护独立 CTS。
- 所有异常进入明确的 `ICommandExceptionHandler` 或 Workflow `OperationResult` 映射；不得依赖 `async void` 或遗失 Task。
- `CanExecute` 的输入状态发生变化时必须显式通知；同一操作执行期间不得通过另一命令绕过互斥约束。

当前 City 提交虽然实现并测试了 `IUiCommandFactory`，但其签名仍位于 Presentation `PublicAPI.Unshipped.txt`，且 Build 发布门要求该文件为空。该冲突解除前不能删除 AtomPix 现有命令层。

## 3. 异步与生命周期清单

施工时逐个 ViewModel 完成以下审计，不允许机械替换基类后收口：

1. 构造期 I/O 和预加载迁移到 activation。
2. `_ = XxxAsync()` 改为命令、activation-owned task 或显式 task owner。
3. 事件订阅与 State subscription 可重复激活且不会重复注册。
4. 取消、窗口关闭和 route leave 使用同一所有权树。
5. 文件探测、缩略图与编辑预览的迟到结果不得覆盖新选择。
6. Batch 进度只在当前执行版本上提交。
7. UI 通知经 dispatcher，Workflow 与 Imaging 保持线程无关。
8. dispose 后命令拒绝执行，异步 continuation 不再更新属性。
9. 泛型命令对 null、错误参数类型和无选择状态的行为有测试。
10. 错误展示只发生一次，不同时由命令异常和结果 guard 重复提示。

## 4. 类型迁移

- 删除 AtomPix `ObservableObject`、`RelayCommand<T>`、`AsyncCommand`、`AsyncCommand<T>`。
- XAML 绑定继续面向 `ICommand`/CommunityToolkit 命令接口，不在 View 中依赖 City 实现类型。
- Core 的 `OperationResult`/错误类型保持不变；若与 City 命名相似，通过 namespace 明确区分，不重命名领域合同。
- 应用特有的 `DesktopOperationResultGuard`、对话框请求和页面输入模型保留，但不得复制命令并发、异常或取消状态机。

## 5. 测试门

- 激活/停用/再次激活、dispose 幂等、订阅计数与迟到提交。
- Reject 与 latest-wins 并发、用户取消、route leave 和 host shutdown。
- 命令异常处理、CanExecute 通知线程、参数校验和执行中状态。
- 静态审计生产代码中的裸 `_ =`、`async void`、本地命令类型和直接 Dispatcher 调用。
