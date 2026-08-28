# AtomPix 测试与发布策略

> 文档状态：历史验证记录 + 正式目标测试基线
>
> 基线时间：2026-06-26
>
> 基线范围：当前文档定义 UI 前置实现阶段的 headless 测试策略、测试轮次、测试项目规划和第一阶段发布验证策略
>
> 变更规则：调整测试分层、测试轮次、发布验证口径时，应先更新本文档。

> 时间语义：第 1–31 节包含 2026-06-26 首轮 Headless 策略和逐轮通过记录；第 32–46 节是后来冻结的目标契约；第 47–50 节记录 2026-08-07 Headless、Desktop 首轮、第一阶段贯通和交互闭环快照。历史记录中出现的内嵌 Resize、`ResizeApplied`、旧 `Saved*` 或 Magick 建目录行为不构成现行规范；冲突时以最新快照及对应模块正式设计为准。

## 1. 总原则

AtomPix 第一阶段采用底层实现先于 Desktop 页面代码的策略。Desktop 的功能交互与状态契约已经建立，迁移前页面也已具备功能基线；新视觉结构已经冻结在 `docs/ui-design/README.md`，生产页面尚待按该基线迁移，且仍应在所依赖的底层目标契约可验证后落地。

这里的 UI 最后不是指不重视 UI，而是指：

```text
所有底层模块充分实现并通过 headless 测试后，再展开 Desktop / UI 层的规划、实现和测试。
```

原因：

- UI 层的视觉设计和交互取舍强依赖用户确认。
- UI 层测试也强依赖用户对真实界面体验的判断。
- Core、Workflows、Infrastructure、Imaging.Abstractions、Imaging.Magick 都可以在没有 UI 的情况下实现和测试。
- 先把底层能力测试扎实，可以避免 UI 实现时被底层不稳定反复打断。

本文档中的 headless 测试指：

```text
不启动 Avalonia，不依赖 AtomUI，不创建窗口，不使用 ViewModel 作为测试入口。
```

## 2. 测试三轮模型

底层模块测试分三轮：

```text
Round 1: 单元测试
Round 2: 契约测试
Round 3: Headless 业务场景测试
```

三轮测试不是互相替代关系，而是逐层增强：

- Round 1 保证最小单元正确。
- Round 2 保证模块契约和洋葱依赖边界正确。
- Round 3 在没有 UI 的情况下模拟用户业务动作，使用真实数据验证完整业务流程。

## 3. Round 1: 单元测试

Round 1 是粒度最小的测试。

目标：

```text
验证单个模型、值对象、策略、状态流转、错误结果和纯函数规则。
```

特点：

- 不访问真实文件系统。
- 不调用 Magick.NET。
- 不依赖 Avalonia / AtomUI。
- 不启动 DI 容器。
- 不测试跨模块组合。

建议测试项目：

```text
tests/AtomPix.Core.Tests/
```

覆盖范围：

```text
OperationResult / AtomPixError
CompressionProfile 默认值和校验
ConversionProfile 默认值和校验
OutputPolicy 默认值和约束
ImageJob / BatchJob 状态规则
AppSettings 默认值
LocalPath 基础校验
```

Round 1 的失败通常说明模型或规则本身有问题，应优先修正 Core 设计或实现。

## 4. Round 2: 契约测试

Round 2 是围绕模块契约和洋葱边界展开的测试。

目标：

```text
验证模块对外承诺是否成立，以及外层实现是否正确满足内层契约。
```

契约测试应按洋葱模型由内向外逐层推进：

```text
Core 契约
  -> Imaging.Abstractions 契约 -> Imaging.Magick 契约
  -> Infrastructure 契约
  -> Workflows 契约（组合 Core 与 Imaging.Abstractions，并通过 Core 端口使用外部能力）
```

这里的“由内向外”不是说外层测试不再测试内层。相反，外层契约测试天然会再次经过内层契约。

例如：

```text
测试 Infrastructure 的 IAppSettingsStore 实现
  必须使用 Core 的 AppSettings / OperationResult / AtomPixError
  因此隐含再次验证 Core 契约在真实外层实现中的可用性
```

### 4.1 Core 契约测试

验证 Core 公共模型和端口签名是否稳定、语义是否清楚。

重点：

- 公共模型不依赖外层类型。
- 端口方法只使用 Core 类型和 .NET BCL 类型。
- 错误码、状态枚举、策略对象表达清晰。

### 4.2 Infrastructure 契约测试

建议测试项目：

```text
tests/AtomPix.Infrastructure.Tests/
```

使用真实本地文件系统，但必须写入测试临时目录。

覆盖：

```text
JsonAppSettingsStore
JsonRecentItemsStore
LocalFileSystemService
AppPathProvider
```

行为：

```text
settings.json 不存在 -> 默认设置
settings.json 损坏 -> Failure
recent-items.json 不存在 -> 空列表成功
recent-items.json 损坏 -> 空列表成功
CreateDirectoryAsync
GetFileSizeAsync
BuildIndexedPath
```

约束：

- 测试不得写用户真实 AppData。
- 测试必须使用临时目录。
- 测试结束清理临时目录。
- Infrastructure 不得执行 OverwritePolicy 决策。

### 4.3 Imaging.Magick 契约测试

建议测试项目：

```text
tests/AtomPix.Imaging.Magick.Tests/
```

使用小型样本图片测试真实 Magick.NET 行为。

样本目录建议：

```text
tests/TestAssets/Images/
  jpeg-basic.jpg
  png-alpha.png
  webp-basic.webp
  bmp-basic.bmp
  gif-animated.gif
  tiff-basic.tiff
```

样本图片要求：

- 小尺寸，例如 100x100 或 256x256。
- 不使用大体积图片入仓库。
- 每个样本只服务明确测试目标。

覆盖：

```text
Probe JPEG / PNG / WebP / BMP / GIF / TIFF
PNG alpha channel -> HasAlphaChannel = true
opaque RGBA PNG -> HasTransparency = false
transparent PNG -> HasTransparency = true
ICC sample -> HasColorProfile = true, independently of HasMetadata
GIF animated -> IsAnimated = true
Preview JPEG -> JPEG bytes
Preview PNG alpha -> PNG bytes
Compress JPEG Balanced
Compress WebP Balanced
Compress PNG Smart
Convert PNG -> WebP
Convert WebP -> JPEG
Unsupported multi-frame compress/convert
Error mapping for missing file
```

### 4.4 Workflows 契约测试

建议测试项目：

```text
tests/AtomPix.Workflows.Tests/
```

使用 fake / test double 验证流程编排，不依赖真实 Magick.NET。

建议测试替身：

```text
FakeImageProcessor
FakeFileSystemService
FakeAppSettingsStore
FakeRecentItemsStore
```

覆盖：

```text
OpenImageWorkflow 调用 Probe
CreatePreviewWorkflow 调用 CreatePreview
输出路径策略：Subfolder / suffix / AutoRename
OverwritePolicy.Skip -> ImageJobStatus.Skipped
OverwritePolicy.Overwrite -> 使用目标路径
OverwritePolicy.AutoRename -> 生成 _1 / _2
批量部分成功
批量取消
设置不存在 -> 默认设置
设置损坏 -> Failure
```

## 5. Round 3: Headless 业务场景测试

Round 3 在没有 UI 的情况下模拟用户点击行为产生的业务逻辑，使用真实数据进行真实测试。

目标：

```text
验证用户视角的完整业务链路，不依赖 Desktop UI。
```

特点：

- 不启动 Avalonia。
- 不创建窗口。
- 不测试 ViewModel。
- 使用真实 Infrastructure。
- 使用真实 Imaging.Magick。
- 使用测试样本图片和临时输出目录。

典型场景：

```text
用户选择一张 PNG，点击转换为 WebP
用户选择一张 JPEG，点击平衡压缩
用户选择多张图片，点击批量压缩
用户选择多张图片，点击批量转换
用户设置输出到 AtomPix_Output，目标文件存在时自动重命名
用户选择 Skip，目标文件存在时任务结果为 Skipped
设置文件不存在时启动，加载默认设置
设置文件损坏时启动，返回明确失败
```

Round 3 可以先放在：

```text
tests/AtomPix.Workflows.Tests/
```

后续如测试规模扩大，再拆出：

```text
tests/AtomPix.Integration.Tests/
```

## 6. 测试项目规划

第一阶段建议创建：

```text
tests/
  AtomPix.Core.Tests/
  AtomPix.Workflows.Tests/
  AtomPix.Imaging.Magick.Tests/
  AtomPix.Infrastructure.Tests/
```

暂不创建：

```text
AtomPix.Desktop.Tests
```

Desktop / UI 测试在 UI 设计和实现启动后再单独规划。

## 7. 必须覆盖的业务风险

优先覆盖：

```text
不会默认覆盖源文件
AutoRename 正确
Skip 不算失败
批量部分成功
用户取消语义正确
设置损坏不偷偷覆盖
最近记录损坏不阻塞主流程
图片库异常不穿透
多帧压缩/转换明确拒绝
OutputPolicy 决策留在 Workflows
Infrastructure 不承载覆盖/跳过/自动重命名策略
```

## 8. 第一阶段发布验证

第一阶段不强制 NativeAOT。

基础验证：

```text
dotnet build
dotnet test
dotnet publish -c Release -r win-x64 --self-contained true
```

后续补充：

```text
linux-x64
osx-arm64
```

NativeAOT 暂时作为实验验证，不作为第一阶段阻塞项：

```text
dotnet publish -c Release -r win-x64 /p:PublishAot=true
```

如果 NativeAOT 发布失败，应记录原因和阻塞点，不阻塞 MVP。

## 9. CI 策略预留

后续接入 CI 时，基础矩阵：

```text
Windows: build + test
Linux: build + test
macOS: build + test
```

Magick.NET 跨平台测试需要关注原生依赖包和平台差异。第一阶段可以先本地验证 Windows，再逐步扩展。
## 10. 当前硬化验收基线

当前 UI 前置阶段已经具备以下 headless 验收基线：

- Core 覆盖结果模型、错误模型、压缩/转换/输出策略、任务状态、设置 schema 和路径值对象。
- Imaging.Abstractions 覆盖图片处理请求、结果和能力声明的不变量。
- Infrastructure 覆盖 JSON 存储、最近记录、原子写入、取消语义和文件系统路径辅助。
- Imaging.Magick 覆盖真实图片探测、预览、压缩、转换、多帧拒绝、取消和非法输出格式。
- Workflows 覆盖输出路径策略、覆盖策略、批量部分成功、真实 headless 用户场景和设置流程。

发布验证口径：

- 当前没有 Desktop 可执行项目，因此 NativeAOT 不能作为真实桌面产物验证。
- 在 Desktop 项目出现前，只验证类库构建、测试和可发布性。
- Desktop 项目出现后，再执行 self-contained / single-file / NativeAOT 真实产物验证。
## 11. 当前发布验证记录

本轮验证命令：

```text
dotnet build AtomPix.slnx --no-restore
dotnet test AtomPix.slnx --no-build --no-restore
dotnet publish src/AtomPix.Workflows/AtomPix.Workflows.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Workflows/win-x64
dotnet publish src/AtomPix.Imaging.Magick/AtomPix.Imaging.Magick.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Imaging.Magick/win-x64
```

验证结论：

- 全量构建通过。
- 全量测试通过。
- `AtomPix.Workflows` 类库发布通过。
- `AtomPix.Imaging.Magick` 类库发布通过。
- 当前没有 Desktop 或其他可执行入口项目，因此不能验证真实 single-file 桌面产物，也不能对真实应用执行 NativeAOT 发布。

后续出现 `AtomPix.Desktop` 后，发布验证必须补充：

