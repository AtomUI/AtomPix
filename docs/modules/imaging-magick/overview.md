# AtomPix.Imaging.Magick 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Imaging.Magick` 是 `AtomPix.Imaging.Abstractions` 的 Magick.NET 实现。

它负责把 AtomPix 的图片处理契约转换为 Magick.NET 调用，并把 Magick.NET 的结果、异常和格式语义转换回 AtomPix 的统一模型。

## 2. 允许包含

- `IImageProcessor` 的 Magick.NET 实现。
- Magick.NET 格式映射。
- Magick.NET 异常到统一错误模型的转换。
- 图片读取、缩放、裁剪、旋转、EXIF 方向处理。
- JPEG / PNG / WebP 等格式转换和质量压缩。
- 拍摄、位置和描述性元数据的保留或移除，以及 ICC 色彩配置的独立保留。
- 面向 DI 的服务注册扩展。

## 3. 禁止包含

- Avalonia、AtomUI、ViewModel 或 UI 状态。
- 输出命名策略和用户流程编排。
- 配置文件保存。
- 把 `ImageMagick` 类型暴露给 Workflows、Desktop 或 Abstractions。

## 4. 推荐目录

```text
src/AtomPix.Imaging.Magick/
  AtomPix.Imaging.Magick.csproj
  Processing/
  Mapping/
  DependencyInjection/
```

## 5. 首批实现

- `MagickImageProcessor`
- `MagickFormatMapper`
- `MagickMetadataMapper`
- `MagickExceptionMapper`
- `MagickImageProcessorOptions`
- `MagickServiceCollectionExtensions`

## 6. 设计约束

- 所有公共 API 使用 `AtomPix.Imaging.Abstractions` 类型。
- Magick.NET 的原生异常不能直接穿透到 Workflows 或 Desktop。
- 默认不开放 PDF、EPS、PS、视频等需要 Ghostscript 或 FFmpeg 的能力。
- 第一阶段优先支持主流本地图片格式：JPEG、PNG、WebP、GIF、BMP、TIFF。
- 任何新增格式都应先更新 `Imaging.Abstractions` 中的能力声明。

## 7. 依赖规则

```text
AtomPix.Imaging.Magick
  -> AtomPix.Imaging.Abstractions
  -> AtomPix.Core
  -> Magick.NET
```

本模块不依赖 `AtomPix.Desktop`、`AtomPix.Workflows` 或 `AtomPix.Infrastructure`。
## 8. Magick.NET 实现基线

`AtomPix.Imaging.Magick` 是 `AtomPix.Imaging.Abstractions` 的第一阶段实现。

本模块负责把 `IImageProcessor` 契约转换为 Magick.NET 调用，并把 Magick.NET 的结果、格式和异常转换回 AtomPix 的统一模型。

### 8.1 NuGet 包选择

第一阶段开发期优先使用：

```text
Magick.NET-Q8-AnyCPU
```

选择理由：

- 开发阶段集成简单。
- 功能验证成本较低。
- 后续发布阶段再根据安装包体积和平台分发策略评估是否切换为平台特定包。

发布优化时可评估：

```text
Magick.NET-Q8-x64
Magick.NET-Q8-arm64
```

### 8.2 支持格式范围

第一阶段输入格式：

```text
JPEG
PNG
WebP
BMP
GIF
TIFF
```

第一阶段输出格式：

```text
JPEG
PNG
WebP
```

第一阶段明确不开放：

```text
PDF
EPS
PS
Video
PSD
AI
HEIC
AVIF
```

说明：

- PDF / EPS / PS 通常牵涉 Ghostscript，不纳入第一阶段。
- 视频格式通常牵涉 FFmpeg，不纳入第一阶段。
- HEIC / AVIF 后续单独评估平台依赖、授权、包体和稳定性。

### 8.3 ProbeAsync 实现策略

`ProbeAsync` 用于读取图片基础信息。

流程：

```text
1. 校验输入文件存在。
2. 使用 MagickImage 或 MagickImageCollection 读取图片信息。
3. 映射 Magick.NET 格式到 ImageFormatKind。
4. 读取宽高。
5. 分别判断 HasAlphaChannel 和 HasTransparency；后者必须检查是否存在真实非不透明像素，不能只读取通道标记。
6. 判断 IsAnimated / FrameCount。
7. 分别判断 HasMetadata 与 HasColorProfile；ICC / ICM 只计入后者。
8. 返回 ImageProbeResult。
```

