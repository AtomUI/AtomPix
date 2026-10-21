# AtomPix × AtomUI.City 逐决策登记表

> 文档角色：City 迁移的决策、交付状态、blocker 与验收证据索引
>
> 设计状态：Design Frozen
>
> 交付状态：Implementation In Progress；Release 仍被 Localization 与许可证闭环阻塞

本表只登记有意义的采用、不采用、应用保留和阻塞决定，不为没有关系的矩阵空格制造记录。设计决定与交付状态必须分开：`Blocked` 不说明采用范围，`Verified` 也必须附真实证据。

## 1. 状态规则

- Decision：`Adopt`、`Indirect`、`Keep AtomPix`、`Do Not Adopt`。
- Delivery state：`Not Started`、`In Progress`、`Blocked`、`Verified`。
- Blocker owner：`City`、`AtomPix`、`AtomUI`、组合责任或 `None`。
- Acceptance evidence 中以“目标：”开头的是尚未产生的验收合同，不得当作通过记录。
- Decision ID 永久稳定；删除决定时保留行并标明 superseded，不复用编号。

## 2. 决策登记

| Decision ID | AtomPix owner | City contract | Decision | Delivery state | Blocker owner | Implementation | Acceptance evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `APX-CITY-BND-001` | Core | 无 | Do Not Adopt | Verified | None | [Core 边界审计](with-city-core.md) | 2026-10-21：Core 源码/csproj 的 `AtomUI.City` 扫描为零命中 |
| `APX-CITY-BND-002` | Imaging.Abstractions / Imaging.Magick | 无 | Do Not Adopt | Verified | None | [Imaging 边界审计](with-city-imaging.md) | 2026-10-21：两个 Imaging 项目的源码/csproj 扫描为零命中 |
| `APX-CITY-BND-003` | Infrastructure | 无 | Do Not Adopt | Verified | None | [Infrastructure 边界审计](with-city-infrastructure.md) | 2026-10-21：Infrastructure 源码/csproj 的 `AtomUI.City` 扫描为零命中 |
| `APX-CITY-HOST-001` | Desktop composition root | Core Host / module lifecycle | Adopt | Verified | None | `DesktopCompositionRoot`、`AtomPixDesktopModule`、`DesktopBootstrap` | 2026-10-21：唯一 City Host/容器；Release build、发布 EXE 正常启动、初始 route/UIA 通过 |
| `APX-CITY-ERR-001` | Workflows + Desktop | Host rollback + State | Keep AtomPix | Verified | None | `DesktopExceptionBoundary`、`AppSettingsStateCoordinator` | `AppSettingsStateTests` 覆盖可恢复 Faulted；City startup rollback release gate 与 AtomPix Desktop 异常边界测试通过 |
| `APX-CITY-LIFE-001` | Browser workspace | Borrowed ViewModel lease / activation | Adopt | Verified | None | `ShellViewModel.AttachCompositionAsync`、`AtomPixCityNavigator` | `Settings_round_trip_rebuilds_browser_visuals_and_activation_without_losing_workspace`：同 workspace/VM、不同 View/scope |
| `APX-CITY-CMD-001` | Browser/工具命令 | `CommandExecutionDomain` | Adopt | Verified | None | City SHA `9097d9d0`；`ImageBrowserViewModel._interactionDomain` | City Release gate；AtomPix 跨 Select/Next latest-wins 取消测试通过 |
| `APX-CITY-REC-001` | Home / Workflows | 不使用 State | Do Not Adopt | Verified | None | Home/生产组合不注册、不调用 Recent workflows | 2026-10-21：生产 Host/Home/打开来源路径扫描无 Recent 读写；兼容模型和文件不删除 |
| `APX-CITY-STATE-001` | Workflows | State key/writer/Observe | Adopt | Verified | None | `AppSettingsStateCoordinator`、`IAtomPixApplicationState` | 4 个 State 专项测试及 Workflows 158 tests：Store-first/State-second、失败不发布、并发 version 顺序、Faulted |
| `APX-CITY-NAV-001` | City + `AtomPixCityNavigator` | coordinator/receipt/conditional restore | Adopt | Verified | None | City SHA `9097d9d0`；`PreparedRoutePlanFactory` | City coordinator 故障注入 Release gate；AtomPix Home/Browser/Settings route 与发布 UIA 通过 |
| `APX-CITY-MVVM-001` | City composition dependency | ViewModel composition | Adopt | Verified | None | City SHA `9097d9d0` | City 真实 Presentation composition/close/stop Release validation、solution/package/Public API gate 全通过 |
| `APX-CITY-MVVM-002` | Desktop ViewModels | ViewModelBase/activation/composition | Adopt | In Progress | AtomPix | 已删除本地 `ObservableObject`/Commands；Shell/编辑器接入 WindowSession composition | 现有激活/重激活/命令/UI 测试通过；尚需按 ownership map 收口 Home/Settings EntryOwned 与旧导航/前台任务职责拆分 |
| `APX-CITY-BUILD-001` | Repository engineering | `AtomUI.City.Build` package | Adopt | Verified | None | runtime SHA `9097d9d0` + Build 包 `1.0.0-atompix.9097d9d` | 包 SHA-256 `97087E41…F66`；MSBuild provenance、Release City DLL 哈希闭包、clean/incremental build 通过 |
| `APX-CITY-LOC-001` | Desktop localization | Localization + Build generated registrar | Adopt | In Progress | AtomPix | 8 个 `zh-CN` RESX catalog、generated registrar、City `IUiDispatcher` presentation bridge、Headless 运行时资源查询已通过 | 尚需生产文案/key/订阅迁移、硬编码扫描 allowlist、missing/duplicate key 与最新 trim/single-file locpack 验收 |
| `APX-CITY-TEST-001` | Windows publish verification | 不采用 City.Testing | Keep AtomPix | Verified | None | fixture manifest、隔离 appdata/temp、JSON/trace、CI/Release artifact | 2026-10-21：最新 TrimmedSingleFile 全新 run root 发布 GUI `9/9`；报告 `.artifacts/published-functional/20261021T083530097Z-19232-018467b1/` |
| `APX-CITY-LIC-001` | Release engineering | 无运行时框架能力 | Keep AtomPix | Blocked | AtomPix + AtomUI | [发布合规](../../implementation/release-compliance.md) | 目标：真实 publish closure、替代授权、Notice/源码/SBOM gate 通过 |
| `APX-CITY-GOV-001` | Documentation | 无 | Keep AtomPix | Verified | None | 本表、[主矩阵](with-city-migration-matrix.md)、[一致性规则](../../document-consistency.md) | 2026-10-21：相对链接、术语/状态、17 个唯一 Decision ID、边界扫描和当前 Release 基线检查通过 |

## 3. City blocker 的完成证据

City blocker 只能在新的固定 SHA 上关闭，并同时记录：City commit、相关测试类/报告、solution + package + Public API release gate 结果、AtomPix 真实 ProjectReference/Build package 消费结果。仅有 API 文件、局部单元测试或口头说明不能把 `Blocked` 改为 `Verified`。

## 4. 维护门禁

每次修改本表必须检查：

1. Decision ID 唯一，专项文档引用存在。
2. `Verified` 不含“目标：”占位，必须链接实际命令/报告/测试。
3. `Blocked` 具有 blocker owner 和可判定的解除条件。
4. 高层矩阵与本表状态不矛盾。
5. 文档状态变化写入 `document-consistency.md` 的追加式快照，不覆盖历史证据。