```text
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

NativeAOT 仍作为实验项：

```text
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 /p:PublishAot=true
```

如果 Avalonia、AtomUI、Magick.NET 或原生依赖导致 NativeAOT 失败，应记录具体错误和替代发布策略，不阻塞 MVP。
## 12. Headless Round 3 增量验收

Headless 业务场景当前新增覆盖：

- 打开图片后通过真实 `JsonRecentItemsStore` 写入最近记录。
- 批量转换结果投影为 `BatchProgressSnapshot`。
- 真实转换场景覆盖 Skip、Overwrite、AutoRename。
- Magick 层覆盖复杂 JPEG 的体积下降和尺寸保持。

后续 Round 3 仍需继续补充：损坏图片、权限失败、路径特殊字符、大尺寸图片和跨平台路径差异。
## 13. 默认设置与 Resize Headless 验收

Headless Round 3 当前新增覆盖：

- 使用真实 `JsonAppSettingsStore` 保存默认设置。
- `ConvertWithDefaultSettingsWorkflow` 使用默认转换配置完成 PNG -> WebP。
- 当时代码中的 `CompressWithDefaultSettingsWorkflow` 使用保存的默认压缩配置完成内嵌 resize；这是历史验收事实，目标实现必须迁移为独立 `ResizeImageWorkflow`。
- Workflows 在调用图片处理前执行输入探测、动画/多帧拒绝和输出格式能力校验。

这些测试保证设置存储不只是孤立读写能力，而是真正参与图片处理流程。
## 14. 批量默认设置与混合输入验收

Headless Round 3 当前新增覆盖：

- `BatchCompressWithDefaultSettingsWorkflow` 使用真实 `JsonAppSettingsStore` 执行批量压缩。
- `BatchConvertWithDefaultSettingsWorkflow` 使用真实 `JsonAppSettingsStore` 执行批量转换。
- 批量输入混入正常图片、缺失文件和动画 GIF。
- 验证单项失败不中断批次，批次状态为 `PartiallySucceeded`。
- 验证批量返回的 `FinalProgress` 已完成且计数正确。
## 15. 处理结果细节验收

当前测试新增覆盖：

- Abstractions 验证当时 `ImageProcessingDetails` 的宽高不变量。
- 当时 Magick 压缩结果返回真实输入/输出尺寸、resize 标记、metadata 移除标记和有损输出标记；目标契约移除 `ResizeApplied`，压缩/转换尺寸必须保持不变。
- Magick 转换结果返回真实输入/输出尺寸和输出格式有损标记。
- Headless 默认压缩场景验证任务大小统计与真实输出文件一致，并验证 resize 后真实图片尺寸。
## 16. 错误语义增量验收

本轮新增错误语义测试覆盖：

- Core 增加 `InvalidImageFile`，用于表达文件存在但不是有效图片或图片损坏。
- Magick 契约测试覆盖非图片文件 Probe、损坏图片 Preview、损坏图片直接 Compress/Convert 的错误映射。
- Workflows 契约测试覆盖预检失败原样透传，且不会进入实际压缩/转换。
- Workflows 批量测试覆盖单项预检失败记录为失败项并继续处理其他图片。
- Headless 场景测试使用真实 Magick 和真实文件系统验证批量转换混入无效图片时返回 `PartiallySucceeded`。
- Infrastructure 契约测试覆盖缺失文件大小读取返回 `InputFileNotFound`。

## 17. 输出写入安全增量验收

本轮新增覆盖：

- Magick 压缩成功后不残留临时输出文件。
- Magick 转换成功后不残留临时输出文件。
- Magick 写入最终路径失败时清理临时输出文件。
- Workflows 在压缩处理失败时返回失败任务结果，并保留输入大小、目标输出路径和错误码。
- Workflows 在转换处理失败时返回失败任务结果，并保留输入大小、目标输出路径和错误码。

输出路径策略仍由 Workflows 决策；图片内容写入安全由 Imaging.Magick 负责。

## 18. 路径与跨平台边界增量验收

本轮新增覆盖：

- Core 验证 `LocalPath` 保留原始路径文本。
- Infrastructure 验证 `Combine` 拒绝 `/`、`\`、`.`、`..` 等非单段文件名。
- Infrastructure 验证无扩展名、多点文件名的索引路径生成。
- Infrastructure 验证多点文件名的扩展名替换。
- Infrastructure 验证 `AppPathProvider` 注入路径和默认路径尾部目录名。
- Workflows 验证 `SameAsInput`、`CustomDirectory`、多点文件名 `AutoRename` 和无扩展名压缩失败。
- 当时 Magick 验证只有文件名的输出路径可写出且会创建缺失输出目录；目标边界已经改为 Workflow 通过文件系统端口在 Job 前准备目录，Magick 不补建业务目录。

## 19. 设置与最近记录存储增量验收

本轮新增覆盖：

- `settings.json` 高于当前 schema version 时返回 `SettingsLoadFailed`。
- 损坏 `settings.json` 加载失败后不会被默认设置覆盖。
- 设置保存失败时清理同目录临时文件。
- 损坏 `recent-items.json` 读取为空列表成功，后续保存可恢复为正常文件。
- 默认设置驱动的压缩/批量转换流程在设置加载失败时不会进入图片处理。

## 20. 取消、统计与 DI 装配增量验收

本轮新增覆盖：

- Core 验证取消任务必须携带取消错误。
- Core 验证 `BatchResult.TotalCount` 可大于 `CompletedCount`，用于批量中途取消。
- Core 验证批量进度在取消后可显示未完成状态。
- Core 验证批量文件体积统计；当前代码仍是旧 Saved 口径，目标中性口径见第 44 节。
- Infrastructure 验证保存预取消不会留下临时 JSON 文件。
- Workflows 验证图片处理器返回取消时单张任务为 `Canceled`。
- Workflows 验证批量中途取消时保留已完成项、记录当前取消项、停止后续未开始项。
- Workflows 验证真实 DI 容器可以装配 Infrastructure、Magick 和 Workflows，并完成一条无 UI 的默认设置转换流程。

当前仍不测试 Desktop、Avalonia、AtomUI 或 ViewModel。UI 层继续放到所有底层 headless 语义稳定之后。

## 21. Headless 质量闸门与发布验证记录

本轮新增文档：

```text
docs/implementation/headless-quality-gate.md
```

工程卫生检查结论：

- Core 没有外层依赖。
- Imaging.Abstractions 只依赖 Core。
- Infrastructure 只依赖 Core。
- Imaging.Magick 只依赖 Core 和 Imaging.Abstractions。
- Workflows 只依赖 Core 和 Imaging.Abstractions。
- 当前没有 Desktop 项目，没有 UI 类型进入底层模块。
- Magick.NET 只存在于 Imaging.Magick 和相关测试中。
- DI 完整容器只在 headless 测试中使用，模块自身只依赖 DI Abstractions。

本轮发布验证命令：

```text
dotnet publish src/AtomPix.Workflows/AtomPix.Workflows.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Workflows/win-x64
dotnet publish src/AtomPix.Imaging.Magick/AtomPix.Imaging.Magick.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Imaging.Magick/win-x64
```

验证结论：

- `AtomPix.Workflows` Release self-contained publish 通过。
- `AtomPix.Imaging.Magick` Release self-contained publish 通过。
- 当前仍没有 `AtomPix.Desktop` 可执行项目，因此不能验证真实桌面 single-file、安装包或 NativeAOT 产物。

进入 Desktop / UI 实现阶段的前置结论：

```text
底层/headless 已具备进入 Desktop / UI 实现阶段的基础条件；正式发布前仍需继续补真实跨平台、权限、大图和包体验证。
```

## 22. Round 1 单元测试基线记录

本轮新增文档：

```text
docs/implementation/headless-test-rounds.md
```

Round 1 口径：只验证最小单元、模型不变量、纯规则、构造边界和轻量 DI 注册边界；不把真实文件系统、真实 Magick.NET 或真实跨模块链路计入 Round 1。

本轮新增/强化：

- Core 策略对象拒绝非法枚举值。
- `AtomPixError` details 防御性拷贝测试。
- `ImagePreviewResult.EncodedBytes` 返回防御性副本。
- Imaging.Abstractions capabilities null 集合测试。
- Infrastructure / Imaging.Magick / Workflows DI 注册扩展 null 参数测试。
- Magick 第一阶段能力声明单元测试。

本轮模块验证结果：

```text
Core: 44
Imaging.Abstractions: 13
Infrastructure: 28
Imaging.Magick: 33
Workflows: 55
```

Round 1 结论：通过。下一步进入 Round 2 契约测试审计与补缺口。
## 23. Round 2 契约测试补强记录

本轮新增契约测试覆盖：

- Core、Imaging.Abstractions、Workflows 增加依赖边界测试，防止底层模块越界引用 UI、Infrastructure 实现或 Magick.NET 实现。
- Infrastructure 增加存储 Save null payload、文件系统取消、扩展名和 indexed path 边界测试。
- Imaging.Magick 增加 Preview / Compress / Convert 缺失文件映射，以及四类图片处理操作的取消映射测试。
- Workflows 增加 Probe / Preview / Settings / RecentItems 失败透传测试，确保流程层不吞错、不在前置失败后继续处理图片。

本轮目标测试结果：

```text
Core: 45 passed
Imaging.Abstractions: 14 passed
Infrastructure: 32 passed
Imaging.Magick: 36 passed
Workflows: 62 passed
Total: 189 passed
```

当前仍不测试 Desktop、Avalonia、AtomUI 或 ViewModel。
## 24. Round 2 公共 API 契约审计记录

本轮新增 Core 与 Imaging.Abstractions 的公共 API 契约测试：

- Core public type surface 白名单。
- Core public member 暴露类型扫描，防止外层模块、UI 库或具体图片实现进入最内层契约。
- Imaging.Abstractions public type surface 白名单。
- `IImageProcessor` 四个异步操作签名白名单。
- `ImageFormatKind` 枚举成员顺序白名单。

目标测试结果：

```text
Core: 47 passed
Imaging.Abstractions: 17 passed
```
## 25. Round 2 Infrastructure 真实文件系统契约硬化记录

本轮新增 Infrastructure 契约测试：

- JSON 存储输出可解析，并固定当前顶层 schema 形状。
- 文件系统路径辅助覆盖空白、绝对路径、跨平台分隔符、遍历段、无扩展名和多点文件名。
- `AppPathProvider` 注入路径不隐式创建目录。
- settings/recent-items 在保存失败时保留旧文件并清理临时文件。
- `FileExists` / `DirectoryExists` 稳定返回存在性。

目标测试结果：

```text
Infrastructure: 35 passed
```
## 26. Round 2 Imaging.Magick 真实图片契约硬化记录

本轮新增 Imaging.Magick 契约测试：

- `Capabilities` 声明的输入/输出格式与真实 Probe/Convert 行为一致。
- animated GIF 转换拒绝，不产生输出文件。
- JPEG 质量档位对输出大小产生可观察影响。
- 当时的 Metadata Remove / Preserve 测试只使用真实 EXIF 样本验证是否删除或保留 EXIF，尚未验证 ICC 始终保留及 AutoOrient 后方向规范化；后两项属于后续目标契约。
- 当时的 PNG/WebP/JPEG 测试只固定“支持格式保留 Alpha、不支持格式移除 Alpha”，尚未规定或验证透明像素的铺底颜色；确定性背景色属于后续目标契约。
- 已存在输出文件可覆盖；转换写入失败会清理临时输出文件。

目标测试结果：

```text
Imaging.Magick: 45 passed
```
## 27. Round 2 Workflows 输出策略与批量矩阵硬化记录

本轮新增 Core / Workflows 契约测试：

- 输出策略组合覆盖 AutoRename 连续递增、CustomDirectory、Subfolder、KeepOriginalName、AppendSuffix、Skip。
- 批量压缩/转换混合成功、失败、跳过、取消时，`BatchResult` 与 `FinalProgress` 统计一致。

目标测试结果：

```text
Core: 55 passed
Workflows: 71 passed
```
## 28. Round 3 第一阶段真实用户主路径记录

本轮新增 Headless 用户场景测试：

- 单张压缩/转换真实写出结果。
- 批量转换多图真实写出 WebP，并用 Magick 验证格式。
- `SameAsInput` 和 `CustomDirectory` 输出策略在真实文件系统中落盘。
- Headless 动态样本补充 animated GIF 和非空 alpha PNG。

目标测试结果：

```text
Workflows: 76 passed
```
## 29. Round 3 第二阶段真实异常与恢复场景记录

本轮新增 Headless 异常恢复场景测试：

- 损坏 settings 阻断默认设置流程，不覆盖损坏文件；修复后恢复。
- 损坏 recent-items 可在用户打开图片后恢复写入，并验证真实去重、排序、截断。
- 输出目标为目录时，转换任务失败且清理临时文件。
- AutoRename 多次真实冲突后选择下一个可用路径。

目标测试结果：

```text
Workflows: 82 passed
```
## 30. Round 3 第三阶段可视化输出产物记录

本轮新增可视化输出产物测试：

- 固定输入样本目录：`tests/TestAssets/Images/`。
- 固定输出产物目录：`tests/TestOutputs/Images/`。
- 生成并保留压缩、resize、PNG alpha 转 WebP/JPEG、WebP 转 JPEG、JPEG 转 PNG 等结果图。
- 输出目录的 `README.md` 记录文件来源和处理参数。

目标测试结果：

```text
Workflows: 83 passed
```
## 31. Round 3 第四阶段 Headless 验收收口与发布验证记录

本轮新增真实 DI 组合验收：

- 默认转换。
- 默认压缩。
- 批量压缩。
- 批量转换。
- 最近记录写入。

验证命令：

```text
dotnet build AtomPix.slnx --no-restore /p:UseSharedCompilation=false
dotnet test AtomPix.slnx --no-build --no-restore
dotnet publish src/AtomPix.Workflows/AtomPix.Workflows.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Workflows/win-x64 /p:UseSharedCompilation=false
dotnet publish src/AtomPix.Imaging.Magick/AtomPix.Imaging.Magick.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Imaging.Magick/win-x64 /p:UseSharedCompilation=false
```

验证结果：

```text
Core: 55 passed
Imaging.Abstractions: 17 passed
Infrastructure: 35 passed
Imaging.Magick: 45 passed
Workflows: 85 passed
Total: 237 passed
Build: passed, 0 warning, 0 error
Publish Workflows: passed
Publish Imaging.Magick: passed
```

注意：当前仍没有 Desktop 可执行项目，因此不能验证真实桌面 single-file、安装包或 NativeAOT 应用产物。

## 32. Desktop / UI 状态测试规划基线

本节制定时仓库尚无 Desktop 项目，因此以下条目最初作为后续测试基线，不计入第 31 节历史 237 条基线。Desktop 现已创建并按该基线持续补测，当前数量见第 50 节；逐页面状态与控件规则见 [Desktop 交互状态设计](../modules/desktop/interaction-state-design.md)，AtomUI 包、组件与自定义控件边界见 [AtomUI 组件映射与实现基线](../modules/desktop/atomui-component-mapping.md)。

建议新增独立的 `AtomPix.Desktop.Tests`，优先以不启动窗口的 ViewModel 单元测试覆盖：

- 首页打开图片、打开文件夹的 Loading、失败恢复与重复触发保护；当前版本不提供最近记录 UI。
- 以假的 Desktop Picker/Launcher 服务覆盖单选、多选、目录选择、用户取消、平台不可用和系统调用失败；ViewModel 测试不得依赖真实 `Window`、`TopLevel` 或系统对话框。
- 选择器成功结果进入对应 Workflow；取消保持页面和草稿不变且不记为失败；当前版本不展示处理完成后的输出目录入口。
- 图片浏览器集合加载、当前项切换、缺失项目保留，以及四项快捷入口只捕获当前图片。
- 四类右侧处理面板的 Empty、草稿校验、任务运行锁定、取消和同面板终态回到草稿；Crop 还需覆盖主图裁剪模式。
- 压缩与转换处理面板只显示原图预览；参数变化不启动处理后效果预览，正式任务开始前不显示预计文件体积。
- 第一阶段不显示“保存为预设”入口，也不要求命名预设的 Core、Workflow 或存储契约。
- 浏览走廊多次追加、全走廊批量范围、运行快照不可变、取消确认、Partial 汇总、重试失败项和处理未完成项。
- **TODO（后期迭代，当前不执行）**：批量画廊自动跟随覆盖 20 项/可见 6 项基准：前 6 项 Running 不滚动，第 7 项 Running 时只平滑左移一个槽位并完整露出；`CurrentItem`、主图 Source 和解码代次均不改变。
- **TODO（后期迭代，当前不执行）**：批量跟随覆盖活动项已可见、左右部分遮挡、跨多个槽位、窗口/DPI/左右列状态改变、虚拟化目标未实现、用户滚动后 `1200 ms` 暂停、Reduced Motion 立即定位，以及终态保持最后偏移。
- **TODO（后期迭代，当前不执行）**：快速连续和乱序进度覆盖动画取消/改投与同帧合并：只跟随当前调用中最新合法 Running Sequence，不积压逐项动画；Skipped/Failed/Succeeded/Canceled 与最终 BatchResult 校正不额外触发滚动。
- **TODO（后期迭代，当前不执行）**：缩略图状态 Presenter 覆盖 `null/Pending/Running/Succeeded/Failed/Skipped/Canceled` 七种快照，校验右上角 `20 DIP` 状态槽、`2 DIP` 内缩/keyline、六种语义图形、Token 颜色、与 CurrentItem 蒙版/底边条的 ZIndex，以及状态槽随图片而非视口滚动。
- **TODO（后期迭代，当前不执行）**：Running 动效使用可控测试时钟验证 `800 ms` 顺时针线性周期、离开 Running 或容器 Detach 后停止、同帧终态不强制展示 Spinner；状态切换验证 `120 ms` 淡化，Reduced Motion 下旋转和淡化均为零时长。
- **TODO（后期迭代，当前不执行）**：状态迁移覆盖 Pending→Running→四类终态和 Pending→Failed/Skipped/Canceled 直接终态；StartRejected 清除临时 Pending，最终 BatchResult 相同状态不重播动画、不同状态只权威校正而不滚动画廊。
- **TODO（后期迭代，当前不执行）**：虚拟化回收覆盖 Running 容器复用后不残留旋转、颜色、图形、Tooltip 或自动化名称；重新实现时按稳定 Batch Index 恢复状态。普通浏览和单张任务不显示批量状态槽，输入收集阶段跳过项不伪装为执行期 Skipped。
- **TODO（后期迭代，当前不执行）**：无障碍检查确认六种状态均由不同图形和中文名称表达、颜色不是唯一信息、Presenter 不进入 Tab 顺序；失败/跳过 Tooltip 使用脱敏原因，缩略图更新不逐项抢占 LiveRegion。
- 设置页面的 Dirty 派生、显式保存、保存失败保留草稿、重试，以及“恢复默认值”只修改草稿。
- 前台任务运行期间图标轨、输入替换、当前项切换和参数编辑全部禁用；查看进度和取消仍然可用。
- `CanExecute`、可见性和恢复动作由状态组合派生，不出现相互矛盾的重复可写布尔状态。

少量 Avalonia 集成测试用于验证命令绑定、禁用态、焦点恢复、取消确认弹窗和关键状态投影；不应把每个 ViewModel 状态排列都重复做成昂贵的窗口级测试。

AtomUI 集成验收至少覆盖：

- 主桌面控件和 ColorPicker 主题在首个 Window 前完成注册，Light/Dark/FollowSystem 不缺资源；Desktop 项目没有 `AtomUI.Desktop.Controls.DataGrid` 包引用、命名空间或 `UseDesktopDataGrid()` 注册。
- 图标轨选中态与 `ActiveTool` 单向收敛；再次点击当前工具收起、切换工具替换右侧列，任务锁定时不可操作图标真实禁用。
- Slider/NumericUpDown、ColorPicker/HEX/Core RgbColor 的双向投影不产生第二份状态或循环更新。
- 图片走廊使用虚拟化面板；容器回收后不串用缩略图、Index、终态标记或命令，横向滚动、当前项跟随和批量状态投影在窗口缩放与高 DPI 下保持正确。
- ImagePreviewer 只消费查看状态，并在 Sources 替换或页面 detach 后释放过期资源；主视口和 CropCanvas 在代次切换、DPI 和窗口缩放后仍正确释放资源并提交像素矩形。
- Alert、Dialog/MessageBox、Message 和 Tooltip 的层级、owner、焦点恢复及关闭行为符合组件映射文档。

Desktop 项目加入解决方案后，发布验收命令至少包括：

```text
dotnet test AtomPix.slnx --no-restore
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 --self-contained true
```

## 33. 独立 Resize 契约测试规划

当前独立 Core / Workflow / Magick Resize 契约、批量逐图解析、Desktop Pixel / Percentage 控件、状态投影和 ViewModel 到真实 Workflow 的执行测试已经实现；真实窗口多 DPI 与键盘端到端验收仍是发布门禁。

- Core：覆盖 Pixel 单边、双边保持比例、非保持比例、整数与小数百分比，以及双边最小约束向下取整。
- Imaging.Abstractions：覆盖 `ImageResizeRequest`、`ImageResizeResult`、`ImageResizeCapabilities` 和 `SameFormatEncodingPolicy` 的有效与非法状态。
- Imaging.Magick：覆盖 JPEG/PNG/WebP 保持原格式、实际尺寸严格等于目标、EXIF 方向后的逻辑尺寸、元数据策略、有损质量、无损格式忽略质量、多帧拒绝与取消。
- Workflows：覆盖创建 Job 前的输入、动画、格式和极端尺寸拒绝；Skip、运行前取消、运行中取消及处理失败的合法状态迁移。
- 结果契约：成功时 `ActualOutputSize` 的宽高必须与 `TargetSize` 一致；失败、取消或跳过时允许为空，但必须保留目标尺寸与 Job 终态。
- 设置快照：便利入口只在提交前加载 `DefaultSameFormatEncodingPolicy`；任务接受后修改设置不得改变本次请求。

上述目标契约实现并通过后，才可把独立 Resize 标记为代码层完成。

## 34. 独立 Crop 契约测试规划

当前独立 Core / Workflow / Magick Crop 契约、CropCanvas、精确像素输入、比例联动和 Desktop ViewModel 到真实 Workflow 的执行测试已经实现；真实窗口多 DPI 坐标仍属于发布验收。已有画布方向键测试作为回归保护保留，但当前版本不要求扩大全页面键盘或屏幕阅读器验收。

- Core：覆盖 `CropRectangle` 的正尺寸与非负坐标不变量，以及结合逻辑 `ImageSize` 的合法边界、四边越界和整数溢出保护；合法矩形不得被钳制或重算。
- Imaging.Abstractions：覆盖 `ImageCropRequest`、`ImageCropResult`、`ImageCropCapabilities` 和同格式编码策略的有效与非法状态。
- Imaging.Magick：覆盖 JPEG、PNG、BMP、单帧 WebP 保持原格式，实际输出尺寸严格等于矩形宽高，不发生隐式 Resize、补边或选区移动。
- 格式边界：GIF、多帧 WebP、TIFF 以及其他未声明格式在创建 Job 前被能力检查拒绝；多帧输入不进入真实 Crop。
- Workflows：覆盖格式、帧数、输入尺寸和矩形边界的创建前拒绝，以及 Skip、执行前取消、运行中取消、处理失败和源文件接受后变化。
- 结果契约：成功时 `ActualOutputSize.Width / Height` 必须等于 `CropArea.Width / Height`；失败、取消或跳过时允许为空，但必须保留 CropArea 与 Job 终态。
- 设置快照：便利入口只在提交前加载公共 `SameFormatEncodingPolicy`；任务接受后修改设置或 UI 选框不得改变本次请求。

上述目标契约实现并通过后，才可把独立 Crop 标记为代码层完成。

## 35. Batch Resize 契约测试规划

当前 Batch Resize 共享参数、逐图尺寸解析、父子 Job、最终汇总、Desktop 表单、逐行预计尺寸和运行结果投影均已实现；10000 项 AtomUI ListView 虚拟化已在第 51 节进入压力门禁。

- 请求快照：输入顺序、共享 `ResizePolicy`、`OutputPolicy` 与 `SameFormatEncodingPolicy` 在批次接受时冻结，运行中不能被 UI 或设置修改。
- 共享规则：覆盖 Percentage、保持比例单边、保持比例双边最大约束，以及不保持比例的统一确定 Width / Height。
- 混合尺寸：同一共享规则应用到横图、竖图和方图时，每项 TargetSize 必须由自己的逻辑 InputSize 解析；保持比例场景不得误用第一张图片的目标尺寸。
- 无逐项覆盖：请求和执行计划中不存在单项 ResizePolicy；不同规则必须形成不同批次。
- 逐项结果：Probe 前失败、策略解析失败、能力/路径失败、Skipped、处理失败、取消和成功分别满足可空字段矩阵。
- 结果对齐：`BatchResizeResult.ItemResults` 与 `BatchResult.Items` 数量、顺序和 JobId 一致；尚未开始的 Pending 项不生成结果。
- 成功校验：每个成功项的实际格式保持不变，`ActualOutputSize` 严格等于该项自己的 `TargetSize`。
- 批量语义：单项失败继续、输出冲突 Skip、运行中取消、公共输出位置失效 Abort、最终进度与终态结果一致。
- Desktop：默认保持比例；双边保持比例显示最大宽高语义；关闭保持比例显示持续变形警告；逐行预计尺寸随输入或共享参数变化重算。

上述目标契约实现并通过后，才可把 Batch Resize 标记为代码层完成。

## 36. OpenFolder 与图片浏览集合契约测试规划

当前 OpenFolder、非递归图片过滤、自然排序、多来源批量追加、Desktop 当前图片预览、最新代次优先、损坏项保留，以及由已实现 ListView 容器触发的会话级缩略图延迟请求均已实现。缩略图请求使用独立取消边界和并发上限 `2`，不会在建立集合时一次性启动全目录解码；大目录容器回收、快速滚动和内存峰值仍属于发布压力验收项。

- Core / Infrastructure：覆盖当前层级枚举、规范化绝对路径、完整文件名、平台路径等价/排序规则、空目录、目录不存在、访问拒绝、枚举异常和取消；Windows 路径比较不区分大小写，不得递归子目录或把失败伪装成成功空集合。
- Workflow 发现规则：覆盖输入能力扩展名过滤、`UnsupportedFileCount`、规范化去重、文件名自然排序和完整路径稳定决胜；`image2` 必须位于 `image10` 之前。
- 空集合语义：可访问但没有候选图片时 `OpenFolderWorkflow` 成功返回空 `Items`；目录级失败返回结构化错误。
- 轻量集合：建立集合时不得调用逐项 Probe 或 Preview，不得创建 `ImageJob`、`BatchJob` 或 `BatchInputPlan`。
- 内容有效性：合法扩展名但内容损坏、格式伪装或枚举后删除的文件保留为候选项，并在 Desktop 后续调用 `OpenImageWorkflow` 时投影为 `Unavailable`。
- 首项选择：首个候选失败时继续尝试下一项，直到首个可用项；全部失败时保留错误占位且没有当前图片。
- 懒加载调度：当前项 Probe/主预览优先；缩略图只加载可见区和预取窗口、并发有界，不允许为整个大目录无界启动任务。
- 竞态与取消：快速切换当前项采用 latest-wins；更换来源、返回首页或销毁页面后，旧集合代次的晚返回结果不得写回。
- 缓存与目录变化：缓存限于当前会话，离开集合释放 Bitmap；第一阶段不建立磁盘缩略图缓存，也不自动追加目录新增文件。
- 业务隔离：首页打开文件夹不得创建 `BatchJob`；浏览集合既能以 `CurrentImagePath` 启动四类单张处理，也能在 Compress、Convert、Resize 面板中按完整冻结顺序启动三类批量处理。Crop 不得出现批量入口。

上述目标契约实现并通过后，才可把文件夹浏览标记为代码层完成。

## 37. 实时批量进度契约测试规划

当前三类批量 Workflow 的运行期进度序号、冻结输出计划、Running/终态单项消息和最终校正，以及 Desktop UI 线程防乱序投影均已实现。

- 无观察器：传入 null 或使用无进度便利重载时，执行顺序、取消语义和最终 `BatchResult` 必须与带观察器调用一致。
- 启动拒绝：请求、设置或公共前置检查失败时不创建 Job，也不发布任何进度。
- 初始消息：BatchJob 进入 Running 后发布 Sequence = 1、CompletedCount = 0、CurrentInputPath = null、ChangedItem = null 的快照。
- 单项定位：每条变化的 Index、JobId、InputPath 必须对应冻结输入计划；Desktop 对越界、路径不符、BatchId 不符的消息忽略并记录诊断。
- 消息顺序：Sequence 严格递增；重复、倒序、旧调用代次和终态后迟到的消息不能覆盖当前 UI。
- 迁移后发布：Running 消息只能在 Core 子 Job 进入 Running 后发布且 Result 为空；终态消息只能在迁移和结果构造后发布且 Result 状态一致。
- 直接终态：Pending 到 Failed、Skipped 或 Canceled 不发布伪造的 Running；CompletedCount 只随终态消息增加。
- 混合结果：成功、失败、跳过混合时，每次 Summary 计数与已经发布的终态项一致，单项失败后继续下一项。
- 取消：接受后第一项前取消允许只有初始消息；运行中取消先产生当前项 Canceled，再返回父任务 Canceled，后续 Pending 项不生成假结果。
- 观察器故障：进度适配器同步抛错或 Desktop 展示失败不得改变 Core Job 迁移和最终 `BatchResult`。
- 终态校正：最终结果与最后一份已消费进度不一致或部分消息未送达时，Desktop 必须以 `BatchResult` 重建权威终态。
- 粒度边界：第一阶段只验证按完成项目数计算的阶梯比例；当前项使用不确定状态，不测试或伪造单张图片内部百分比。
- 类型覆盖：压缩使用 `BatchCompressItemResult`；转换使用 `BatchConvertItemResult`；Batch Resize 使用 `BatchResizeItemResult`，实时结果字段满足各自终态不变量。

上述目标契约实现并通过后，才可把 UI 实时批量进度标记为代码层完成。

## 38. 批量终态恢复与 Skipped 契约测试规划

当前 Core/Workflow 已实现 Failed、Canceled、Skipped 的稳定终态和未启动项不伪造结果；Desktop 已实现失败项重试、处理未完成项和 Skipped 改用 AutoRename 的新草稿恢复动作。

- Skipped 唯一来源：MVP 只有目标存在且 `OverwritePolicy.Skip` 产生 `ImageJobStatus.Skipped`；不调用图片处理器，结果保留目标路径和 `OutputFileAlreadyExists`。
- 状态区分：图片损坏、能力不支持、读取/写入失败为 Failed；用户取消为 Canceled；公共前置拒绝为 StartRejected；输入计划过滤项不创建 Job。
- 批次汇总：Succeeded 与 Skipped 混合仍为 Succeeded；Skipped 不进入 FailedCount，也不单独造成 PartiallySucceeded。
- 提交快照：Desktop 保留的任务类型、输入顺序、处理参数、编码策略和输出策略必须等于实际提交请求，运行中设置变化不得回写。
- 重试失败项：只选择 Failed，排除 Succeeded、Skipped、Canceled 和未开始项，保持原提交顺序并形成 Ready 草稿，不立即执行。
- 处理未完成项：取消或中止后，排除 Succeeded/Skipped，包含 Failed、Canceled 和没有结果的原输入；空白“继续处理其他图片”不得复用该集合。
- 自动重命名处理：只对 `Skipped + OutputFileAlreadyExists` 建立新草稿，默认把 OverwritePolicy 改为 AutoRename，其他处理参数保持旧提交值。
- 参数来源：恢复草稿复制旧提交快照，不重新加载后来变化的默认设置；用户编辑后新请求使用编辑后的快照。
- 新任务隔离：再次点击开始后生成全新 BatchJobId、ImageJobId、Sequence 和 BatchResult；旧任务状态与统计不变，新结果不合并回旧结果。
- 错误修复：缺失、损坏或输出无权限项允许在新草稿中重新定位、移除或修改输出策略；Desktop 不以本地可重试分类替代 Workflow 正式校验。
- StartRejected：没有旧 Core Job，不显示终态恢复动作，只保留并修正原草稿。

上述目标契约实现并通过后，才可把批量失败重试、未完成项处理和 Skipped 恢复标记为代码层完成。

## 39. 透明图片转换契约测试规划

当前透明探测、显式保留/铺底、单张/批量结果、设置持久化以及 Desktop HEX / 黑白快捷背景控件与结果文案均已实现。

- Core：覆盖 `RgbColor` 的 RGB 边界、`#RRGGBB` 大写格式、白/黑默认值，以及 `ConversionProfile.TransparencyPolicy` 非空约束。
- 探测：分别覆盖无 Alpha、带 Alpha 但全部不透明、包含全透明像素、包含半透明像素；只允许 `HasTransparency = true` 与 `HasAlphaChannel = true` 组合。
- 结果不变量：`NotPresent / Preserved` 的背景色必须为空，`Flattened` 必须返回实际 `RgbColor`；非法组合不能构造。
- Magick PNG/WebP：真实透明输入转换到支持 Alpha 的目标格式必须保留透明度并返回 `Preserved`，不得因请求携带背景色而铺底。
- Magick JPEG：全透明区域分别使用白、黑和自定义背景色铺底；半透明区域验证 Alpha 混合后的像素颜色。JPEG 有损编码允许小范围色差，但测试不能只断言 Alpha 被移除。
- 颜色管理与顺序：带 ICC Profile 的透明样本在移除元数据前完成颜色空间感知的合成；`MetadataPolicy.Remove / Preserve` 都不得改变约定背景色语义。
- Workflow：单张覆盖默认背景色，批量共享一套背景色；成功的 `ConvertImageResult / BatchConvertItemResult` 原样交付处理器透明结果，无透明项目返回 `NotPresent`；失败、取消、跳过不伪造结果；运行中修改设置不影响已经提交的 Profile。
- 批量恢复：失败项和未完成项形成新草稿时复制旧提交背景色，不重新加载当前默认设置。
- Desktop：只有 `HasTransparency && JPEG` 显示颜色控件；HEX 非法时提交被拒绝并显示顶部中央 Message（设置保存仍禁用）；切换到 PNG/WebP 隐藏控件但保留草稿；结果文案取 `TransparencyProcessingResult`。