多帧策略：

- GIF / WebP / TIFF 等多帧输入，`FrameCount > 1` 时视为 `IsAnimated = true`。
- 第一阶段只识别多帧，不承诺完整处理多帧压缩或转换。

### 8.4 CreatePreviewAsync 实现策略

`CreatePreviewAsync` 用于生成 Desktop 可显示的预览数据。

流程：

```text
1. 读取图片第一帧。
2. 自动处理 EXIF 方向。
3. 按 MaxPixelSize 缩放，保持比例。
4. 根据透明通道选择预览编码格式。
5. 返回 ImagePreviewResult。
```

预览输出策略：

```text
存在真实透明像素 -> PNG
不存在真实透明像素 -> JPEG
```

约束：

- 不返回 Avalonia Bitmap。
- 只返回 `EncodedBytes`、`MimeType`、`Width`、`Height`。
- 多帧或动画图片第一阶段只预览第一帧。

### 8.5 CompressAsync 实现策略

`CompressAsync` 实现单张图片压缩。

流程：

```text
1. 读取图片。
2. 自动处理 EXIF 方向。
3. 多帧输入按第一阶段策略拒绝处理。
4. 应用 MetadataPolicy：按策略保留或删除拍摄、位置和描述性信息，但保留 ICC；AutoOrient 后规范化 Orientation。
5. 校验输入属于 JPEG / PNG / WebP，输出路径扩展名与探测格式一致，并固定使用输入格式编码。
6. 根据 CompressionMode / ImageQuality 生成一个或多个临时候选。
7. 按固定模式或 Smart 候选规则选择最终结果，并以安全临时文件策略提交到 OutputPath。
8. 返回输入、输出文件大小和实际采用的 AppliedQuality。
```

压缩质量映射：

| 模式 | 质量 |
| --- | --- |
| `HighQuality` | `90` |
| `Balanced` | `80` |
| `Maximum` | `65` |
| `Custom` | 用户指定 |
| `Smart` | AtomPix 内置候选策略，用户不可配置 |

`Smart` 确定性策略：

```text
JPEG -> 82, 77, 72, 67, 65
WebP -> 80, 75, 70, 65
PNG -> 保持无损优化，不做激进颜色量化
```

- JPEG/WebP 先编码序列中的第一个质量；候选小于原图时立即采用，不再降低质量。
- 候选未小于原图时继续按序列重试，质量下限固定为 `65`。Smart 不允许由请求或设置覆盖序列、步长与下限。
- 到达下限仍没有更小结果时，选择所有有效候选中体积最小者并正常保存；压缩成功不以“必须变小”为前提。
- `HighQuality`、`Balanced`、`Maximum` 和 `Custom` 只按各自单一质量编码，不得为了追求更小体积在后台降低用户选择的质量；该有效结果即使不小于原图也必须保存。
- 所有候选必须应用相同的 MetadataPolicy、方向规范化和 ICC 保留规则；中间候选只能写入临时文件，未采用候选必须清理。
- 成功结果的 `AppliedQuality` 必须等于最终采用候选的质量。PNG 等无损结果返回 `null`。

PNG 策略：

- 第一阶段不做激进颜色量化，避免明显损伤。
- 可应用元数据移除和基础无损优化。
- 如果用户希望显著减小 PNG，推荐通过转换为 WebP 实现。
- Compress 不根据输出路径扩展名选择另一种格式，也不执行隐式格式降级；任何扩展名与输入格式不一致的请求返回 `UnsupportedOutputFormat`。

### 8.6 ConvertAsync 实现策略

`ConvertAsync` 实现单张图片格式转换。

流程：

```text
1. 读取图片。
2. 自动处理 EXIF 方向。
3. 多帧输入按第一阶段策略拒绝处理。
4. 判断是否存在真实透明像素以及目标格式是否支持 Alpha。
5. 真实透明且目标不支持 Alpha 时，将 TransparencyPolicy 的 sRGB 背景色转换到正确工作颜色空间后合成并移除 Alpha。
6. 应用 MetadataPolicy：按策略保留或删除拍摄、位置和描述性信息，但保留 ICC；AutoOrient 后规范化 Orientation。
7. 设置 Profile.OutputFormat 对应的目标格式。
8. 如果目标格式支持质量参数，应用 Quality。
9. 写入 OutputPath。
10. 返回输入格式、输出格式、前后文件大小和实际 TransparencyProcessingResult。
```

