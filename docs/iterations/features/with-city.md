# AtomPix Desktop × AtomUI.City 迁移设计

> 设计状态：Design Frozen
>
> 交付状态：Implementation In Progress；Host/State/Routing/MVVM/Build/发布验证及 Localization AXAML 层已完成，剩余项见[迁移主矩阵](with-city-migration-matrix.md)和[决策登记表](with-city-decision-register.md)

## 1. 目标与不变量

City 接管 Desktop 的 Host、模块生命周期、MVVM、顶层路由、View 定位、Outlet、UI Dispatcher、本地化与生成器。迁移不得改变四项单张功能、三项批量功能、输出策略、ImageGallery 交互或 Browse/Operate 普通两列布局。

迁移完成后只能存在一个根服务容器；`App` 不再手工创建 `ServiceCollection`、调用 `BuildServiceProvider` 或直接构造 `MainWindow`。Core、Imaging 和 Infrastructure 不反向引用 City。正式施工顺序和每项阻塞由 `APX-CITY-*` Decision ID 管理，高层矩阵不代替专项合同。

## 2. 生命周期与启动错误分级

新增 `AtomPixDesktopModule`，声明 Desktop 对 City Presentation、Routing、Localization 以及 AtomPix Workflows/Imaging/Infrastructure 注册的依赖。City Host 是唯一组合根。State definition、writer authority 和只读 facade 在首窗前完成结构注册；小型 `settings.json` 仍在主窗口首帧后低优先级初始化，用户提前进入 Settings 与预加载共享同一任务。

启动错误分为三类：

| 等级 | 示例 | 结果 |
| --- | --- | --- |
| L1 启动致命 | 模块图、DI、生成清单、Presentation attach、主窗口/primary outlet 或初始 Home route 无法建立 | 异常越过模块边界；Host 回滚；不显示不可信窗口 |
| L2 功能降级 | 设置损坏、schema 过高、权限或读取失败 | 模块正常启动；`AppSettingsState` 发布 `Faulted`；主窗口继续可用并提供恢复 |
| L3 操作级 | 某次导航、保存或图片操作失败 | 当前操作结束并反馈；保留此前有效状态 |

设置文件不存在是首次启动正常路径；关闭导致的取消是正常生命周期控制。不得用宽泛异常捕获把结构错误降级为 L2。

## 3. 路由与 Browser workspace

三个顶层 route 为 `home`、`browser`、`settings`，但 route presentation 生命周期不等于产品工作会话生命周期：

| Route ID | View/ViewModel 获取 | 离开后的对象状态 |
| --- | --- | --- |
| `home` | 每次进入创建，entry owned | 离开释放 |
| `browser` | View 每次创建；Browser/工具 ViewModel 从 workspace 借用 | View、视觉树、旧 activation scope 释放；workspace 保留或按业务终止 |
| `settings` | 每次进入创建，entry owned | 离开释放 |

压缩、转换、缩放和裁剪是 Browser workspace 内的工具模式；Batch 是 workspace 内的一次 operation，不进入顶层导航历史。

Home 首次进入 Browser 时，`BrowserNavigationSessionStore` 保存不可变 `BrowserNavigationContext` 并生成一次性 session key；City route 只携带该 key。原子消费成功后建立稳定的 `BrowserWorkspaceId`。从 Settings 返回 Browser 使用 workspace ID，不得再次消费初始 session key。

```text
WindowSession
├── ShellViewModel
├── BrowserWorkspaceSession（可选）
│   ├── ImageBrowserViewModel
│   ├── 图片集合、当前项、ActiveTool
│   ├── 四个工具 ViewModel、Batch 和草稿
│   └── viewport/画廊恢复检查点
└── 当前 PresentationEntry
    ├── View 与视觉树
    ├── ActivationScope
    └── 展示期订阅和资源 lease
```

进入 Settings 只替换 `PresentationEntry`，不终止 Browser workspace。返回时以同一 workspace 状态创建新 View、新视觉树和新 activation scope；不得复用已经结束的 scope，也不得通过回放点击或命令恢复界面。当前项、顺序、ActiveTool、工具草稿、裁剪参数、缩放、画布偏移和必要画廊位置必须在视觉上等价恢复；Tooltip、悬停和半途动画等瞬时状态不恢复。