上述目标契约实现并通过后，才可把“透明图片转 JPEG 背景色”从设计缺口标记为代码层完成。

## 40. MetadataPolicy 与色彩配置契约测试规划

当前 MetadataPolicy、ICC 独立保留、方向规范化和 v1 设置兼容已经实现；真实相机样本矩阵仍需在发布验证中扩充。

- Core：`MetadataPolicy` 只允许互斥的 `Preserve / Remove`；每个 Compression、Conversion 和 SameFormatEncoding 请求必须携带其中一个有效值。
- AppSettings：三个默认 Profile 的 `MetadataPolicy` 必须相等；设置页面保存一次同步更新三处，持久化值不一致时加载失败而不是任选其一。
- Probe：带 EXIF 但无 ICC、带 ICC 但无描述性元数据、两者都有和两者都没有四种样本，分别验证 `HasMetadata / HasColorProfile`，不得把 ICC 计入 `HasMetadata`。
- Preserve：目标格式支持时保留 EXIF、GPS、IPTC、XMP、注释等仍有效信息，同时保留 ICC；不承诺原始 Profile 字节顺序完全一致。
- Remove：删除 EXIF、GPS、IPTC、XMP、注释和内嵌缩略图等拍摄或描述性信息，但仍保留 ICC / ICM；不得使用无差别 `Strip()` 作为最终实现。
- Orientation：使用 Orientation 非 TopLeft 的真实样本覆盖 Preserve 和 Remove；输出像素方向正确，方向标记删除或规范为 TopLeft，重新打开不得发生二次旋转。
- 处理后失效信息：Preserve 下的尺寸、缩略图或方向字段不能继续保存旧值；目标格式无法承载的字段允许丢失并记录为格式能力限制。
- 色彩验收：Display P3 或其他带 ICC 样本经过压缩、转换、Resize、Crop 后，Remove 与 Preserve 都保留有效色彩解释；不能出现“删除 ICC 但像素仍按原色域编码”的组合。
- 结果细节：`ImageProcessingDetails.MetadataRemoved` 只表示拍摄、位置和描述性信息已移除，不表示 ICC 被删除。
- Desktop：复选框勾选映射 Remove、未勾选映射 Preserve；辅助文案说明 ICC 保留，浏览器分别展示拍摄信息和色彩配置。

