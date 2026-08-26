# AtomPix Headless 三轮测试分层

> 文档状态：历史 Headless 测试快照
>
> 基线时间：2026-06-26
>
> 范围：当前无 UI 阶段的 Core、Imaging.Abstractions、Infrastructure、Imaging.Magick、Workflows。

> 时间语义：本文记录 2026-06-26 首轮代码和测试事实，不是现行目标契约清单。后续冻结的独立 Resize/Crop、输出计划、资源保护、诊断和 Desktop 测试以 `testing-and-release.md` 第 32–46 节为准。

## 1. 分层原则

AtomPix headless 测试分三轮：

```text
Round 1: 单元测试
Round 2: 契约测试
Round 3: Headless 用户场景测试
```

Round 1 只验证最小单元、模型不变量、纯规则、构造边界和轻量 DI 注册边界。

Round 1 不验证：

- 真实文件系统读写。
- 真实 Magick.NET 图片处理。
- 跨模块真实组合链路。
- 用户完整流程。
- Avalonia / AtomUI / ViewModel。

## 2. Round 1 当前覆盖

### 2.1 Core

测试项目：

```text
tests/AtomPix.Core.Tests
```

覆盖：

- `OperationResult` / `OperationResult<T>` 成功失败不变量。
- `AtomPixError` message 和 details 防御性拷贝。
- `LocalPath` 非空和原始文本保留。
- `CompressionProfile`、`ImageQuality`、`ResizePolicy`、`MetadataPolicy`。
- `ConversionProfile`、`OutputImageFormat`。
- `OutputPolicy`、`OutputLocationPolicy`、`OutputNamingPolicy`、`OverwritePolicy`。
- `AppSettings`、`RecentItemsSettings`、`RecentItemsPolicy`。
- `ImageJob`、`BatchJob` 状态流转。
- `ImageJobResult`、`BatchResult`、`BatchProgressSnapshot`。

本轮新增收口：

- Core 策略对象拒绝非法枚举值。
- `Canceled` 任务结果必须携带取消错误。
- `BatchResult.TotalCount` 和 `CompletedCount` 分离的取消场景。

### 2.2 Imaging.Abstractions

测试项目：

```text
tests/AtomPix.Imaging.Abstractions.Tests
```

覆盖：

- `ImageProcessorCapabilities` 输入/输出格式声明不变量。
- 能力集合防御性拷贝。
- Probe / Preview / Compress / Convert 请求与结果模型。
- 当时 `ImageProcessingDetails` 的尺寸不变量；其中旧 `ResizeApplied` 不进入目标契约。
- `ImagePreviewResult` 输入和输出字节数组防御性拷贝。

本轮新增收口：

- capabilities 拒绝 null 格式集合。
- preview encoded bytes 属性返回副本，避免外部修改内部状态。

### 2.3 Infrastructure

测试项目：

```text
tests/AtomPix.Infrastructure.Tests
```

Round 1 只统计其中不依赖真实 IO 的单元测试，例如：

- `LocalFileSystemService.Combine` 路径段校验。
- `ChangeExtension`、`BuildIndexedPath` 字符串路径辅助。
- `AppPathProvider` 注入路径和默认目录名。
- DI 注册扩展 null 参数保护。

真实 JSON 文件读写、损坏文件、取消、临时文件清理属于 Round 2 契约测试。

### 2.4 Imaging.Magick

测试项目：

```text
tests/AtomPix.Imaging.Magick.Tests
```

Round 1 只统计其中不读写真实图片的单元测试，例如：

- `MagickImageProcessor.Capabilities` 第一阶段能力声明。
- DI 注册扩展 null 参数保护。

真实图片探测、预览、压缩、转换、损坏图片、多帧图片属于 Round 2 契约测试。

### 2.5 Workflows

测试项目：

```text
tests/AtomPix.Workflows.Tests
```

Round 1 只统计其中使用 fake 且不访问真实 IO/真实 Magick 的流程单元测试，例如：

- Workflow 构造函数 null 依赖保护。
- 请求 null 保护。
- 输出路径策略的纯编排行为。
- 图片处理失败、取消、错误透传的 fake 场景。
- DI 注册扩展 null 参数保护。

真实 Infrastructure + Magick 的 headless 链路属于 Round 3。

## 3. Round 1 当前验证命令

```text
dotnet test tests/AtomPix.Core.Tests/AtomPix.Core.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests/AtomPix.Imaging.Abstractions.Tests/AtomPix.Imaging.Abstractions.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests/AtomPix.Infrastructure.Tests/AtomPix.Infrastructure.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests/AtomPix.Imaging.Magick.Tests/AtomPix.Imaging.Magick.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test tests/AtomPix.Workflows.Tests/AtomPix.Workflows.Tests.csproj --no-restore /p:UseSharedCompilation=false
```

