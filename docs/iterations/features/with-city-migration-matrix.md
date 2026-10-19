# AtomPix × AtomUI.City 迁移主矩阵

> 状态：Design Frozen / City Blocked
>
> 实施分支：`feature/with-city`
>
> City 基线：`e058270f469f23453c7df59029784cb4b53fe414`（`develop`，2026-10-19）
>
> 基线日期：2026-10-19

本文是 AtomPix City 迁移的治理入口。范围、能力归属、实施状态和验收证据以本文为准；产品行为仍以 `docs/product/` 为准，模块边界仍以 `docs/architecture/` 与 `docs/modules/` 为准。

## 1. 治理规则

1. City 能力跟随业务事实、状态和生命周期的所有者，不跟随最先调用或展示它的 Desktop。
2. Desktop 只拥有窗口、路由、View/ViewModel、UI 线程与交互状态；设置和最近项目的共享运行时事实属于 Workflows。
3. 持久化 Store 始终是设置和最近项目的事实源；City.State 是成功持久化后的只读运行时投影，不是第二套数据库。
4. 图片处理调用、批处理进度与取消是一次调用内的短生命周期，不升级为 City 后台任务或全局 State。
5. 一个单元只有在所有权、City 契约、依赖、替代范围、失败语义和测试证据全部明确后才能进入施工。
6. City 固定提交的必需契约或发布门失败时标记为 `阻`；AtomPix 不修改 City，也不增加框架级临时替代。

状态含义：`定` 表示采用决策和目标设计已冻结、尚未施工；`否` 表示审计后不采用；`适` 表示只通过上层组合或标准合同间接使用；`阻` 表示决定采用但被 City 契约或发布门阻塞；`已` 表示实现和验证均完成。本文不存在开放的 `审-*` 条目。

## 2. 当前事实与预检证据

- AtomPix 迁移前基线：Release 构建通过；功能与 Headless UI 测试 `398 passed`；压力测试 `4 passed`；漏洞审计未发现易受攻击包；`win-x64` 裁剪发布成功；发布产物 5 秒启动烟测通过。
- City 使用仓库隔离安装的 SDK `10.0.300` 完成 Release 预检。Core、Mvvm、Routing、State、Localization、Presentation 与 Generators 的主体测试均可构建，相关测试集中已观察到 Mvvm `106`、Routing `168`、State `154`、Localization `96`、Presentation `171`、Generators `206` 通过。
- City 完整测试未通过：`AtomUI.City.Build.Tests.PackagingReleaseGateTests.PresentationPublicApiGateUsesFrozenBaselineAnalyzersAndPackageValidation` 要求 `PublicAPI.Unshipped.txt` 为空，但该提交公开了 `IUiCommandFactory` 并把签名留在该文件中。
- City TemplateSmoke 的三项失败中，两项是未先产出本地包，一项是子进程未继承隔离 SDK；它们属于执行环境/顺序问题。公共 API 冻结门失败与顺序无关，并直接命中 AtomPix 的必需命令工厂合同，因此构成阻塞。
- City 当前声明 Avalonia `12.0.4`，AtomPix 为 `12.1.1`。在公共 API 门修复后，仍须通过 AtomPix 真实项目引用构建确认最终依赖解析；不能仅凭 NuGet 的高版本胜出规则认定兼容。

## 3. 主矩阵

### 运行时与交互能力

| AtomPix \\ City | Core | Mvvm | Routing | Presentation | State | EventBus | Localization |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Core | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Imaging.Abstractions | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Imaging.Magick | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 | 否 | 否 | 否 | 否 |
| Workflows | 否 | 否 | 否 | 否 | 定 | 否 | 否 |
| Desktop | 定 | 阻 | 定 | 阻 | 适 | 否 | 定 |
| Engineering | 定 | 阻 | 定 | 阻 | 定 | 否 | 定 |

### 数据、安全与扩展能力

| AtomPix \\ City | Data | Security | PluginSystem |
| --- | --- | --- | --- |
| Core | 否 | 否 | 否 |
| Imaging.Abstractions | 否 | 否 | 否 |
| Imaging.Magick | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 |
| Workflows | 否 | 否 | 否 |
| Desktop | 否 | 否 | 否 |
| Engineering | 否 | 否 | 否 |

### 工程能力

| AtomPix \\ City | Build | Generators | Testing | CLI | Templates |
| --- | --- | --- | --- | --- | --- |
| Core | 否 | 否 | 否 | 否 | 否 |
| Imaging.Abstractions | 否 | 否 | 否 | 否 | 否 |
| Imaging.Magick | 否 | 否 | 否 | 否 | 否 |
| Infrastructure | 否 | 否 | 否 | 否 | 否 |
| Workflows | 否 | 否 | 否 | 否 | 否 |
| Desktop | 阻 | 阻 | 否 | 否 | 否 |
| Engineering | 阻 | 阻 | 否 | 否 | 否 |

Build/Generators 本身没有发现能力缺口，但它们属于同一 Desktop 编译链；在 Presentation/Mvvm 公共 API 基线恢复前统一标记为阻塞，避免产生不可发布的半迁移状态。

## 4. 已冻结的能力归属

- Workflows 直接引用 City.State，拥有 `AppSettings` 和 `RecentItems` 的共享投影；Desktop 只读消费。
- Desktop 直接引用 City.Core、Mvvm、Routing、Presentation、Localization、Build、Generators，作为唯一 Host 与 UI 组合根。
- Core、Imaging 与 Infrastructure 不出现 City 类型；它们继续通过现有端口被 Workflows/Desktop 组合。
- EventBus 不采用：State 已承载共享事实；窗口内一对一交互继续使用明确的命令、属性通知或应用服务。
- Data/Security/PluginSystem 不采用：AtomPix 当前没有远程数据管线、应用主体权限或插件产品合同。
- City.Testing、CLI、Templates 不采用：现有真实 Host、Headless UI、发布脚本与项目结构提供更直接的产品证据。

## 5. 施工解锁条件

必须同时满足以下条件，矩阵中的 `阻` 才能转为 `定` 并开始源码施工：

1. 固定一个新的 City `develop` SHA，且不得在 AtomPix 内漂移跟踪分支。
2. City 官方 Release/Public API 门通过；`IUiCommandFactory` 已进入合法冻结基线或由 City 正式替代合同取代。
3. City 采用模块的测试、打包与漏洞审计通过；AtomPix 不直接或传递引入已知高危运行时包。
4. 用 AtomPix 的 Avalonia `12.1.1` 和 .NET `net10.0` 真实项目引用完成还原、构建、Headless 测试与裁剪发布。
5. 更新本文的 City SHA、证据和矩阵状态后，再按专项文档顺序施工。

## 6. 专项文档

- [Desktop 总体迁移](with-city.md)
- [MVVM、命令与生命周期](with-city-mvvm.md)
- [Workflows State](with-city-workflows.md)
- [Core 边界审计](with-city-core.md)
- [Imaging 边界审计](with-city-imaging.md)
- [Infrastructure 边界审计](with-city-infrastructure.md)
- [工程、CI 与验收](with-city-engineering.md)