上述目标契约实现并通过后，才可把当前 `image.Strip()` 的 Metadata 行为标记为符合设计。

## 41. 源文件覆盖保护契约测试规划

- Core：`OutputPathConflictsWithInput` 是稳定错误码，分类为 `Validation`；`OverwritePolicy` 不能表达覆盖任务输入。
- 单张四类 Workflow：`SameAsInput + KeepOriginalName + 保持原格式 + Overwrite` 在创建 Job、目录或临时文件前返回 `StartRejected`，图片处理器零调用。
- 单张转换：目标扩展名改变且最终路径不同可以正常执行；路径比较不能只比较文件名。
- AutoRename / Skip：同路径场景分别选择新名称和产生 `Skipped + OutputFileAlreadyExists`，不能误报源文件冲突。
- 批量：任一计划输出命中自己的输入或批次内另一输入时，整个批次在父子 Job 创建前拒绝；验证 `ConflictCount` 和首个冲突上下文。
- 跨平台路径：Windows 大小写差异、`.` / `..` 与分隔符归一化后仍能识别同一路径；第一阶段不测试符号链接、硬链接或外部进程竞争。
- Imaging 契约：直接提交相同 InputPath / OutputPath 时防御性返回 `OutputPathConflictsWithInput`，不得修改源文件。
- 输出目录：Workflow 在创建 Job 前通过 `IFileSystemService` 准备所有实际处理目录；目录准备失败为 StartRejected。Magick 直接收到不存在目录时返回结构化写入失败且不创建业务目录，任务接受后目录被外部移除则形成 Job 失败。
- Desktop：错误映射为阻断弹窗；“改为自动重命名”只修改草稿并回到 Idle，“返回修改”保留草稿，两者都不自动启动且不存在“仍然覆盖”入口。