透明区域执行规则：

- PNG / WebP 输出保留真实透明像素并返回 `Preserved`；第一阶段不主动铺底这些格式。
- JPEG 输出在源图真实透明时必须显式合成 `OpaqueBackgroundColor` 并返回 `Flattened + 实际颜色`，不能依赖 `image.Format = JPEG` 的隐式默认行为。
- 源图没有真实透明像素时返回 `NotPresent`，即使它带有完全不透明的 Alpha 通道。
- 背景色是 Core 定义的 sRGB `RgbColor`。实现必须考虑源图 ICC / colorspace，不能把 RGB 字节直接解释为任意源颜色空间。
- Alpha 合成必须发生在描述性元数据清理之前，并始终利用可用 ICC 完成颜色空间感知的合成。

输出格式映射：

```text
OutputImageFormat.Jpeg -> MagickFormat.Jpeg
OutputImageFormat.Png  -> MagickFormat.Png
OutputImageFormat.WebP -> MagickFormat.WebP
```

### 8.7 ResizeAsync 实现策略

`ResizeAsync` 只执行 `ImageResizeRequest` 中已经确定的 `TargetSize`，不解释高层 Resize 规则。

流程：

```text
1. 读取图片并识别真实输入格式。
2. 自动处理 EXIF 方向，得到逻辑方向下的 InputSize。
3. 拒绝动画或多帧输入。
4. 校验输入格式属于 Capabilities.Resize.SupportedSameFormatFormats。
5. 校验 TargetSize 未超过 MaxWidth、MaxHeight 和 MaxPixelCount。
6. 将完整图片严格缩放为 TargetSize；不裁剪、不补边，也不二次保持比例。
7. 应用 SameFormatEncodingPolicy：有损格式使用 LossyQuality，并按 MetadataPolicy 保留或移除元数据。
8. 使用输入格式写入 Workflow 已解析好的 OutputPath；不得根据扩展名转换为另一种格式。
9. 重新读取或从可靠的写出结果取得实际逻辑尺寸，并验证 OutputSize == TargetSize。
10. 返回格式、输入/输出逻辑尺寸和前后文件大小。
```

编码规则：

- 第一阶段 `Capabilities.Resize.SupportedSameFormatFormats` 固定为 JPEG、PNG、WebP、BMP，其中 WebP 仅接受单帧输入；GIF、多帧 WebP 和 TIFF 不进入本轮 Resize 范围。
- JPEG、WebP 等有损输出使用请求显式携带的 `LossyQuality`；公共默认值为 `90`，但 Magick 实现不得自行读取设置或硬编码默认设置来源。
- PNG、BMP 等无损输出忽略 `LossyQuality`，但仍执行 `MetadataPolicy`。
- Resize 始终保持输入格式；需要改变格式时应建立独立 Convert 任务。
- 写出仍采用本模块统一的安全临时文件与最终替换策略，失败不得破坏已有目标文件。

若实际输出尺寸与目标尺寸不一致，必须返回 `ImageResizeFailed`，不能把偏差结果包装为成功。

### 8.8 CropAsync 实现策略

`CropAsync` 只执行 `ImageCropRequest` 中已经确定的 `CropArea`，不解释比例、选框或批量居中策略。

流程：

```text
1. 读取图片并识别真实输入格式。
2. 自动处理 EXIF 方向，得到逻辑方向下的 InputSize。
3. 拒绝动画或多帧输入。
4. 校验输入格式属于 Capabilities.Crop.SupportedSameFormatFormats。
5. 校验 InputSize 未超过 MaxInputWidth、MaxInputHeight 和 MaxInputPixelCount。
6. 校验 CropArea 完整位于 InputSize 内。
7. 严格提取 CropArea；不 Resize、不补边，也不移动或重算选区。
8. 应用 SameFormatEncodingPolicy，并使用输入格式写入 Workflow 已解析好的 OutputPath。
9. 重新读取或从可靠的写出结果取得实际逻辑尺寸，并验证 OutputSize == CropArea.Width × CropArea.Height。
10. 返回格式、输入/输出逻辑尺寸和前后文件大小。
```

