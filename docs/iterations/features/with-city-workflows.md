# AtomPix Workflows × City.State 迁移设计

> 设计状态：Design Frozen
>
> 交付状态：Verified；City.State 设置投影、唯一 writer 与 Recent 退出边界均已验证

## 1. 所有权与唯一事实源

当前 City 迁移只为有真实消费者的设置建立共享状态，不建立 `RecentItemsState`。

```text
持久化事实源：settings.json / IAppSettingsStore
进程内只读投影：City AppSettingsState
唯一写入编排者：AtomPix Workflows
Desktop/工具：只读 facade
```

图片转换、压缩、缩放、裁剪和批处理是调用级用例。进度使用 `IProgress<T>`，取消使用调用 token，结果使用既有 Workflow/Core 模型，不进入 City.State。

## 2. 状态合同

AtomPix 定义不可变联合状态：

```text
AppSettingsState
  Uninitialized
  Ready(AppSettings value)
  Faulted(AtomPixError error)
```

`Faulted` 不伪装默认值，也不覆盖 Store 中的数据。UI 可以保留 last-known-good 仅用于明确标注的展示，但该值不能被当作当前已保存事实或新草稿默认值。

City 注册合同：

| 项目 | 决议 |
| --- | --- |
| Key | 稳定的 `StateKey<AppSettingsState>`；名称由 Workflows 常量声明 |
| Owner | AtomPix Workflows module |
| Lifetime | `Application` |
| Access | `OwnerWrite`；writer 由 `IApplicationStateWriterFactory` 和模块 authority 创建 |
| Snapshot | `Transient`；禁止 City 再持久化设置 |
| Read surface | `IAtomPixApplicationState.AppSettings`，只公开读取和观察 |
| UI observe | `Observe` + Dispatcher policy；subscription 由 activation scope 释放 |

Desktop 不得从根容器取得 writer、writer factory 或 State key 的写入口。项目引用和架构测试必须守住该边界。

## 3. 初始化与错误分级

State 定义与 writer authority 在模块结构初始化阶段注册；实际 Store load 保持产品现有时序，在主窗口首帧后低优先级执行，用户提前进入 Settings 与该预加载共享同一任务。Workflows 初始化器从 Store 加载后发布：

- 文件不存在：`Ready(AppSettings.Default)`。
- 合法设置：`Ready(value)`。
- 明确的格式、schema、权限或 I/O 失败：`Faulted(error)`，作为 L2 功能降级；模块仍正常完成启动。
- State 注册、writer authority、registry lifetime 或状态不变量错误：L1 结构故障；异常越过模块边界并触发 Host 回滚。

Settings `Faulted` 时 Home、Browser、导入和显式参数图片操作仍可用；在重试成功或用户确认重置前，不创建依赖默认设置的新工具草稿。不得静默回退内置默认值。

## 4. 保存顺序与并发

设置保存必须遵循：

```text
校验并冻结 candidate
-> 进入 application-scoped 设置写入串行区
-> IAppSettingsStore.SaveAsync(candidate)
-> City writer 发布 Ready(candidate)
-> 返回保存成功
```

规则：

1. Store 失败时不修改 State；Settings 保留 Dirty 草稿并反馈 L3 错误。
2. Store 成功后发布同一个不可变 candidate；当前 Store 不改变输入，无需再次读取磁盘。
3. State 发布完成后保存操作才返回成功。
4. 两个保存不得交错完成；State version 顺序必须等于持久化成功顺序。
5. Store 已成功而 State 因注册、权限或生命周期错误无法提交时，磁盘事实不能假装回滚。该路径是结构故障，记录诊断并使集成测试/启动验证失败。
6. State 提交后 UI dispatcher 投递失败不回滚已提交状态；City diagnostics 必须可观察该失败，页面重新观察时取得最新版本。
7. Host shutdown 先停止新写入，再等待或取消串行区，最后释放 State/Host；测试每例使用独立 Host，不能跨例泄漏 application state。

工具 ViewModel 只在明确创建新草稿时读取 `Ready` 设置；后续状态更新不得覆盖既有工具草稿或活动任务快照。

## 5. Recent 退出边界

- Home 不注入 Recent workflows，不订阅集合，也不在打开成功后记录历史。
- Host 不初始化 Recent State；迁移没有 Recent key、writer、Faulted 映射或验收项。
- `AppSettings.RecentItems` 等未暴露兼容字段继续随完整快照原样往返。
- 应用不删除现存 `recent-items.json`。遗留 Core/Workflow/Store 类型的物理清理另行评估格式兼容，不阻塞本迁移。

## 6. 验收

- 首次启动、合法设置、损坏、schema 过高、权限失败、重试和确认重置。
- 保存成功后发布、保存失败不发布、并发保存顺序和 State version 顺序。
- dispatcher 投递失败后提交仍存在，重新观察取得最新值。
- writer authority、snapshot policy、Desktop 只读边界和跨测试隔离。
- 当前草稿/活动任务不被新设置覆盖；新草稿取得新设置。
- 生产 Host、Home 和成功打开来源路径不再加载、观察或写入 Recent。
