# AtomPix × City Build、Generators 与 Localization 接线设计

> 设计状态：Design Frozen
>
> 交付状态：Verified；Build/Generators、生产文案迁移与发布闭环均已完成

## 1. 同 SHA 双通道消费

```text
固定 City gitlink SHA
├── 运行时：ProjectReference
│   ├── Desktop -> Core/Mvvm/Routing/Presentation/Localization
│   └── Workflows -> State
└── 构建期：由同一 SHA 生成 AtomUI.City.Build nupkg
    ├── build / buildTransitive props/targets
    ├── Build.Tasks
    └── Generators / analyzers
```

生产项目不得直接 `ProjectReference` `AtomUI.City.Build.Tasks` 或 `AtomUI.City.Generators`，也不得手工导入 City 仓库内部 targets。Desktop 使用正式 Build 包边界：

```xml
<PackageReference Include="AtomUI.City.Build"
                  PrivateAssets="all"
                  IncludeAssets="build;buildTransitive;analyzers" />
```

实际版本由固定 gitlink SHA 本地打包产生，必须包含不可复用的提交身份。打包前运行 City 完整 Release gate；包进入 AtomPix 隔离本地 feed 后才能 restore/build。CI 校验包记录的 source SHA 等于 runtime ProjectReference 指向的 submodule SHA，并拒绝相同包版本下的旧缓存制品。Build.Tasks、Generators 和 Roslyn 资产不得进入发布运行时闭包。

## 2. 工程接线顺序

1. checkout 并校验 submodule 目录、精确 SHA 和 clean 状态。
2. 使用仓库固定 SDK 执行 City solution/build/package/Public API/release validation。
3. 生成带 SHA 身份的 `AtomUI.City.Build` 包，写入 AtomPix 的隔离 feed 和隔离 packages cache。
4. restore AtomPix；MSBuild 预检读取包 metadata 并与 gitlink SHA 比较。
5. runtime City 项目以明确 `ProjectReference` 参与编译；build assets 只由 PackageReference 导入。
6. clean 和 incremental build 均验证 manifest/locpack/generated source 的稳定性。

当前固定事实：runtime gitlink SHA 为 `c2e4176159b9b441af2d7ce065d5acde6a888543`；Build 包为 `AtomUI.City.Build 1.0.0-atompix.c2e4176`，SHA-256 为 `5ED32116E11204D782740DA84FA4560E674FB49BCD2358D7E445FEBA08BA9F8E`。`Directory.Build.targets` 在 Desktop build 前同时校验包 hash 与 runtime source SHA；`eng/verify.ps1` 再校验 AtomPix Release closure 中每个 City DLL 与 City Release 输出 hash 相同。

## 3. Localization catalog

AtomPix RESX 编辑输入按以下 catalog 划分，并使用同名 key prefix：

| Catalog | 范围 |
| --- | --- |
| `Common` | 跨页面动作、格式化和共享反馈 |
| `Shell` | Window、标题栏、导航与关闭 |
| `Home` | 打开文件/目录和空状态 |
| `Browser` | 画廊、加载、不可用项和集合 |
| `Tools` | Compress/Convert/Resize/Crop |
| `Batch` | 批量进度、结果和恢复 |
| `Settings` | 四个设置分区、验证和恢复 |
| `Errors` | `AtomPixErrorCode` 到用户文案 |

跨界文案进入 `Common`，不得复制同义 key。覆盖范围包括 AXAML、Window/route 标题、命令文本、Validation、Dialog、Message/Notification、错误映射、Tooltip 和 AutomationName；页面正文之外的硬编码中文同样必须迁移。

原 RESX 只是编辑/构建输入；City Build 生成 embedded locpack 和 attribute/generated source，模块初始化由生成 registrar 原子注册一次。View/route/service attribute 与手工注册只能选一个事实源，禁止双注册。

当前已建立 `Common`、`Shell`、`Home`、`Browser`、`Tools`、`Batch`、`Settings`、`Errors` 八个 `zh-CN` RESX 目录，共 407 个生成 key。City Build 已生成八个 embedded locpack、attribute 和强类型 key，`AtomPixDesktopModule.OnApplicationInitializationAsync` 通过唯一 generated registrar 注册。Shell、Home、Settings、Browser、工具面板、输出策略、批量结果、平台选择器、异常边界、Validation、Dialog、Message/Notification、错误映射和 AutomationName 均已迁入同一 projection。