第一阶段 `Capabilities.Crop.SupportedSameFormatFormats` 固定为 JPEG、PNG、WebP、BMP，其中 WebP 仅接受单帧输入。GIF、多帧 WebP 和 TIFF 不进入本轮 Crop 处理范围。

JPEG 和单帧 WebP 使用请求中的 `LossyQuality`；PNG、BMP 忽略质量值。所有格式仍按请求中的 `MetadataPolicy` 处理元数据，并沿用同目录临时文件安全写入策略。

若实际输出格式改变、实际尺寸与选区宽高不一致，或任务接受后源图片变化导致原选区无法执行，必须返回 `ImageCropFailed`，不能把偏差结果包装为成功。

### 8.9 动画和多帧处理策略

第一阶段策略：

```text
Probe:
  识别多帧和动画。

Preview:
  只显示第一帧。

Compress / Convert / Resize / Crop:
  默认拒绝多帧输入。
```

原因：

- 多帧 GIF / WebP / TIFF 的压缩和转换语义更复杂。
- 第一阶段优先保证单帧主流图片处理稳定。
- 后续如需支持动画，应单独设计多帧任务模型、预览和输出策略。

### 8.10 异常映射

Magick.NET 异常不得穿透到 Workflows 或 Desktop。

映射建议：

| 场景 | AtomPixErrorCode | Category |
| --- | --- | --- |
| 读取失败 | `ImageReadFailed` | `ImageProcessing` |
| 写入失败 | `ImageWriteFailed` | `FileSystem` 或 `Permission` |
| 压缩失败 | `ImageCompressFailed` | `ImageProcessing` |
| 转换失败 | `ImageConvertFailed` | `ImageProcessing` |
| 调整尺寸失败 | `ImageResizeFailed` | `ImageProcessing` |
| 裁剪失败 | `ImageCropFailed` | `ImageProcessing` |
| 预览失败 | `ImagePreviewFailed` | `ImageProcessing` |
| 输入格式不支持 | `UnsupportedInputFormat` | `UnsupportedFormat` |
| 输出格式不支持 | `UnsupportedOutputFormat` | `UnsupportedFormat` |
| 用户取消 | `OperationCanceled` | `Cancellation` |
| 未预期异常 | `Unknown` | `Unexpected` |

实现层应保留必要诊断信息到 `AtomPixError.Details`，但不得暴露 Magick.NET 对象实例。

### 8.11 设计边界

- 本模块只实现 `AtomPix.Imaging.Abstractions`。
- 本模块不依赖 Desktop、Workflows 或 Infrastructure。
- 本模块不处理输出命名、覆盖策略或批量任务状态。
- Workflows 传入的 `OutputPath` 已经是策略决策后的最终路径。
- 本模块负责把图片内容写入该路径。
## 9. Imaging.Magick 工业级硬化基线

`AtomPix.Imaging.Magick` 是图片契约的具体实现层。它必须严格服从 `AtomPix.Imaging.Abstractions` 的能力声明，不能因为 Magick.NET 能做更多事情就擅自扩大 AtomPix 第一阶段能力边界。

### 9.1 请求边界

公共方法必须满足：

- null 请求属于调用方编程错误，直接抛出 `ArgumentNullException`。
- 用户可预期失败必须返回 `OperationResult<T>.Failure`。
- 已取消的 `CancellationToken` 必须映射为 `OperationCanceled`，不能映射为图片读取、压缩、转换、调整尺寸或裁剪失败。

### 9.2 输入格式边界

读取成功后，必须把 Magick.NET 格式映射为 `ImageFormatKind`，并检查是否包含在 `Capabilities.SupportedInputFormats` 中。

- 无法识别或未声明支持的输入格式返回 `UnsupportedInputFormat`。
- 多帧图片第一阶段仍只允许 `Probe` 和第一帧 `Preview`，`Compress` / `Convert` / `Resize` / `Crop` 返回 `UnsupportedInputFormat`。

### 9.3 输出格式边界

`Capabilities.SupportedOutputFormats` 是实现层对外承诺的输出格式集合。第一阶段仅支持：

