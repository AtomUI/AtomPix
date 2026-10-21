# AtomPix × City Build、Generators 与 Localization 接线设计

> 设计状态：Design Frozen
>
> 交付状态：Implementation In Progress；Build/Generators 已 Verified，Localization 基础接线已验证，生产文案迁移与发布闭环未完成

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

当前固定事实：runtime gitlink SHA 为 `9097d9d0d8244a864e5b7944ef2bf7ab2be70ae3`；Build 包为 `AtomUI.City.Build 1.0.0-atompix.9097d9d`，SHA-256 为 `97087E411C1FFDB7B264A45D057333BF67EC031FD409B885D2DBA6753FD2FB66`。`Directory.Build.targets` 在 Desktop build 前同时校验包 hash 与 runtime source SHA；`eng/verify.ps1` 再校验 AtomPix Release closure 中每个 City DLL 与 City Release 输出 hash 相同。

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

当前已建立 `Common`、`Shell`、`Home`、`Browser`、`Tools`、`Batch`、`Settings`、`Errors` 八个 `zh-CN` RESX 目录，每个目录先放置一个可验证的种子 key。City Build 已生成八个 embedded locpack、attribute 和强类型 key，`AtomPixDesktopModule.OnApplicationInitialization` 通过唯一 generated registrar 注册。这个事实只证明 catalog 管线已经接通，不代表现有生产中文已经迁完。

## 4. Culture 与 UI bridge

当前只交付完整 `zh-CN`。`System` 解析 OS culture：只有 catalog 声明支持时采用，否则显式回退 `zh-CN`；不得用空字符串、InvariantCulture 或当前线程偶然 culture 充当产品 fallback。

AtomPix 组合层实现 `IPresentationLocalizationBridge`，把后台 culture 变更显式切到 Avalonia UI dispatcher。ViewModel 持有的 `ILocalizedText`/订阅随 activation scope 释放。同一 culture revision 的组合文案要么全部来自旧 revision，要么全部来自新 revision，不向用户提交混合语言状态。

当前 `AvaloniaPresentationLocalizationBridge` 复用 City `IUiDispatcher`，在 UI 线程统一更新当前线程/默认线程 culture 和 AtomUI `LanguageVariant`；组合根把它注册为 `IPresentationLocalizationBridge`。Headless Avalonia 运行时测试已验证真实 DI 类型、bridge 应用结果、generated registrar 的八个 descriptor 以及 `Settings.Title -> 设置` 查询。production View/route/service 尚未改用生成 key 或 `ILocalizedText`，因此 revision 一致性与 activation-scope 释放仍待后续内容迁移验证。

硬编码中文扫描允许清单仅包括测试 fixture、预期中文断言、内部日志/诊断和注释；每条生产例外必须有理由，不能用全目录排除。

## 5. 验收

- props/targets 实际加载，Generator 以 Analyzer 方式运行。
- clean/incremental build 的 locpack、manifest 和 generated source 可重复且不漂移。
- missing/duplicate key 产生确定性失败；registrar 只注册一次。
- 后台 culture 更新安全进入 UI，同一 revision 无混合语言。
- 发布产物包含所需 locpack；Build.Tasks/Generators/Roslyn 不在运行时闭包。
- runtime ProjectReference SHA、Build package source SHA 和证据记录完全一致。
- 干净环境 restore/build、Partial Trim、single-file 通过；NativeAOT 仅在正式采用时进入强制门。

截至 2026-10-21，前两项构建管线、八目录 registrar 和 UI bridge 的最小运行时链路已经通过；最新 win-x64 Partial Trim single-file 也已完成启动 UIA 与发布 GUI `9/9` 回归。由于生产 UI 尚未消费生成 key，这次发布验证不能代替裁剪后 locpack 真实查询；missing/duplicate key、生产硬编码清零/逐项 allowlist、动态文本订阅生命周期和发布物资源查询探针仍是开放项。本专项保持 `Implementation In Progress`。