## 42. 批量文件名格式与输出计划契约测试规划

当前 `OutputNamingPolicy.CustomPattern`、自动三位序号、冻结输出计划、跨输入覆盖保护、AutoRename 以及 Desktop 快捷插入、实际生效格式和输出示例均已实现。

- Core 预设：KeepOriginalName、AppendSuffix 和 CustomPattern 分别解析为 `{name}`、`{name}{Suffix}` 和用户格式；扩展名不进入格式。
- 模板校验：只接受 `{name}` 与最多一个 `{index}`；未知、大小写错误、未闭合占位符返回 `InvalidOutputNamingPattern`，路径分隔符和非法展开名返回 `InvalidOutputPath`。
- 自动序号：批量数量大于 1 且基础格式缺少 `{index}` 时自动在末尾追加 `_{index}`；一项批量和单张不自动追加。
- 序号格式：1、8、999、1000 项分别验证最少三位和随总数量扩展；编号严格来自冻结输入顺序。
- 自定义名称：纯文本 `holiday` 在多项批次生成 `holiday_001`、`holiday_002`；`web_{index}_{name}` 尊重用户占位符位置。
- BatchOutputPlan：Items 与输入数量、顺序、ItemIndex 和 SequenceNumber 一一对应；所有 Process OutputPath 唯一且在任务接受后保持不变。
- 稳定性：前项成功、失败、Skipped、取消都不改变后项序号或计划路径；失败前项的名称不转交给后项。
- 冲突策略：序号先形成计划名，磁盘冲突再应用 Skip / Overwrite / AutoRename；`holiday_001.webp` 的自动重命名结果为 `holiday_001_1.webp`。
- 源文件保护：展开后的每个输出仍与完整输入集合比较，命中时沿用 `OutputPathConflictsWithInput`。
- 恢复任务：新草稿按自己的当前顺序重新编号，旧 BatchOutputPlan 和旧结果文件名不改变。
- Desktop：占位符快捷项、实际生效格式、输出示例、缺少 `{index}` 提醒、非法格式提交反馈和运行期只读快照均有 ViewModel 测试。

## 43. Custom / Smart 压缩契约测试规划

当前 Custom / Smart Headless 编码、`AppliedQuality` 与 Desktop 模式/质量控件及实际质量展示均已实现；处理前效果预览、体积估算和命名预设按已冻结范围延后。

- Core：`Custom` 必须携带 `1..100` 的 `ImageQuality`；覆盖 `1`、`100`、常用中间值、空值和越界值。`Smart` 不接受用户质量，其内部候选不进入 `AppSettings`。
- Abstractions/Magick：Compress 只接受 JPEG、PNG、WebP 同格式输出；请求扩展名与 Probe 格式不一致时拒绝，成功结果满足 `InputFormat == OutputFormat`，PNG 转 WebP 只能由 Convert 完成。
- Imaging 固定模式：HighQuality、Balanced、Maximum 和 Custom 只按指定质量编码一次，不得因结果偏大而静默降低质量；成功的有损结果准确返回 `AppliedQuality`。
- Imaging Smart：JPEG 覆盖 `82/77/72/67/65`，WebP 覆盖 `80/75/70/65`；验证首个较小候选即停止、触底仍未变小时选择最小有效候选、未采用临时文件被清理，以及最终 `AppliedQuality` 准确。
- 输出保留：固定模式或 Smart 的最终有效输出等于或大于原图时仍正常保存并保持任务成功；不存在丢弃结果、改为 Skipped 或等待用户选择的分支。
- 无损格式：PNG 等只做格式支持的无损优化，不执行质量递减、不改变输出格式，`AppliedQuality = null`。
- Workflow：单张原样投影 `AppliedQuality`；批量使用一套共享 Custom 质量，逐项结果与 JobId/顺序一致，失败、取消和跳过不得伪造实际质量。
- 设置：允许持久化完整的 `DefaultCompressionProfile(Custom, Quality, MetadataPolicy)`；Custom 缺少合法质量时保存失败，Smart 不持久化内部候选值。
- Desktop 单张：五种模式完整可选；Custom 的滑块与整数输入双向同步，非法值在提交时拒绝并显示顶部中央 Message；切换模式保留会话值但非 Custom 不提交；无损输出隐藏或禁用质量控件。
- Desktop 批量：一个 Custom 质量应用于全部有损项目；混合批次显示受影响数量，全部无损时禁用并说明原因；运行和恢复均使用提交快照，不提供逐项覆盖。
- Desktop 结果：Smart 和 Custom 显示 Workflow 返回的实际采用质量；无损输出显示“无损优化”，不得由模式名称反推质量。

## 44. 中性文件体积变化契约测试规划

当前 Core 已使用中性的 `SizeDeltaBytes / SizeDeltaRatio / FileSizeChangeKind`，批量只汇总大小可比较的成功项；Desktop 单张和批量结果均按该中性口径展示。

- 单项方向：分别验证 `1000 -> 600` 派生 `SizeDeltaBytes = -400 / Reduced`、`1000 -> 1000` 派生 `0 / Unchanged`、`1000 -> 1100` 派生 `100 / Increased`。
- 单项比例：比例统一为 `(Output - Input) / Input`；输入为零时差值可计算但比例为空，输入或输出任一缺失时差值、比例和类型全部为空。
- 只读派生：`FileSizeChangeKind` 不能由构造调用方传入，必须与原始字节数一致；四个旧 `Saved*` 属性不保留兼容别名。
- 批量比较集合：只选择 `Succeeded` 且双边大小完整的项目。Failed、Canceled、Skipped、未开始项，以及异常缺少任一大小的成功项都不进入求和或方向计数。
- 批量汇总：验证 `ProcessedOutputSizeBytes - ProcessedInputSizeBytes`、有符号比例、三类 ItemCount 与 `SizeComparedItemCount`；一个减少项和一个等量增加项应产生总体 `Unchanged`，但项目计数仍分别保留。
- 空集合：没有可比较成功项时 Processed 两个求和值为 `0`，总体差值、比例和类型为 `null`，不能与真实 `0 B / Unchanged` 混淆。
- Workflow：处理器失败、取消和 Skip 的 `OutputSizeBytes` 保持 `null`，不得补零；输出变大的成功结果仍为 `Succeeded` 并保留输出路径。

## 45. 图片处理资源保护契约测试规划

当前 Workflow 静态资源预检、Magick Ping 防御复检、进程级 memory/map/disk/thread 上限和批量中止语义已实现；真实资源耗尽、磁盘满与长期压力仍是发布测试项。

- 能力模型：`ImageResourceCapabilities` 七个上限均为正数，像素数使用 `long`；集合和嵌套能力防御性不可变。
- 默认能力：Magick 声明 `512 MiB` 输入文件、`32768 px` 输入/输出单边及 `128000000 px` 输入/输出像素上限，Resize/Crop 专用能力不得放宽公共边界。
- 边界检查：文件字节、Width、Height、PixelCount 分别覆盖等于上限成功和超过上限拒绝；恶意宽高乘积不得通过 `int` 溢出绕过。
- 读取顺序：超限样本在完整像素解码和 Job 创建前被拒绝；可通过伪造轻量探测器或受控大图头验证图片处理方法零调用。
- 无隐式降级：超限时不自动 Resize、Crop、改格式或降质量，不产生输出文件。
- 按需分配：组合根设置 Memory/Map/Disk/Thread 上限后，不创建等量内存、映射或磁盘文件；只允许建立空私有缓存目录。
- 资源异常：达到配置的 memory、map 或 pixel-cache 上限映射为 `ImageResourceLimitExceeded`；操作系统报告输出卷或私有缓存卷空间不足映射为 `InsufficientDiskSpace`，底层 Magick 异常不穿透。
- 单张语义：创建前文件/尺寸超限为 `StartRejected` 且没有 Job；接受后资源异常形成 Failed 终态并清理临时文件。
- 批量语义：某项文件/尺寸/内存资源失败后继续下一项；输出卷或公共私有缓存磁盘不足使当前项 Failed 并 `BatchJob.Abort`，剩余 Pending 项没有伪造结果。
- 清理：成功、失败、取消和资源异常后均不遗留未提交候选、输出半成品或失去关联的私有像素缓存；清理错误不覆盖原资源错误。
- Desktop：错误卡显示实际值和上限，不提供自动缩小绕过；磁盘不足提供释放空间、改输出目录和处理未完成项恢复动作。
- Desktop：单张和批量分别显示“减少”“文件大小未变化”“增加”；展示差值和比例的绝对值，不显示负号或“节省负数”。没有数据时显示“暂无可比较结果”或隐藏单项体积行。
- Desktop 验收：右侧处理面板使用方向化体积文案；批量结果态显示成功比较项数和总体变化；“体积增加但任务成功且结果已保存”必须有明确反馈。