```text
JPEG
PNG
WebP
```

压缩输出格式解析规则：

- 如果 `OutputPath` 带扩展名，则扩展名必须映射为 JPEG / PNG / WebP。
- 如果扩展名不受支持，返回 `UnsupportedOutputFormat`，不能回退到输入格式。
- 如果 `OutputPath` 不带扩展名，才允许根据输入格式回退；但回退结果仍必须是 JPEG / PNG / WebP。

转换输出格式解析规则：

- `ConversionProfile.OutputFormat` 必须映射到 JPEG / PNG / WebP。
- 非法枚举值或未声明支持的目标格式返回 `UnsupportedOutputFormat`。
- 输出 JPEG 且输入具有真实透明像素时必须执行显式铺底；输出 PNG / WebP 时不得因 `TransparencyPolicy` 存在而移除 Alpha。

Resize 输出格式解析规则：

- 输入格式必须包含在 `Capabilities.Resize.SupportedSameFormatFormats` 中；第一阶段为 JPEG、PNG、WebP、BMP。
- WebP 必须是单帧；GIF、TIFF 和所有多帧输入不进入本轮 Resize 范围。
- `OutputPath` 扩展名必须与输入格式一致；不一致时返回 `UnsupportedOutputFormat`，不得按扩展名偷偷转换。
- 编码质量与元数据处理只取自请求中的 `SameFormatEncodingPolicy`。

Crop 输出格式解析规则：

- 输入格式必须包含在 `Capabilities.Crop.SupportedSameFormatFormats` 中；第一阶段为 JPEG、PNG、WebP、BMP。
- WebP 必须是单帧；GIF、TIFF 和所有多帧输入不进入本轮 Crop 范围。
- `OutputPath` 扩展名必须与输入格式一致；不一致时返回 `UnsupportedOutputFormat`。
- 编码质量与元数据处理只取自请求中的 `SameFormatEncodingPolicy`。

### 9.4 写入路径边界

Workflows 传入的 `OutputPath` 已经是最终路径。Magick 实现只负责：

- 验证最终路径所在目录已经存在并可写。
- 在该目录内创建安全临时文件，完成编码后提交到最终路径。
- 不处理覆盖、跳过、自动重命名或输出命名策略。

业务输出目录由 Workflows 在创建 Job 前通过 `IFileSystemService` 准备。若目录在任务接受后被删除或变得不可写，Magick 返回结构化写入/目录失败，不自行重建业务目录。

### 9.5 异常映射

Magick.NET、IO、权限、路径参数和平台不支持异常必须被捕获并转换为 AtomPix 错误模型，不得让 `ImageMagick` 异常穿透到 Workflows 或 Desktop。

### 9.6 资源保护与按需分配

第一阶段 `MagickImageProcessorOptions` 和 `Capabilities.Resources` 使用同一组默认硬边界：

```text
MaxInputFileSizeBytes = 512 MiB
MaxInputWidth / MaxInputHeight = 32768
MaxInputPixelCount = 128000000
MaxOutputWidth / MaxOutputHeight = 32768
MaxOutputPixelCount = 128000000
```

正式读取顺序：

```text
1. 检查文件存在性和实际字节数。
2. 使用 Magick 的轻量 Ping / 头信息探测取得格式、宽高和帧信息，不先完整解码像素。
3. 使用 checked long 计算 Width × Height，并校验公共资源能力。
4. Resize 额外校验已解析 TargetSize；Crop 额外校验操作专用输入能力。
5. 全部通过后才完整读取、处理并写出。
```

Magick 进程级运行上限在 Desktop/Headless 组合根启动时设置一次，默认值为：

```text
Memory = 512 MiB
Map = 1 GiB
Disk = 4 GiB
Thread = min(4, Environment.ProcessorCount)，且至少为 1
Pixel cache directory = IAppPathProvider.TempDirectory 下的 AtomPix 私有目录
```

这些值都是最大允许量，不是预分配指令：设置 `Memory` 不会立即申请 `512 MiB`，设置 `Map` 不会立即映射 `1 GiB`，设置 `Disk` 不会创建 `4 GiB` 文件，Thread 也不会预先常驻创建四个线程。除建立一个空的私有临时目录和加载图片引擎自身运行时外，没有任务时不应产生与这些上限等量的资源占用。实际像素缓存按任务需要申请，图片对象和缓存必须在任务结束、失败或取消后释放。