## 4. Culture 与 UI bridge

当前只交付完整 `zh-CN`。`System` 解析 OS culture：只有 catalog 声明支持时采用，否则显式回退 `zh-CN`；不得用空字符串、InvariantCulture 或当前线程偶然 culture 充当产品 fallback。

AtomPix 组合层实现 `IPresentationLocalizationBridge`，把后台 culture 变更显式切到 Avalonia UI dispatcher。ViewModel 持有的 `ILocalizedText`/订阅随 activation scope 释放。同一 culture revision 的组合文案要么全部来自旧 revision，要么全部来自新 revision，不向用户提交混合语言状态。

当前 `AvaloniaPresentationLocalizationBridge` 复用 City `IUiDispatcher`，在 UI 线程统一更新当前线程/默认线程 culture 和 AtomUI `LanguageVariant`；组合根把它注册为 `IPresentationLocalizationBridge`。`AtomPixLocalizedTextProjection` 在应用模块生命周期内持有 module scope 和 407 个 City `ILocalizedText`，只在同一 culture revision 的全部 handle 就绪后原子发布只读 snapshot；`LocExtension` 订阅 projection revision，使 AXAML 文本、Tooltip 与 AutomationName 使用同一入口。Headless Avalonia 运行时测试已验证真实 DI 类型、bridge、八个 descriptor、407 keys、跨 Settings/Browser/Tools 的资源查询、Settings 按钮真实可访问名称和生命周期释放。

首次把 projection 放入同步 Host 启动路径时，测试发现 City 的 package single-flight、mutation queue 与 localized-text refresh 使用 `Task.Yield()` 捕获 Avalonia `SynchronizationContext`，会让非泵送 UI 线程同步等待 Host 时死锁。City 固定提交 `c2e41761` 改为“锁内先登记共享 completion、锁外在线程池执行”，并新增 non-pumping context 回归；Localization `97/97` 与 AtomPix 完整 UI 项目 `19/19`（非压力 `18` + virtualization 压力 `1`）通过。该问题归属 City 基础能力，不由 AtomPix 业务绕过。

硬编码中文扫描允许清单仅包括测试 fixture 和预期中文断言；生产 Desktop AXAML/C# 当前没有例外，因此门禁不需要生产 allowlist。将来若引入内部日志、诊断或注释例外，必须逐条记录理由，不能用全目录排除。

## 5. 验收

- props/targets 实际加载，Generator 以 Analyzer 方式运行。
- clean/incremental build 的 locpack、manifest 和 generated source 可重复且不漂移。
- missing/duplicate key 产生确定性失败；registrar 只注册一次。
- 后台 culture 更新安全进入 UI，同一 revision 无混合语言。
- 发布产物包含所需 locpack；Build.Tasks/Generators/Roslyn 不在运行时闭包。
- runtime ProjectReference SHA、Build package source SHA 和证据记录完全一致。
- 干净环境 restore/build、Partial Trim、single-file 通过；NativeAOT 仅在正式采用时进入强制门。

截至 2026-10-21，构建管线、八目录/407 keys registrar、UI bridge、应用级 text projection、全部 AXAML 与 C# 动态文案迁移均已通过；Desktop 生产 AXAML/C#（排除 catalog）的硬编码中文为零。`eng/verify-localization.ps1` 对固定八目录、空值、全局重复 key、AXAML 缺失 key 以及 AXAML/C# 中文回流进行确定性失败，并由 `eng/verify.ps1` 调用。Release build 为 0 warning/0 error，完整 UI 项目 `19/19`、全解决方案非压力 `410/410`、压力 `4/4` 通过。commit `2ef33a6d0f063bb62c4d7ca179f46b8158a99a80` 的 win-x64 Partial Trim single-file 已通过启动 smoke、UIA 和真实发布 GUI `9/9`；报告位于 `.artifacts/published-functional/20261021T093545336Z-16700-68239d5f/published-functional-results.json`。本专项转为 `Verified`。