## 46. 诊断与本地日志契约测试规划

当前已实现 OperationId、Job/Batch 关联、Magick 内层作用域、本地 JSON Lines 轮转、PathToken、路径清理、DiagnosticId、日志故障隔离和 Desktop 全局错误边界。后续发布验收必须继续做真实未处理异常注入、日志目录故障和用户可复制诊断编号的端到端核对。

- 作用域：一次 Desktop 命令到 Workflow、Imaging 和 Infrastructure 共享 OperationId；Headless 直接调用 Workflow 时自动创建，Core Job 创建后追加 JobId/BatchId。
- DiagnosticId：只有未预期异常和全局错误边界生成 `APX-` + 12 位大写十六进制编号；UI 编号能够定位唯一日志事件，普通校验、Skip 和用户取消不生成。
- 级别：开始/终态/取消为 Information，已知可恢复运行失败为 Warning，未预期异常为 Error；批量不逐项记录成功或每条 UI 进度。
- `Dispatcher.UIThread.UnhandledException` 代表当前前台 UI 操作失效，可以显示带 DiagnosticId 的错误 Dialog；`TaskScheduler.UnobservedTaskException` 发生在后台任务最终化时，与用户当前点击没有可靠时序关系，只写入本机错误日志并标记 Observed，禁止随机打断仍可继续使用的浏览会话。错误 Dialog 正文必须显式绑定/赋值为脱敏文案，不得继承 Shell DataContext 后显示 ViewModel 类型名。
- 单次异常所有者：同一原始异常只有一条携带脱敏调用栈的事件，外层终态不重复写入异常。
- 隐私：输入/输出/缓存/设置路径、文件名、图片内容、EXIF/XMP/ICC 内容、命名格式和敏感凭据均不以明文出现；异常消息、调用栈和 `AtomPixError.Details` 也经过过滤。
- PathToken：同一路径在同一 Session 内一致，使用不同会话密钥后不一致；扩展名、文件大小和尺寸作为独立字段时不泄漏文件名。
- 存储：UTF-8 JSON Lines 单文件不超过 `10 MiB`，保留不超过 `7` 天且目录总量不超过 `50 MiB`，最旧文件优先清理。
- 故障隔离：日志目录无权限、磁盘已满、写入、flush、滚动和清理失败不阻断启动，不改变 Workflow 结果、Job 状态或原始错误。
- 依赖：Core 与 Imaging.Abstractions 不引用日志框架；具体文件 Provider 只由 Infrastructure 提供并在组合根注册。
- 无遥测：默认执行路径不建立日志上传、远程遥测、持久设备标识或跨会话路径关联。

## 47. 2026-08-07 Headless 施工回归快照

本快照覆盖第 33–46 节已落地的 Headless 契约；Desktop 尚未创建，因此不包含 ViewModel、Avalonia/AtomUI 集成或真实应用发布测试。

```text
Core: 42 passed
Imaging.Abstractions: 18 passed
Infrastructure: 37 passed
Imaging.Magick: 49 passed
Workflows: 102 passed
Total: 276 passed
```

执行命令：

```text
dotnet restore AtomPix.slnx
dotnet test AtomPix.slnx --no-restore --logger "console;verbosity=minimal"
git diff --check
```

本轮确认：独立 Resize/Crop、文件夹与批量输入、冻结输出计划、Batch Resize、实时进度、资源保护、诊断脱敏和跨层 OperationId 均有自动化证据。`Magick.NET-Q8-AnyCPU 14.14.0` 在 restore/test 中仍报告多条 NU1901/NU1902 已知低/中危公告；这不是测试失败，但必须作为依赖安全阻断项单独评估，不能在发布结论中忽略。

## 48. 2026-08-07 Desktop 首轮施工快照

`AtomPix.Desktop` 已进入解决方案并固定使用 Avalonia `12.1.0`、AtomUI 正式包 `6.1.2`。本地 `out-lib/AtomUI` 的 `6.1.3` 只用于公开 API 核对，不形成 ProjectReference。首轮覆盖 Shell、首页、图片浏览器、平台 Picker/Launcher、图片展示适配器和 Resize 内容页。

Desktop 自动化当前为 `8 passed`，覆盖：选择取消无副作用、打开图片进入浏览器、打开文件夹不创建批量任务、Probe/Preview 投影、Shell 前台任务锁、DataGrid 依赖禁令、Resize 双约束取小与百分比规则、Resize 真实 Workflow 执行及锁释放。隐藏窗口启动烟测持续 5 秒未提前退出，证明 AtomUI Application、主题、Window 和首屏资源可以在真实桌面生命周期中初始化。

加入 Desktop 后已执行解决方案全量回归，共 `284 passed`：Core `66`、Imaging.Abstractions `18`、Imaging.Magick `49`、Infrastructure `41`、Workflows `102`、Desktop `8`，没有失败或跳过。Magick.NET 14.14.0 的 NU1901/NU1902 公告仍是发布安全阻断项，Desktop 接入不会降低该风险等级。

## 49. 2026-08-07 Desktop 第一阶段贯通快照

本快照在第 48 节首轮纵向切片之上完成正式页面施工。首页、浏览器、压缩、转换、Resize、单张 Crop、三类批量任务、设置、最近记录、关闭确认和全局错误边界均已进入生产组合根；原有通用空页和模拟处理入口已经移除。批量页使用 AtomUI 主包 `ListView`，没有引用、注册或复制 DataGrid 包。

Desktop 自动化当前为 `14 passed`，除第 48 节原有覆盖外，新增验证：设置公共 Metadata 策略同步、恢复默认只修改草稿、Discard 恢复原快照、Custom 压缩质量提交、透明 PNG 转 JPEG 的确定性背景色、Crop 3:2 比例向下取整与精确矩形提交。Workflows 自动化新增最近记录 Load / Remove / Clear 和每条批量进度携带冻结 `BatchOutputPlan` 的断言。

当前解决方案实际 Release 回归基线：

```text
Core: 66 passed
Imaging.Abstractions: 18 passed
Infrastructure: 41 passed
Imaging.Magick: 49 passed
Workflows: 103 passed
Desktop: 14 passed
Total: 291 passed
```

本轮已把 `Magick.NET-Q8-AnyCPU` 从 `14.14.0` 升级到 `14.16.0`。升级后 restore 无 NU1901 / NU1902，Release 全量 `291 passed`；`dotnet list AtomPix.slnx package --vulnerable --include-transitive --no-restore` 对 12 个项目均报告没有易受攻击的包。Windows x64 自包含 publish 成功，发布产物隐藏启动 5 秒未提前退出，证明真实组合根、AtomUI 主题和首个 Window 可以初始化。

因此自动化构建、测试、依赖审计和基础启动烟测门禁通过。正式分发前仍需完成人工多 DPI、超大图、磁盘压力与关键真实图片业务验收；大列表容器回收已在后续第 51 节自动化。键盘全路径和屏幕阅读器不属于当前版本需求。

## 50. 2026-08-07 Desktop 交互闭环快照

本快照继续补齐第 49 节中“页面存在但交互闭环不足”的部分，不把压力验收冒充功能实现。完成项包括：

- 图片浏览器缩略图按已实现容器延迟请求，并发上限为 `2`；主预览和缩略图具有独立取消边界。
- 上一张、下一张、适应窗口、`1:1`、`25%..400%` 缩放、不可用项保留与显式移除均已接入 ViewModel 命令。
- 压缩、转换、调整尺寸、裁剪四个快捷操作分别按输入格式、单帧约束及处理器能力计算 `CanExecute`，不再共享笼统开关。
- 首页拖放采用薄 Avalonia Bridge；View 只提取一个本地文件或目录路径，随后复用 `OpenImageWorkflow` / `OpenFolderWorkflow`、最近记录和原地错误状态。
- 压缩、转换、Resize、Crop 和批量页均展示并提交同一语义的 `OutputPolicy` 草稿：输出位置、子目录/自定义目录、文件名格式与 Skip/Overwrite/AutoRename。无效草稿在提交边界拒绝并显示顶部中央 Message，不得由命令系统静默吞掉；源文件冲突改为 AutoRename 后修改的是用户可见字段且不会自动重启。
- 批量多图仍在冻结提交快照中自动补 `_{index}`；输出位置和冲突策略不再暗中沿用不可见默认值。
- Shell 路由和 AtomUI NavMenu 选中态同步；失效最近记录可按原类型重新定位，替换来源验证成功后才删除旧记录。
- 透明图片转 JPEG 的主预览显示当前背景色；CropCanvas 支持方向键 `1 px`、Shift + 方向键 `10 px` 微调。

Desktop 无窗口状态测试从第 49 节的 `14` 项扩充为 `34` 项。新增覆盖拖放单文件/目录/多项拒绝、按需缩略图、浏览切换与缩放、按需原始像素 `1:1`、快速切换 latest-wins、预览失败降级、按能力禁用、不可用项移除、OutputPolicy 条件校验和真实输出路径、批量重复追加与自动序号、最近记录重新定位、转换背景预览，以及设置保存失败保留 Dirty 草稿。

当前解决方案 Release 回归基线：

```text
Core: 66 passed
Imaging.Abstractions: 18 passed
Infrastructure: 41 passed
Imaging.Magick: 49 passed
Workflows: 103 passed
Desktop: 34 passed
Total: 311 passed
```

验证命令和结果：

```text
dotnet test AtomPix.slnx -c Release --no-restore
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore
发布产物隐藏启动 5 秒：通过，未提前退出
git diff --check：无空白错误（仅现有行尾转换提示）
```

本段是第 50 节形成时的风险快照；其中大目录容器回收、自动化压力和未处理异常边界已由第 51 节后的门禁取代。`311 passed` 不是当前基线，现行数字与尚需外部验收的事项以第 51 节为准。

## 51. 2026-08-07 UI 自动化、压力测试与发布流水线终态

本节取代第 50 节末尾关于“尚未建立自动化压力与发布门禁”的描述。人工观感和真实辅助技术验收仍然是发布责任，但它们不再代表工程中缺少可执行的自动化基础设施。

新增正式测试项目与门禁：

```text
tests/AtomPix.Desktop.UiTests
  Avalonia 官方 HeadlessUnitTestSession + Skia 真实渲染
  所有生产页面与主窗口加载、布局和帧捕获
  真实按钮 Click、文本选区插入、焦点恢复
  CropCanvas 真实键盘输入与自动化属性
  所有生产数据输入控件的稳定 UI 自动化定位名称门禁
  AtomUI ListView 10000 项虚拟化压力

tests/AtomPix.StressTests
  2000 项 BatchCompressWorkflow + 实时进度序列 + 唯一输出计划
  4000 条并发 JSON Lines 日志滚动与总量上限
  4096×4096 图片 Probe + 4 路并发受限 Preview
```

该时点的 Desktop 无窗口状态测试曾覆盖 Preview/Thumbnail/Probe 三类有界 LRU；这属于 ImageGallery 迁移前历史实现。当前生产只保留 AtomPix Probe LRU，主图/缩略图调度、缓存和 Lease 由 ImageGallery 自己的专项测试覆盖。其余窗口重新激活时复核输出、批量终态只读、恢复草稿、失败详情、诊断编号复制和重新定位仍由 AtomPix Desktop 测试负责。

Imaging.Magick 的输出提交已经收敛为同目录临时文件编码后原子替换。编码、替换或权限失败会清理临时文件并保留已有目标；可注入提交器稳定验证 `ImageWriteFailed / Permission` 与 `InsufficientDiskSpace / FileSystem`。批量 Workflow 对磁盘空间不足中止剩余项并保留既有成功结果。