第一阶段不设置强制处理时间上限。普通 `CancellationToken` 仍用于用户取消，但不得宣称能够强制终止正在执行的所有原生编码；如后续需要可靠硬超时，应把图片处理隔离到可终止的工作进程中另行设计。

资源错误映射：

- 文件体积超限返回 `InputFileTooLarge`，不完整解码。
- 输入或输出尺寸超限返回 `ImageDimensionsExceedLimit`。
- Magick 内存、map 或像素缓存资源异常返回 `ImageResourceLimitExceeded`，并在 `Details.ResourceKind` 说明实际类型。
- 输出卷或私有像素缓存目录空间不足返回 `InsufficientDiskSpace`。
- 所有失败必须释放图片对象并清理未提交候选、输出临时文件和私有像素缓存；清理失败不能覆盖原始资源错误。

ImageMagick 的 `area` 限制用于决定像素缓存是否转移到磁盘，不代替 AtomPix 的硬像素数拒绝。资源配置属于 `Imaging.Magick` 运行参数，不进入 `AppSettings`，也不在第一阶段设置页面开放。

实现时以 ImageMagick 官方 [Resources](https://imagemagick.org/resources/) 和 [Security Policy](https://imagemagick.org/security-policy/) 的最大资源限制语义为依据。AtomPix 将这些限制解释为运行上限而非资源预留；硬文件/像素拒绝仍由自己的能力契约保证。

### 9.7 测试要求

`AtomPix.Imaging.Magick.Tests` 至少覆盖：

- 主流输入格式探测。
- PNG 真实透明预览与 JPEG 预览 MIME，并区分完全不透明 Alpha 通道。
- JPEG 压缩、PNG/WebP/JPEG 转换，以及透明度保留或显式铺底结果。
- Custom 对质量 `1`、`100` 和常用中间值只编码指定质量；Smart 按候选序列停止、触底和选择最小有效候选，并准确返回 `AppliedQuality`。
- JPEG/PNG/单帧 WebP/BMP 同格式 Resize，输出格式不变且尺寸严格等于目标尺寸。
- Resize 的有损质量与元数据策略，以及 PNG 对质量字段的忽略行为。
- `MetadataPolicy.Preserve / Remove` 对 EXIF/GPS/IPTC/XMP 的选择性处理、ICC/ICM 独立保留，以及 AutoOrient 后 Orientation 的删除或 TopLeft 规范化。
- JPEG/PNG/单帧 WebP/BMP 同格式 Crop，输出尺寸严格等于矩形宽高。
- 非法或越界矩形、实际输出尺寸偏差及任务接受后源尺寸变化映射为 `ImageCropFailed`。
- 多帧输入拒绝压缩、转换、Resize 和 Crop。
- 缺失文件、取消 token、非法输出格式和 null 请求。
- 资源边界前一像素/一字节成功、边界后一像素/一字节拒绝，以及超大乘积无整数溢出。
- 配置资源上限后没有任务时不预建大文件、不占用等量内存；任务结束后私有缓存可清理。
## 10. 压缩效果验收补充

第一阶段不能只验证“能写出文件”，还必须验证最低限度的处理效果：

- JPEG 使用 `Maximum` 压缩时，复杂样本输出体积应小于输入体积。
- 压缩不应改变图片尺寸；调整尺寸由独立 `ResizeAsync` 执行。
- PNG alpha 转 WebP 后应保持尺寸，并写出真实 WebP 格式。
- 透明 PNG 转 JPEG 必须验证全透明和半透明区域按请求颜色合成，不能只断言输出不再具有 Alpha。

这些测试不是最终图像质量评价体系，只是防止压缩/转换实现退化为无意义写出。
## 11. Resize 行为验收补充

Core 负责把 `ResizePolicy + ImageSize` 统一解析为 `ResolvedResizeSize`。Magick 的独立 `ResizeAsync` 只执行已经确定的目标 width 和 height，不再解释保持比例、双边约束或百分比：

- 输出应严格写成 `ResolvedResizeSize.Width × ResolvedResizeSize.Height`。
- 允许目标宽高改变原图比例。
- 不得再次按原图比例改写目标尺寸。
- 不得隐式裁剪或补边。
- 必须保持输入格式；JPEG/WebP 等有损格式使用请求中的质量，PNG 等无损格式忽略质量。
- 必须按请求中的 `MetadataPolicy` 处理元数据，Resize 页面本身不参与该决策。
- `ImageResizeResult.OutputSize` 必须记录实际逻辑尺寸，并严格等于请求目标尺寸。
- 压缩和转换始终不应改变尺寸。

目标测试至少覆盖：

- Core：原图 120x80，Pixels(60, null, 保持比例) -> 60x40。
- Core：原图 120x80，Pixels(null, 30, 保持比例) -> 45x30。
- Core：原图 120x80，Pixels(60, 30, 保持比例) -> 45x30。
- Core：原图 120x80，Pixels(60, 30, 不保持比例) -> 60x30。
- Core：原图 120x80，Percentage(50) -> 60x40。
- Core：原图 120x80，Percentage(12.5) -> 15x10。
- Imaging：任意已解析的 60x30 目标尺寸均严格输出 60x30，不重新应用比例逻辑。

当前代码已使用 Core 的封闭 `ResizePolicy`、`ImageSize` 和 `ResolvedResizeSize`，Magick 只执行上层解析完成的精确目标尺寸；像素与小数百分比契约均有测试覆盖。

## 12. Crop 行为验收补充

Magick 实现裁剪时必须：

- 使用原图自动方向处理后的坐标系解释 `X/Y/Width/Height`。
- 校验裁剪矩形完整位于图片范围内。
- 只提取指定矩形区域，不隐式 Resize、补边或改变输出格式。
- 单张自由裁剪与比例裁剪都只接收上层已经解析完成的 `CropRectangle`。
- 输出继续使用同目录临时文件安全写入策略。
- 多帧或动画图片第一阶段返回 `UnsupportedInputFormat`。
- 第一阶段原格式 Crop 仅支持 JPEG、PNG、单帧 WebP 和 BMP；GIF、TIFF 不进入本轮范围。
- `ImageCropResult.OutputSize` 必须记录实际逻辑尺寸，并与请求矩形的 Width / Height 一致。

当前 `IImageProcessor.CropAsync` 与 Magick 同格式裁剪已经实现，并验证边界、输出尺寸、多帧拒绝和源文件保护。
## 13. 处理结果细节填充

Magick 实现必须为压缩和转换结果填充 `ImageProcessingDetails`：

- `InputWidth` / `InputHeight` 使用自动方向处理后的输入尺寸。
- `OutputWidth` / `OutputHeight` 使用写出前的最终尺寸。
- 压缩与转换的输入、输出逻辑尺寸必须相同；尺寸变化只由独立 Resize/Crop 结果表达。
- `MetadataRemoved` 根据请求中的 `MetadataPolicy.Remove` 判断，但只表示拍摄、位置和描述性元数据已移除，不表示 ICC 被删除。
- `LossyOutput` 对 JPEG / WebP 返回 true，对 PNG 返回 false。

当前不把这些细节塞进 `ImageJobResult`，避免任务模型过早膨胀。需要展示详细报告时，再设计报告模型。

Metadata 实现约束：

- 当前代码使用的无差别 `image.Strip()` 会同时删除 ICC，不符合目标设计，必须替换为按 Profile / Attribute 类型选择性清理。
- `Preserve` 也不能原样保留失效的 Orientation。`AutoOrient` 后必须确认方向标记已删除或规范为 TopLeft，避免查看器二次旋转。
- `Remove` 删除 EXIF、GPS、IPTC、XMP、注释和内嵌缩略图等隐私或描述性信息；ICC / ICM 在目标格式支持时保留。
- 目标格式无法承载某类元数据时允许自然丢失，但不能声称字节级完整 Preserve；处理后失效的尺寸和缩略图信息必须更新或移除。
## 14. 无效图片错误映射补充

Magick 实现必须把无效图片和损坏图片映射为用户可解释错误：

| 场景 | AtomPixErrorCode | Category |
| --- | --- | --- |
| 文件不存在 | `InputFileNotFound` | `FileSystem` |
| Probe/Preview 读取到 Magick.NET 图片解析异常 | `InvalidImageFile` | `ImageProcessing` |
| Probe 期间发生 IO、权限、路径或平台读取异常 | `ImageReadFailed` | `ImageProcessing` |
| Compress 直接处理损坏图片失败 | `ImageCompressFailed` | `ImageProcessing` |
| Convert 直接处理损坏图片失败 | `ImageConvertFailed` | `ImageProcessing` |

产品流程中，Workflows 会先调用 `ProbeAsync` 做预检，因此用户触发四类图片处理时遇到损坏图片，应优先表现为 `InvalidImageFile`，而不是笼统的具体处理失败。

## 15. 输出写入安全补充

Magick 实现写出压缩、转换、Resize 或 Crop 结果时，不能直接把编码过程写入最终 `OutputPath`。

在读取或创建临时文件前，Magick 实现还必须防御性检查规范化后的 `InputPath` 与 `OutputPath`。两者按当前平台路径规则相同时返回 `OutputPathConflictsWithInput`，不得依赖“上层通常已经校验”而允许原地替换。该检查不让 Imaging 解释 `OverwritePolicy`；它只守住图片处理请求自身的输入输出分离不变量。

写入策略：

```text
1. Workflows 传入的 OutputPath 已经是最终路径。
2. Magick 在最终路径同目录创建临时输出文件。
3. 图片内容先完整写入临时文件。
4. 临时文件写入成功后，再替换或移动到最终 OutputPath。
5. 写入、移动或替换任一步失败时，尽力删除临时文件。
```

约束：

- 临时文件必须与最终文件在同一目录，避免跨卷移动。
- 临时文件名必须带隐藏前缀和 `.tmp` 标记，避免被误认为用户输出结果。
- 如果最终文件已存在，说明 Workflows 已经允许覆盖；Magick 可以执行替换。
- 如果最终文件不存在，Magick 直接移动临时文件到最终路径。
- 图片读取、解码或编码动作失败时返回与当前操作对应的 `ImageCompressFailed`、`ImageConvertFailed`、`ImageResizeFailed` 或 `ImageCropFailed`；输出提交失败统一返回 `ImageWriteFailed`，磁盘空间不足返回 `InsufficientDiskSpace`。任何失败都不得把临时文件或半成品伪装为成功结果。

该策略不能替代真正的备份/恢复系统，但第一阶段必须避免“图片编码写到一半，最终输出路径留下半成品文件”。

## 16. 输出路径目录边界补充

Magick 实现接收的 `OutputPath` 已经是 Workflows 决策后的最终路径。

约束：

- Workflows 必须已经通过文件系统端口准备好业务输出目录；Magick 不调用 `Directory.CreateDirectory` 补建目录。
- 如果 `OutputPath` 只有文件名，现有当前工作目录视为其目录；仍不执行目录创建。
- 临时文件始终位于最终输出路径所在的既有目录；目录缺失、被移除或不可写时返回结构化失败。
- Magick 不解析 `OutputPolicy`，不参与 SameAsInput、Subfolder、CustomDirectory、Skip、Overwrite 或 AutoRename 决策。

## 17. Imaging.Magick DI 注册与取消边界补充

`AtomPix.Imaging.Magick.DependencyInjection` 提供 `AddAtomPixMagickImaging()`。

注册内容：

- `IImageProcessor -> MagickImageProcessor`

取消边界：

- Magick.NET 的读取、解码、缩放和编码 API 是同步、CPU/IO 密集调用。`MagickImageProcessor` 的全部公开异步入口必须把实际 Magick 操作调度到默认后台调度器，不能在调用线程中先执行再用 `Task.FromResult` 包装；否则 Desktop 的 Loading 状态没有机会渲染，窗口会在 Probe、Preview 或正式处理期间冻结。
- 后台调度属于 Imaging.Magick 对其 `IImageProcessor` 异步契约的实现细节；Workflow 和 Desktop 不再额外嵌套 `Task.Run`，Core 也不感知线程模型。
- Magick 实现会在入口检查 `CancellationToken`，并返回 `OperationCanceled`。
- 第一阶段不承诺中途强行终止 Magick.NET 同步读写或编码过程。
- Workflows 负责在批量项之间检查取消，并停止后续未开始项。