本轮验证结果：

```text
Core: 44
Imaging.Abstractions: 13
Infrastructure: 28
Imaging.Magick: 33
Workflows: 55
```

注意：Infrastructure、Imaging.Magick、Workflows 的项目测试总数中混有 Round 2/3 测试。后续如测试规模继续扩大，可按 Round 拆分测试类或测试项目。

## 4. Round 1 结论

当前结论：

```text
Round 1 单元测试基线通过。
```

下一步应进入 Round 2：契约测试审计与补缺口。
## 5. Round 2 当前补强记录

Round 2 目标：验证模块契约、实现端口行为和洋葱依赖边界。

本轮新增覆盖：

- Core 依赖边界：不得引用 Infrastructure、Workflows、Imaging、Avalonia、AtomUI、Magick.NET、SkiaSharp。
- Imaging.Abstractions 依赖边界：不得引用具体图片实现、Infrastructure、Workflows 或 UI 库。
- Workflows 依赖边界：不得引用 Infrastructure 实现、Imaging.Magick 或 UI 库。
- Infrastructure 契约：存储 Save payload null 保护、文件系统取消映射、空扩展名拒绝、无文件名 indexed path 拒绝。
- Imaging.Magick 契约：Preview / Compress / Convert 缺失文件统一映射为 `InputFileNotFound`；Probe / Preview / Compress / Convert 预取消统一映射为 `OperationCanceled`。
- Workflows 契约：Open / Preview 失败透传；设置保存失败透传；最近记录加载/保存失败透传。

本轮模块验证结果：

```text
Core: 45
Imaging.Abstractions: 14
Infrastructure: 32
Imaging.Magick: 36
Workflows: 62
Total: 189
```

Round 2 当前结论：模块契约补强测试通过；下一步可继续扩大 Round 2 的边界审计，或进入 Round 3 场景缺口补强。
## 6. Round 2 公共 API 契约审计记录

本轮聚焦 Core 和 Imaging.Abstractions 的公共 API 稳定性。

新增覆盖：

- Core public type surface 白名单，任何新增/删除/移动 public 类型都必须显式更新测试。
- Core public member 暴露类型扫描，防止 Core 成员签名泄漏 Infrastructure、Workflows、Imaging、Avalonia、AtomUI、Magick.NET 或 SkiaSharp 类型。
- Imaging.Abstractions public type surface 白名单。
- `IImageProcessor` 固定四个异步操作：Probe、Preview、Compress、Convert，并锁定 request、result、CancellationToken 签名。
- `ImageFormatKind` 枚举成员和顺序白名单。

本轮模块验证结果：

```text
Core: 47
Imaging.Abstractions: 17
```
## 7. Round 2 Infrastructure 真实文件系统契约硬化记录

本轮聚焦 Infrastructure 端口实现的真实文件系统行为。

新增覆盖：

- JSON 存储契约：settings、recent-items 保存后必须是可解析 JSON，并锁定当前顶层 schema 形状。
- 文件系统服务契约：`Combine` 拒绝空白文件名、绝对路径、跨平台分隔符和遍历段；`ChangeExtension` 支持无扩展名路径；`BuildIndexedPath` 固定无扩展名、多点文件名和大 index 行为。
- 路径提供器契约：注入路径必须原样保留为 `LocalPath`，构造 `AppPathProvider` 不隐式创建目录。
- 存储失败恢复：已有有效 settings/recent-items 文件时，目标文件被占用导致保存失败，旧文件内容必须保持不变，临时文件必须清理。
- 文件系统存在性查询：`FileExists` / `DirectoryExists` 对存在和不存在路径返回稳定结果。

当前 Infrastructure 验证结果：

```text
Infrastructure: 35
```
## 8. Round 2 Imaging.Magick 真实图片契约硬化记录

本轮聚焦 `AtomPix.Imaging.Magick` 对 `IImageProcessor` 的真实实现契约。

新增覆盖：

- 能力声明与真实行为一致：`Capabilities.SupportedInputFormats` 中声明的 JPEG、PNG、WebP、BMP、GIF、TIFF 都能真实 `Probe`；声明的 JPEG、PNG、WebP 输出都能真实 `Convert`。
- 多帧边界：animated GIF 转换明确返回 `UnsupportedInputFormat`，且不写出目标文件。
- 参数效果：JPEG 高质量压缩输出大于最大压缩输出；resize 继续验证真实输出尺寸。
- Metadata 策略：当前测试只确认带 EXIF 的 JPEG 在 `MetadataPolicy.Remove` 后移除 EXIF、在 `Preserve` 后保留 EXIF，并验证 result details；尚未覆盖 ICC 始终保留和 AutoOrient 后方向规范化。
- 透明度转换：当前测试确认 PNG alpha -> WebP 保留 alpha、PNG alpha -> JPEG 移除 alpha、WebP -> JPEG 输出无 alpha；它尚未规定或验证 JPEG 透明区域的确定性铺底颜色，后续按 `TransparencyPolicy` 目标契约补齐。
- 输出安全：压缩和转换可覆盖已有文件；转换写入失败返回 `ImageWriteFailed` 并清理临时输出文件。

