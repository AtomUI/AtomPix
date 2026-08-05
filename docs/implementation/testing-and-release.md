# AtomPix 测试与发布策略

> 文档状态：测试策略讨论基线
>
> 基线时间：2026-06-26
>
> 基线范围：当前文档定义 UI 前置实现阶段的 headless 测试策略、测试轮次、测试项目规划和第一阶段发布验证策略
>
> 变更规则：调整测试分层、测试轮次、发布验证口径时，应先更新本文档。

## 1. 总原则

AtomPix 第一阶段采用 UI 最后策略。

这里的 UI 最后不是指不重视 UI，而是指：

```text
所有底层模块充分实现并通过 headless 测试后，再展开 Desktop / UI 层的规划、原型、实现和测试。
```

原因：

- UI 层的规划、原型图和交互取舍强依赖用户确认。
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
SubscriptionState / FeatureAccessPolicy
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
  -> Infrastructure 契约
  -> Imaging.Abstractions 契约
  -> Imaging.Magick 契约
  -> Workflows 契约
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
LocalSubscriptionStore
JsonRecentItemsStore
LocalFileSystemService
AppPathProvider
```

行为：

```text
settings.json 不存在 -> 默认设置
settings.json 损坏 -> Failure
subscription.json 不存在 -> Free
subscription.json 损坏 -> Failure
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
PNG alpha -> HasAlpha = true
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
FakeSubscriptionStore
FakeRecentItemsStore
```

覆盖：

```text
OpenImageWorkflow 调用 Probe
CreatePreviewWorkflow 调用 CreatePreview
CompressImageWorkflow 检查 FeatureId.SingleCompress
ConvertImageWorkflow 检查 FeatureId.SingleConvert
BatchCompressWorkflow 检查 FeatureId.BatchCompress
BatchConvertWorkflow 检查 FeatureId.BatchConvert
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
免费用户尝试批量压缩，被 FeatureAccessPolicy 拦截
订阅有效用户执行批量压缩，全部功能可用
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
免费/订阅功能访问判断集中生效
设置损坏不偷偷覆盖
订阅损坏不静默降级
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

- Core 覆盖结果模型、错误模型、压缩/转换/输出策略、任务状态、订阅功能访问、设置 schema 和路径值对象。
- Imaging.Abstractions 覆盖图片处理请求、结果和能力声明的不变量。
- Infrastructure 覆盖 JSON 存储、订阅状态、最近记录、原子写入、取消语义和文件系统路径辅助。
- Imaging.Magick 覆盖真实图片探测、预览、压缩、转换、多帧拒绝、取消和非法输出格式。
- Workflows 覆盖功能访问检查、输出路径策略、覆盖策略、批量部分成功、真实 headless 用户场景和设置流程。

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
- `CompressWithDefaultSettingsWorkflow` 使用保存的默认压缩配置完成 resize 压缩。
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

- Abstractions 验证 `ImageProcessingDetails` 的宽高不变量。
- Magick 压缩结果返回真实输入/输出尺寸、resize 标记、metadata 移除标记和有损输出标记。
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
- Magick 验证只有文件名的输出路径可写出，且缺失输出目录可创建。

## 19. 设置与订阅存储增量验收

本轮新增覆盖：

- `settings.json` 高于当前 schema version 时返回 `SettingsLoadFailed`。
- 损坏 `settings.json` 加载失败后不会被默认设置覆盖。
- 设置保存失败时清理同目录临时文件。
- 非法 `subscription.json` 返回 `SubscriptionLoadFailed`，不静默降级 Free。
- 损坏 `recent-items.json` 读取为空列表成功，后续保存可恢复为正常文件。
- 默认设置驱动的压缩/批量转换流程在设置加载失败时不会进入图片处理。

## 20. 取消、统计与 DI 装配增量验收

本轮新增覆盖：

- Core 验证取消任务必须携带取消错误。
- Core 验证 `BatchResult.TotalCount` 可大于 `CompletedCount`，用于批量中途取消。
- Core 验证批量进度在取消后可显示未完成状态。
- Core 验证批量总节省比例。
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

进入 UI 原型阶段前置结论：

```text
底层/headless 已具备进入 Desktop / UI 原型阶段的基础条件；发布商业版本前仍需继续补真实跨平台、权限、大图、包体和商业订阅验证。
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
- Workflows 增加 Probe / Preview / Settings / RecentItems / Subscription 失败透传测试，确保流程层不吞错、不绕过功能访问检查、不在前置失败后继续处理图片。

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
- settings/subscription/recent-items 在保存失败时保留旧文件并清理临时文件。
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
- Metadata Remove / Preserve 使用真实 EXIF 样本验证。
- PNG/WebP/JPEG 透明度转换行为固定。
- 已存在输出文件可覆盖；转换写入失败会清理临时输出文件。

目标测试结果：

```text
Imaging.Magick: 45 passed
```
## 27. Round 2 Workflows 输出策略与功能访问矩阵硬化记录

本轮新增 Core / Workflows 契约测试：

- 免费、过期、有效订阅的功能访问矩阵。
- Workflows 层对单张免费功能、批量付费功能、订阅过期和订阅加载失败的访问检查。
- 输出策略组合覆盖 AutoRename 连续递增、CustomDirectory、Subfolder、KeepOriginalName、AppendSuffix、Skip。
- 批量压缩/转换混合成功、失败、跳过、取消时，`BatchResult` 与 `FinalProgress` 统计一致。

目标测试结果：

```text
Core: 55 passed
Workflows: 71 passed
```
## 28. Round 3 第一阶段真实用户主路径与权益流程记录

本轮新增 Headless 用户场景测试：

- 免费用户单张压缩/转换真实可用。
- 过期用户批量转换真实返回 `SubscriptionExpired`。
- 有效订阅批量转换多图真实写出 WebP，并用 Magick 验证格式。
- `SameAsInput` 和 `CustomDirectory` 输出策略在真实文件系统中落盘。
- Headless 动态样本补充 animated GIF 和非空 alpha PNG。

目标测试结果：

```text
Workflows: 76 passed
```
## 29. Round 3 第二阶段真实异常与恢复场景记录

本轮新增 Headless 异常恢复场景测试：

- 损坏 settings 阻断默认设置流程，不覆盖损坏文件；修复后恢复。
- 损坏 subscription 阻断批量流程，不降级免费；修复后恢复。
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
- 免费用户批量拦截。
- 有效订阅批量放行。
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