发布脚本固定为：

```text
eng/verify.ps1
  restore -> Release build -> 功能/UI 自动化 -> 压力测试
  -> NuGet 漏洞审计 -> git diff --check

eng/publish.ps1
  win-x64 / linux-x64 / osx-arm64
  net10.0 self-contained + single-file
  清理调试符号 -> release-manifest.json -> zip/tar.gz -> SHA-256

eng/smoke-test.ps1
  从正式 publish 目录隐藏启动，若在观察窗口内异常退出则失败

eng/windows-ui-automation.ps1
  隐藏启动 win-x64 正式发布产物 -> 检查 UIA 主窗口
  -> 检查六个图标轨动作均以可用 Button 暴露
```

Windows 发布进程脚本只负责验证正式 EXE 的原生窗口和 UIA 暴露。设置按钮的真实 Pointer Down/Up、`DesktopRoute.Settings` 参数绑定、普通设置页立即切换，以及“压缩配置/转换配置/输出配置/关于”连续内容组合，由 `AtomPix.Desktop.UiTests` 在 Avalonia Headless 的确定性坐标系内验证；不得在无人值守 Windows 会话中依赖前台焦点、多显示器位置或 DPI 虚拟化注入物理光标。设置态还必须断言全局图标轨和右侧工具面板隐藏、四个分区同时存在、左侧命令产生真实纵向偏移。

`.github/workflows/ci.yml` 对 Windows、Linux、macOS 执行 Release 构建、全部非压力测试、Avalonia/AtomUI UI 自动化和依赖审计；独立 Windows 压力任务执行大批量、大图、日志与 ListView 虚拟化；三平台随后各自在原生 Runner 上打包并执行启动烟测，Linux 使用 Xvfb，Windows 额外执行正式发布进程的 UIA 导航检查。`.github/workflows/release.yml` 只在完整门禁通过后为版本 Tag 生成三平台不可变归档、独立 SHA-256 文件并发布 GitHub Release。

本机施工验收已实际生成并检查：

```text
Core: 42 passed
Imaging.Abstractions: 18 passed
Infrastructure: 37 passed
Imaging.Magick: 55 passed
Workflows: 103 passed
Desktop 状态/交互: 40 passed
Desktop UI 自动化: 6 passed（5 项功能/控件定位 + 1 项虚拟化压力）
独立压力项目: 3 passed
Total: 304 passed

AtomPix-0.1.0-local-win-x64.zip + .sha256
AtomPix-0.1.0-local-linux-x64.tar.gz + .sha256
AtomPix-0.1.0-local-osx-arm64.tar.gz + .sha256
Windows 发布目录 5 秒真实启动烟测：通过
Windows 发布进程 UIA 导航烟测：通过
14 个项目的直接与传递 NuGet 漏洞审计：无已知漏洞
```

签名证书、公证账号和商店身份属于发布主体持有的外部密钥，不写入仓库。流水线当前发布可校验的自包含便携包；若进入系统商店或启用平台代码签名，必须通过受保护 Secret 和独立签名 Job 注入，不得把证书、密码或长期凭据提交到源码。

## 52. 2026-08-11 Desktop 现行视觉重构验收快照

本节记录 2026-08-11 上一轮生产 Shell：沉浸式 Home/Browser、贴左图标轨、AtomUI 官方右侧 Drawer 和设置 Dialog。Browser 只维护一份可追加集合；压缩、转换和 Resize 在集合不少于两张时同时显示单张与批量动作，Crop 始终单张。批量运行状态投影到缩略图，Running 项驱动画廊真实横向偏移；恢复失败项或未完成项时，走廊同步重建为新的目标子集。该历史快照不再定义 2026-08-23 之后的正式视觉目标。

本轮额外验证增量追加不会重载当前预览或丢失选择，工具面板打开后集合数量和批量动作会同步更新；设置页面的所有返回入口统一静默撤销未保存草稿，不显示二次确认。UI 自动化按现行首页尺寸、图片画廊、图标轨、普通右侧面板、设置连续页面及全生产 View 渲染重新建立。

本机 Release 结果：

```text
Core: 42 passed
Imaging.Abstractions: 18 passed
Infrastructure: 37 passed
Imaging.Magick: 55 passed
Workflows: 103 passed
Desktop 状态/交互: 50 passed
Desktop UI 自动化: 12 passed
独立压力项目: 3 passed
Total: 320 passed
Build: 0 warnings, 0 errors
```

该数字是 2026-08-11 本轮代码与文档同步后的本机基线；历史章节中的较小数字只描述其形成时点，不能作为当前完成度结论。

## 53. 2026-08-24 AtomUI.Labs ImageGallery 迁移门禁

生产代码迁移已完成；本节是每次发布必须重跑的专项测试门禁，最终通过数字记录在本节末尾。专项架构与供应链设计见 [AtomUI.Labs ImageGallery 接入与迁移设计](../modules/desktop/atomui-labs-imagegallery-migration.md)。

迁移的第一道门禁不是页面截图，而是可复现依赖：

- 仓库内本地 NuGet 源存在 `AtomUI.Labs.Controls.ImageGallery.6.0.8.nupkg`，SHA-256 为 `86B4A7E63D290356B05A804B37D8808C797FF5FC7C036057302ED6A48C2BB35F`；缺失或不一致时 Desktop 构建目标确定性失败。
- `NuGet.config` 使用相对本地源，`RestorePackagesPath` 固定到仓库内 `eng/nuget-cache`；构建不读取 `D:\work\c#\AtomUI.Labs`，同版本开发机旧缓存不能掩盖本次重打包制品。
- AtomUI Desktop/ColorPicker/Font/Icon 与 Labs 全部统一为 `6.0.8`，Avalonia 为 `12.1.1`；依赖图不得混装 `6.1.3` AtomUI Core 或出现重复资产。
- CI 三平台 restore/build/publish 使用同一 nupkg；发布产物实际包含程序集和主题/语言资源。

Desktop 状态与 UI 自动化至少新增或迁移以下覆盖：

- `UseImageGallery()` 在首窗前幂等注册，Light/FollowSystem 首帧无缺失资源；公共 AXAML 命名空间可加载。
- Home、Browser、Crop 与批量状态均从独立浅色标题栏下方开始；图片不进入标题栏下层，切图不会改变标题/Caption 前景或产生旧顶部渐变。
- 打开图片或文件夹后 `ActiveTool=None`，ImageGallery 占满内容区且不存在空白右列；点击工具后同一窗口内形成“可伸缩左列 + 约 `380 px` 右列”，再次点击当前工具或关闭按钮恢复全宽浏览态，切换其他工具只替换右列内容。
- Browse/Operate 切换不改变顶层 `ClientSize`，不创建 Drawer/Popup/遮罩，不播放边缘滑入动画；右列 Loading 首帧与 ActiveTool 在同一次 UI 提交中可见，不能等待图片预览或配置异步加载一至两秒。
- Browser item/source adapter 的 Key/Identity 稳定，集合换代、项目移除、快速切换和离场取消后没有晚返回覆盖或 lease 泄漏。
- JPEG/PNG/静态 WebP/BMP、JPEG EXIF 方向、损坏/被删除/资源超限项的主图与缩略图行为符合现有范围，错误文案不泄漏路径。
- 默认 Fit 完整显示、白色留白、ActualSize、`25%..400%`、ImageGallery 原生 `ZoomStep`、放大平移与窗口尺寸隔离；AtomPix 不实现固定百分点步进，图片缩放不得改变顶层 ClientSize。
- 追加图片、上一张/下一张、缩略图点击、选中项滚入可见、走廊滚轮横移和首末项不循环保持现有语义；Browse/Operate 切换后画廊只按 ImageGallery 自身 Bounds 重新居中和响应式收窄，不执行 Drawer 避让。
- 10,000 项虚拟化、快速滚动、容器回收、主图优先加载、缓存预算与相邻预取压力达标，不能同时运行旧 Browser 缓存管线。
- 当前版本只要求最终 `BatchResult` 校正与批量进度、当前处理项、终态在右侧面板完整通过；与 `CurrentItem` 分离的 `ActiveBatchIndex` 自动跟随、用户滚动暂停、缩略图六态及 Running 图标动画均属于后期 TODO，不阻断当前迁移。
- 所有模式显式关闭 ImageGallery 主图上一张/下一张按钮，导航只保留走廊按钮和缩略图；Crop 模式以 `ResourceOnly` 额外停用默认主图呈现、命中和默认工具，但保留走廊、选择与逻辑主图加载。CropCanvas 独占 Pointer，只借用 expected item 对应外部 Lease 的 `IImage`，输出像素矩形不受 Gallery 缩放/旋转状态影响。
- Crop 布局门禁必须在默认窗口、最小窗口和窗口尺寸变化后断言：CropCanvas 左边界位于导航轨安全区之后，底边界位于浮动画廊安全区之上，Gallery 仍占满自己的左列 Bounds；工作台使用 `#F5F7FA`，全画布不存在旧 `#202733` 深色填充。安全区只依赖共享 Layout Token 和 Avalonia `Measure / Arrange`，不得使用定时器或窗口绝对坐标。
- Empty/Loading/Error/Unavailable 与“移出不可用项”等恢复动作可用；稳定 AutomationProperties 不依赖 Labs internal 类型、伪类或 Template Part。

旧 `AtomPixImageGalleryViewer`、Browser 使用的 `AtomPixImageViewport`、旧主题、旧缩略图容器和重复缓存已经删除。Release build、全解决方案测试、UI 自动化、压力测试、当前平台 publish/启动烟测、依赖检查及 `git diff --check` 仍是每次迁移或升级的收口条件；其他平台 publish 由对应 CI runner 负责，不能用 Windows 本机构建冒充。

设置快照在主窗口首帧后低优先级预加载；预加载和用户提前进入设置共享同一次 Load，磁盘读取完成后再把 `SettingsPageViewModel` 设为 `CurrentPage`。设置不使用 AtomUI Dialog、Overlay、遮罩或 Content 重托管。页面左列“压缩配置、转换配置、输出配置、关于”四个分区按钮必须拉伸为同宽并左对齐；右列四个分区必须同时存在于单一 ScrollViewer。点击左列触发约 `220 ms` 的纵向定位滚动，手动滚动同步左列选中态。设置采用显式保存，返回 Dirty 草稿时直接恢复最近一次已保存快照，禁止再次显示“保存/放弃/留在设置”提示。当前设置页没有主题选择，旧 `IDesktopAppearanceService` 及“打开设置即重设 RequestedThemeVariant”链路保持删除。对应 Headless 用例通过真实鼠标按下/释放进入设置，断言普通页面组合、连续分区、真实滚动偏移、设置态图标轨/工具面板隐藏及返回恢复。

2026-08-25 本机最终 Release 收口结果：

```text
Core: 42 passed
Imaging.Abstractions: 18 passed
Infrastructure: 37 passed
Imaging.Magick: 55 passed
Workflows: 103 passed
Desktop 状态/交互: 51 passed
Desktop Headless UI: 7 passed（6 项功能/组合 + 1 项压力）
独立压力项目: 3 passed
Total: 316 passed
Build: 0 warnings, 0 errors
NuGet vulnerability audit: 14/14 项目无已知漏洞
Windows 发布进程 UIA: passed
Windows 发布目录 5 秒启动烟测: passed
win-x64 self-contained single-file package: created with SHA-256 sidecar
```

## 54. 2026-08-26 设置默认值传播与任务快照门禁

设置保存后的正式生效语义为：当前面板草稿、已经提交的单张任务和活动批量任务保持冻结；下一次明确创建的普通工具草稿/任务读取最新默认设置。浏览走廊切换当前图片属于同步输入，不属于新建草稿，不得重置用户已经编辑的参数。