当前 Imaging.Magick 验证结果：

```text
Imaging.Magick: 45
```
## 9. Round 2 Workflows 输出策略与批量矩阵硬化记录

本轮聚焦输出策略和 Workflows 编排契约。

新增覆盖：

- 输出策略组合：转换 AutoRename 连续递增到可用路径；压缩 CustomDirectory + KeepOriginalName 保留输入扩展；转换 Subfolder + KeepOriginalName 使用目标格式扩展；转换 Skip 不调用图片处理器。
- 批量统计一致性：批量压缩/转换混合成功、失败、跳过、取消时，`BatchResult` 和 `FinalProgress` 的 Total/Completed/Succeeded/Failed/Skipped/Canceled 计数一致。

当前目标验证结果：

```text
Core: 55
Workflows: 71
```
## 10. Round 3 第一阶段真实用户主路径记录

Round 3 第一阶段继续坚持无 UI：不启动 Avalonia，不创建窗口，不经过 ViewModel；使用真实 Infrastructure、真实 Magick.NET 图片处理和真实 Workflows 编排模拟用户动作。

本轮新增覆盖：

- 单张主路径：单张压缩和单张转换均可执行，并真实写出文件。
- 真实批量转换：多张输入批量转换为 WebP，所有成功项均真实落盘，并用 Magick 验证输出格式。
- 真实输出策略：`SameAsInput + KeepOriginalName` 将转换结果写到源图同目录；`CustomDirectory + AppendSuffix` 自动创建目录并写出结果。
- 动态样本增强：Round 3 场景生成真实 animated GIF 和带有效 alpha 像素的 PNG，减少“缺失文件假失败”对场景语义的干扰。

当前 Workflows 验证结果：

```text
Workflows: 76
```
## 11. Round 3 第二阶段真实异常与恢复场景记录

本轮聚焦真实异常、恢复能力和数据不破坏，仍然不经过 UI、ViewModel 或 Avalonia。

新增覆盖：

- 设置恢复：损坏 `settings.json` 会阻断默认设置驱动流程并返回 `SettingsLoadFailed`，不会覆盖原损坏文件；重新保存有效设置后流程恢复并真实写出文件。
- 最近记录恢复：损坏 `recent-items.json` 后，用户打开图片并写入最近记录可恢复文件；真实 store 下验证最近记录去重、排序和截断。
- 输出异常：转换目标路径为目录时，单项任务返回 `ImageWriteFailed`，且不残留临时输出文件。
- AutoRename 恢复：真实文件系统中连续存在 `_atompix`、`_atompix_1`、`_atompix_2` 时，自动命名选择 `_atompix_3` 并真实写出。

当前 Workflows 验证结果：

```text
Workflows: 82
```
## 12. Round 3 第三阶段可视化输出产物记录

本轮新增固定可视化输出产物测试，目的是保留真实压缩/转换后的图片，便于人工打开查看清晰度、透明度和格式转换效果。

固定输入目录：

```text
tests/TestAssets/Images/
```

固定输出目录：

```text
tests/TestOutputs/Images/
```

新增输出文件：

```text
compressed-balanced.jpg
compressed-maximum.jpg
resized-compressed.jpg
converted-png-alpha-to-webp.webp
converted-png-alpha-to-jpeg.jpg
converted-webp-to-jpeg.jpg
converted-jpeg-to-png.png
```

规则：

- 输入样本缺失时由 `VisualOutputArtifactTests` 生成并保留。
- 输出图片每次运行测试时覆盖写入，但不会删除输出目录。
- 输出目录中的 `README.md` 记录每个输出文件的来源和处理参数。
- 这些产物用于人工观察，不替代自动断言；测试仍会验证输出文件存在、格式正确、尺寸或体积关系符合预期。

当前 Workflows 验证结果：

```text
Workflows: 83
```
## 13. Round 3 第四阶段 Headless 验收收口记录

本轮新增真实 DI 组合验收：默认转换、默认压缩、批量压缩、批量转换和最近记录写入。

发布验证回归：

```text
Build: passed, 0 warning, 0 error
Test: 237 passed
Publish Workflows win-x64 self-contained: passed
Publish Imaging.Magick win-x64 self-contained: passed
```

阶段性结论：底层/headless 可以进入 Desktop / UI 实现阶段；正式发布前仍需补 Desktop 产物、NativeAOT 实验、真实用户图片集、大图性能和跨平台权限。

