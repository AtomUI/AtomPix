# AtomPix Workflows × City.State 迁移设计

> 状态：目标设计冻结；随整体 City 迁移暂停

## 1. 所有权

设置和最近项目是多个调用方共享的应用事实，属于 Workflows；它们不是 Desktop 页面私有状态。文件 Store 继续是持久化事实源，City.State 只提供当前值、阶段和有序变更观察。

图像转换、压缩、缩放、裁剪与批处理是调用级用例。进度继续使用 `IProgress<T>`，取消继续使用调用方 token，完成结果继续使用现有 Workflow/Core 模型，不进入全局 State。

## 2. 公共合同

新增 `IAtomPixApplicationState`，只公开读取与订阅能力：

- `AppSettingsState`：`Uninitialized | Ready(AppSettings) | Faulted(error)`。
- `RecentItemsState`：`Uninitialized | Ready(immutable items) | Faulted(error)`。

writer 与 State key 保持 Workflows internal。Desktop、Imaging 和 Infrastructure 不能取得写入口。快照必须不可变；订阅回调按照 State 的版本顺序观察，不允许先看到内存成功、后看到持久化失败。

## 3. 初始化和写入顺序

1. Host 在首个窗口和路由激活前调用 Workflows 初始化器。
2. 从现有 Store 加载；Store 的“文件不存在则默认值”语义保持不变。
3. 成功后一次性发布 Ready；真实 I/O/解析失败发布 Faulted，并通过现有 UI 错误映射展示。
4. 设置保存：校验 → Store save → 发布新 AppSettings。保存失败不改变快照。
5. 最近项目变更：计算候选 → Store save → 发布新不可变集合。失败不发布候选。
6. 同类写入串行化，并以持久化成功顺序分配 State 版本。

## 4. 消费规则

- Settings 页面编辑的是本地草稿；保存成功后等待 State 新版本成为展示事实。
- Home 页面订阅 RecentItems，不自行重复加载 Store。
- 需要默认参数的 Workflows 从已初始化设置快照读取；调用方明确传入的 profile 优先，不被 State 隐式覆盖。
- State Faulted 时，不把默认值伪装成已保存值；显式参数的图像操作仍可执行。
- 不增加 EventBus 作为第二条设置/最近项目通知通道。

## 5. 验收

- 首次启动、缺失文件、损坏文件、读取失败和重试初始化。
- 保存成功后发布、保存失败不发布、并发保存的最终顺序。
- 最近项目增加、去重、裁剪、清空与失败回滚。
- 多订阅者获得同一版本；释放订阅后不再回调。
- Workflows 测试直接验证真实 City.State 合同，不使用 Desktop 代理证明业务正确性。