返回 Home、用新来源替换工作会话或关闭窗口时才释放 workspace。前台任务运行期间继续禁止进入 Settings，本轮不设计任务跨 Settings 暂停。

路由与 Presentation 的事务、条件恢复和部分失败语义见[专项设计](with-city-routing-presentation.md)。

## 4. View、命令与关闭

- 使用 City View registry、ViewModel lease、composition、Window session 与 route outlet。
- 删除 Shell 的 `CurrentPage` 对象切换、`DesktopNavigationCoordinator` 和用于路由的手写 DataTemplate。
- Browser workspace ViewModel 使用 borrowed lease；旧 View 和 activation scope 始终按一次展示期释放。
- Batch/消息/结果对话框由 AtomPix adapter 管理，owner window 和关闭时序来自当前 City window session。
- picker、launcher、clipboard 和 AtomUI 反馈 adapter 继续属于产品/平台集成。

窗口 Closing 首次触发时允许取消并异步确认；确认通过后只允许一次授权关闭。City 不得提前释放窗口会话，也不得在取消关闭后留下已停用 ViewModel。

跨命令 latest-wins 依赖 City 新增的最小 `CommandExecutionDomain`。该域只负责多个 Command 共享的执行准入、当前 execution lease/generation、取消和 activation drain；AtomPix 仍负责业务 generation/version guard。不得把本轮扩展成 Queue、Priority、Retry、Debounce 或策略引擎。详见[MVVM 设计](with-city-mvvm.md)。

## 5. State、Settings 与 Recent

Desktop 只注入 AtomPix 的只读 `IAtomPixApplicationState`。设置采用 Store-first、State-second：校验不可变候选值，串行保存 `IAppSettingsStore`，持久化成功后把同一候选发布到 City.State；保存失败不改变 State。City.State 使用 application lifetime 和 transient snapshot，不形成第二套持久化事实。

当前版本删除全部 Recent 用户功能与运行时行为：Home 不显示、不加载、不订阅、不写入最近记录；City 迁移不定义 `RecentItemsState`。兼容字段继续随完整 `AppSettings` 原样往返，应用不主动删除既有 `recent-items.json`。底层类型/Store 的物理删除是独立兼容清理，不得顺带删除用户数据。

所有用户可见中文迁入 City.Localization；当前只交付完整 `zh-CN`，`System` 仅在系统 culture 受支持时采用，否则回退 `zh-CN`。工程接线与 catalog 见[Build、Generators 与 Localization 设计](with-city-build-generators-localization.md)。

## 6. 施工顺序

1. City 完成 Public API/Release gate、ViewModel composition 发布级验证、`CommandExecutionDomain` 和 Routing-Presentation 协调层。
2. AtomPix 固定新的 City gitlink SHA，从同一 SHA 生成唯一版本的本地 `AtomUI.City.Build` 包。
3. 按决策登记表施工 Host/State，再施工 Routing/Presentation、MVVM、Localization，随后删除旧双栈。
4. 完成发布功能测试、第三方许可证/SBOM 闭环和全量发布验证。

AtomPix 不在 City 前置项完成前实现临时框架替代，也不允许新旧导航或命令基础设施长期并存。

## 7. 验收

- Settings 往返后 workspace 状态和用户可感知界面等价，旧 View/scope 已释放，新 scope 只注册一次。
- L1/L2/L3 错误分级、设置 `Faulted` 重试和 Store-first/State-second 顺序均有故障注入覆盖。
- 正常导航、Router 失败、Presentation 各阶段失败、条件恢复成功/被拒绝和迟到补偿不撤销新导航。
- 同一 `CommandExecutionDomain` 下跨命令替换、取消、停用 drain 与业务迟到提交隔离。
- 关闭被拒绝后窗口仍可操作；授权关闭只发生一次。
- 当前生产代码没有 Recent UI/运行时读写，兼容数据不被破坏。
- 本地化、发布产物功能、许可证/SBOM、Trim/single-file 和全量测试达到工程文档定义的 Release Gate。