自动化门禁采用 11 项独立配置维度乘以 Compress、Convert、Resize、Crop 四大功能，共 44 个真实 Magick 解码/编码输出用例：

| 配置维度 | Compress | Convert | Resize | Crop | 主要断言 |
| --- | --- | --- | --- | --- | --- |
| 压缩模式 | ✓ | ✓ | ✓ | ✓ | 设置可完整往返；Compress 使用目标模式 |
| 自定义压缩质量 | ✓ | ✓ | ✓ | ✓ | 合法质量进入新 Compress 请求 |
| 公共元数据策略 | ✓ | ✓ | ✓ | ✓ | 四类真实输出按 Preserve/Remove 执行 |
| 转换格式 | ✓ | ✓ | ✓ | ✓ | Convert 扩展名和实际编码格式一致 |
| 转换质量 | ✓ | ✓ | ✓ | ✓ | JPEG/WebP 有损质量进入新 Convert 请求 |
| 透明铺底色 | ✓ | ✓ | ✓ | ✓ | 透明 PNG 转 JPEG 的角像素接近配置 RGB，不出现黑底 |
| 输出位置模式 | ✓ | ✓ | ✓ | ✓ | SameAsInput/Subfolder/CustomDirectory 解析正确 |
| 子目录名 | ✓ | ✓ | ✓ | ✓ | 四类输出进入配置子目录 |
| 自定义输出目录 | ✓ | ✓ | ✓ | ✓ | 四类输出进入指定绝对目录 |
| 文件名格式 | ✓ | ✓ | ✓ | ✓ | 输出基础名按 Token 展开 |
| 同名文件策略 | ✓ | ✓ | ✓ | ✓ | Skip 产生 Skipped，且不伪造输出 |

补充门禁：

- Desktop 四类编辑器分别验证：保存设置后切换走廊当前项只更新输入并保留当前 Draft；再次 `LoadAsync` 创建新草稿后读取新 Profile、`SameFormatEncodingPolicy` 与 `OutputPolicy`。
- Batch Compress、Batch Convert、Batch Resize 各使用 3 张真实 JPEG，验证批次共同使用最新保存的处理参数、输出目录和带 `{index}` 的命名策略；Crop 按产品范围仍只支持单张。
- 单张与批量各有 1 个并发快照用例：图片处理已启动并被测试闸门暂停时保存另一套设置，释放后活动请求及批量剩余项目仍全部写入旧快照目录；随后新任务写入新目录。
- 这些用例必须使用真实 `JsonAppSettingsStore`、`SaveSettingsWorkflow`、`LocalFileSystemService` 和 `MagickImageProcessor`，不能仅断言 ViewModel 字段或 Mock 调用。

## 55. 2026-08-26 自包含、压缩单文件、Trim 与 NativeAOT 发布基线

正式便携包通过 `eng/publish.ps1` 生成。脚本默认使用 `TrimmedSingleFile`，其语义固定为：

- `SelfContained=true`：发布包包含对应 RID 的 .NET Runtime，终端用户不需要安装 .NET Runtime，更不需要安装 .NET SDK。
- `PublishSingleFile=true` 与 `IncludeNativeLibrariesForSelfExtract=true`：托管程序集、运行时和本机库进入同一个应用入口文件；本机库由 .NET 单文件宿主按需解包。
- `EnableCompressionInSingleFile=true`：启用 Bundle 内部压缩；外层 ZIP/TAR 仍使用最优压缩并生成 SHA-256 旁车文件。
- `PublishTrimmed=true` 与 `TrimMode=partial`：正式单文件包启用保守的部分裁剪。不得把 `TrimMode=full` 直接用于发布，除非 AtomUI、Avalonia、ReactiveUI、COM 文件选择和完整 UI 回归均通过。
- Release 包不携带 PDB；`release-manifest.json` 必须明确记录 `selfContained`、`publishMode`、`singleFile`、`singleFileCompression`、`trimmed`、`trimMode` 与 `nativeAot`，不能仅靠文件名推断。

正式 Windows x64 本机构建命令：

```powershell
./eng/publish.ps1 -RuntimeIdentifier win-x64 -Version 0.1.0
```

发布模式如下：

| `PublishMode` | 自包含 | 单文件 | Bundle 压缩 | Trim | NativeAOT | 定位 |
| --- | --- | --- | --- | --- | --- | --- |
| `CompressedSingleFile` | 是 | 是 | 是 | 否 | 否 | 第三方裁剪回归时的保守回退 |
| `TrimmedSingleFile`（默认） | 是 | 是 | 是 | Partial | 否 | 正式便携发布基线 |
| `NativeAot` | 是 | 否 | 不适用 | Full（AOT 固有） | 是 | 实验产物，不覆盖正式单文件包 |

NativeAOT 实验命令：

```powershell
./eng/publish.ps1 -RuntimeIdentifier win-x64 -Version 0.1.0 -PublishMode NativeAot
```

2026-08-26 的 win-x64 实测结论：NativeAOT 已完成本机代码生成，入口进程隐藏启动 8 秒未提前退出；但 Avalonia/Skia、HarfBuzz 与 Magick.NET 仍需要独立本机动态库，因此 AOT 目录不是字面意义上的单文件。AOT 包输出到 `.artifacts/publish-nativeaot`，归档名带 `-nativeaot`，不得覆盖或冒充默认的压缩单文件包。只有在四大图片功能、文件/目录选择、设置持久化、AtomUI 全页面及跨平台原生 Runner 回归完成后，才能考虑将 AOT 提升为正式模式。

为保证裁剪后的真实可运行性，本轮已经完成以下代码收口：

- 设置与最近项目 JSON 改用 `System.Text.Json` 源生成元数据；原子替换写入接收显式 `JsonTypeInfo<T>`，不再依赖运行时反射。
- 本地 JSONL 日志改用 `Utf8JsonWriter` 写入受控标量，并保持 fail-open；日志异常不得再次遮蔽原始 UI 异常。
- AtomPix 主题 `StyleInclude` 移入编译期 AXAML；批量结果 Dialog 的开关同步改为显式属性同步，清除 AtomPix 自有代码中的 IL2026 警告。
- 默认 Trim 单文件发布后进程隐藏启动 8 秒未提前退出。win-x64 入口约 `38.90 MB`，归档约 `33.49 MB`；尺寸只作为本机基线，不作为跨平台固定阈值。

仍可见的 Trim 分析警告来自 Built-in COM、AtomUI.Core、Avalonia.DesignerSupport、DynamicData 与 ReactiveUI 依赖链。它们是维持 `TrimMode=partial`、不把 Full Trim/AOT 直接设为正式唯一产物的依据，不能通过全局 `NoWarn` 隐藏。

## 56. 2026-08-27 GitHub 五平台安装器与 Release 基线

本节取代第 51 节中“正式 Release 只生成三个平台便携归档”的旧口径；第 51 节的日常 CI、测试、压力与依赖审计门禁继续有效。

正式 GitHub 仓库为 `https://github.com/AtomUI/AtomPix`，程序集与安装器元数据必须使用该地址；应用 Bundle Identifier 固定为 `net.atomui.atompix`。正式发布只由 `v*` Tag 触发，工作流入口必须再次校验版本满足 `v<major>.<minor>.<patch>` 以及当前脚本支持的可选预发布或构建后缀。本阶段首个目标 Tag 为 `v0.1.0`。

`.github/workflows/ci.yml` 继续承担日常三平台质量门禁、Windows 压力测试和便携包验证，不得退化为 AtomBox 的较小测试集合。`.github/workflows/release.yml` 在完整 `eng/verify.ps1` 门禁通过后，按 AtomBox 已验证的 AtomUITools 安装器链路生成以下正式产物：

| RID | Runner | 正式产物 |
| --- | --- | --- |
| `win-x64` | `windows-latest` | 自包含、压缩单文件、Partial Trim ZIP |
| `osx-x64` | `macos-15-intel` | 已签名 DMG |
| `osx-arm64` | `macos-latest` | 已签名 DMG |
| `linux-x64` | `ubuntu-latest` | AppImage |
| `linux-arm64` | `ubuntu-24.04-arm` | AppImage |

五个平台都必须通过原生 Runner 的 restore、Release 测试、`TrimmedSingleFile` publish 和发布目录启动烟测。Windows 额外运行真实进程 UIA；macOS 在 DMG 挂载后对其中唯一 `.app` 执行 `codesign --verify --deep --strict`；Linux 使用 Xvfb 验证发布入口。每个正式包必须有同名 `.sha256`，Release 汇总 Job 只接受五个包和五个校验文件，数量不符时不得创建不完整 GitHub Release。

macOS/Linux 安装器使用公开仓库 `https://github.com/kusarparlly/AtomUITools`，并固定到经过 AtomPix 验证的完整提交 SHA，不跟随可变分支，也不再依赖 `ACCESS_TOKEN`。更新工具版本时必须显式修改 SHA 并重新执行五平台发布门禁。macOS 安装器按架构声明实际最低系统版本：`osx-arm64` 为 macOS 14.0，`osx-x64` 为 macOS 15.0；AtomUITools 必须在代码签名前将其写入 `Info.plist` 的 `LSMinimumSystemVersion`，发布流水线挂载 DMG 后必须校验该值与矩阵一致。macOS 强制签名并要求 `CERT_P12_BASE64`、`CERT_PASSWORD`、`KEYCHAIN_PASSWORD`、`CERT_IDENTITY`。任一凭据缺失、最低系统版本不符或签名验证失败都必须使发布失败，不允许自动降级为未签名 DMG。当前阶段不包含 Apple notarization、Windows 代码签名或 Linux 包签名。

安装器不得复用 AtomBox 品牌资源。三平台图标采用与 AtomBox 一致的组织和消费方式：Windows 的多分辨率 `AtomPix.ico` 保留在 Desktop `Assets/Branding` 中，并同时作为项目 `ApplicationIcon` 与主窗口 `Icon`；macOS 在 `assets/macos/AtomPix.icns` 提供受版本控制的标准 ICNS；Linux 在 `assets/linux/icons/hicolor/{size}x{size}/apps/atompix.png` 提供 16、32、48、64、128、256、512 七档桌面图标。`assets/source/AtomPix-1024.png` 是安装器图标的主源副本，`eng/generate-platform-icons.ps1` 只从现有 AtomPix 品牌资源可重复生成上述跨平台资产，不产生第二套视觉设计。发布 Runner 必须直接打包仓库内的 ICNS 和 hicolor 资源，不得在 CI 中临时缩放或转换，以避免平台产物与本地审阅资源漂移。`eng/publish.ps1` 与 `eng/smoke-test.ps1` 的正式 RID 集合统一为 `win-x64`、`osx-x64`、`osx-arm64`、`linux-x64`、`linux-arm64`，五个平台继续遵守第 55 节的 Partial Trim、manifest、无 PDB 和自包含约束。

仓库级 `NuGet.config` 只声明随仓库提交的 `eng/nuget-local` 与 `nuget.org` 两个正式包源。不得把用户目录下的全局包缓存配置成 `fallbackPackageFolders`：全新 GitHub Runner 上该目录可能尚不存在，而 NuGet 会把 fallback 目录当成必须已经存在的包源并以 `NU1301` 终止。全局包缓存的位置和创建由 NuGet 自身管理；CI 可通过临时 `NUGET_PACKAGES` 路径执行冷缓存恢复，以验证构建不依赖开发机缓存。

Headless UI 自动化不得以“动画时长加少量余量”的固定 `Task.Delay` 作为完成条件。GitHub macOS/Linux Runner 会并发执行多个测试程序集，调度延迟可能超过动画余量。涉及滚动、导航和异步布局的断言必须等待可观测的最终状态，并在等待期间推进 Avalonia UI 调度队列；超时只承担防止永久等待的职责，不作为正常同步机制。

