# AtomPix.Imaging.Abstractions 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Imaging.Abstractions` 是图片处理子系统的契约层。

它定义 AtomPix 需要图片引擎提供哪些能力，但不关心这些能力由 Magick.NET、SkiaSharp、ImageSharp 还是其他库实现。

## 2. 允许包含

- 图片处理接口，例如 `IImageProcessor`。
- 图片探测、预览、压缩、转换请求和结果。
- 图片格式枚举、MIME 类型、元数据策略、预览数据结构。
- 图片处理错误分类和引擎能力描述。

## 3. 禁止包含

- Magick.NET、SkiaSharp、ImageSharp 等具体图片库类型。
- Avalonia `Bitmap`、AtomUI 控件或其他 UI 类型。
- 文件选择器、弹窗、ViewModel。
- 配置文件读写、日志实现、网络实现。

## 4. 推荐目录

```text
src/AtomPix.Imaging.Abstractions/
  AtomPix.Imaging.Abstractions.csproj
  Formats/
  Metadata/
  Processing/
  Preview/
  Requests/
  Results/
```

## 5. 首批契约

主接口：

```csharp
public interface IImageProcessor
{
    ImageProcessorCapabilities Capabilities { get; }

    Task<OperationResult<ImageProbeResult>> ProbeAsync(
        ImageProbeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(
        ImagePreviewRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCompressResult>> CompressAsync(
        ImageCompressRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageConvertResult>> ConvertAsync(
        ImageConvertRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageResizeResult>> ResizeAsync(
        ImageResizeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCropResult>> CropAsync(
        ImageCropRequest request,
        CancellationToken cancellationToken);
}
```

首批模型：

- `ImageProcessorCapabilities`
- `ImageFormatKind`
- `ImageProbeRequest`
- `ImageProbeResult`
- `ImagePreviewRequest`
- `ImagePreviewResult`
- `ImageCompressRequest`
- `ImageCompressResult`
- `ImageConvertRequest`
- `ImageConvertResult`
- `TransparencyProcessingResult`
- `TransparencyOutcome`
- `ImageResizeRequest`
- `ImageResizeResult`
- `ImageCropRequest`
- `ImageCropResult`

## 6. 预览结果约定

预览结果不返回 Avalonia 类型，推荐返回编码后的图片数据：

```csharp
public sealed record ImagePreviewResult(
    byte[] EncodedBytes,
    string MimeType,
    int Width,
    int Height);
```

Desktop 层负责把 `EncodedBytes` 转成 Avalonia 可显示的位图对象。

## 7. 依赖规则

`AtomPix.Imaging.Abstractions` 在 AtomPix 项目内只依赖 `AtomPix.Core`，用于复用稳定的结果、路径、策略和错误类型。Core 不反向依赖本模块，本模块也不依赖 Workflows、Infrastructure、Desktop 或任何具体图片实现；图片契约层不能演变为业务规则层。
## 8. 图片处理契约基线

`AtomPix.Imaging.Abstractions` 定义 AtomPix 需要图片引擎提供的公共能力。第一阶段目标围绕六类能力设计：

```text
Probe      探测图片信息
Preview    生成预览图
Compress   压缩图片
Convert    转换格式
Resize     调整完整图片尺寸
Crop       提取矩形区域
```

本模块可以依赖 `AtomPix.Core`，用于复用 `OperationResult<T>`、`AtomPixError`、`LocalPath`、`CompressionProfile`、`ConversionProfile` 和 `OutputImageFormat` 等稳定核心类型。

依赖边界：

- 可以依赖 `AtomPix.Core`。
- 不能依赖 Magick.NET、SkiaSharp、ImageSharp 等具体图片库。
- 不能依赖 Avalonia、AtomUI 或 Desktop UI 类型。
- 不能返回 `ImageMagick` 类型或 Avalonia `Bitmap`。

### 8.1 IImageProcessor

主接口：

```csharp
public interface IImageProcessor
{
    ImageProcessorCapabilities Capabilities { get; }

    Task<OperationResult<ImageProbeResult>> ProbeAsync(
        ImageProbeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(
        ImagePreviewRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCompressResult>> CompressAsync(
        ImageCompressRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageConvertResult>> ConvertAsync(
        ImageConvertRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageResizeResult>> ResizeAsync(
        ImageResizeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCropResult>> CropAsync(
        ImageCropRequest request,
        CancellationToken cancellationToken);
}
```

设计说明：

- 所有用户可预期失败使用 `OperationResult<T>` 返回。
- 图片库异常由具体实现转换为 `AtomPixError`。
- 所有异步方法必须支持 `CancellationToken`。
- `Capabilities` 用于向 Workflows 和 Desktop 暴露当前图片引擎支持的输入、输出和元数据能力。

目标接口中的六类能力为 Probe、Preview、Compress、Convert、Resize、Crop。当前代码和公共 API 白名单仍只有前四类；实现 Desktop 的调整尺寸与裁剪流程前，必须升级接口、请求/结果模型与契约测试。

Workflow Request 携带 Core `ResizePolicy` 作为用户业务意图；Workflow 在 Probe 后调用 Core 统一解析为 `ResolvedResizeSize`。传给图片处理器的 `ImageResizeRequest` 只携带确定的目标 Width / Height，不再让 Imaging 解释保持比例、双边约束或百分比。

单张比例意图必须在 Desktop / Core 中先落实为确定的 `CropRectangle`；传给图片处理器的 `ImageCropRequest` 只携带该矩形，不接收 `CropAspectRatio`、画布状态或其他上层编辑意图。批量裁剪不进入 MVP。

Resize 与 Crop 不能复用：Resize 改变完整图片尺寸，Crop 提取原图坐标系中的矩形区域。

### 8.2 ImageFormatKind

输入和探测格式枚举：

```csharp
public enum ImageFormatKind
{
    Unknown,
    Jpeg,
    Png,
    WebP,
    Bmp,
    Gif,
    Tiff
}
```

说明：

- 第一阶段支持 JPEG / PNG / WebP / BMP / GIF / TIFF。
- HEIC、AVIF、SVG、PDF、PSD 等格式暂缓。
- `OutputImageFormat` 仍定义在 Core，因为它表达 AtomPix 对用户承诺的输出格式。

### 8.3 ImageProbeRequest / ImageProbeResult

请求：

```csharp
public sealed record ImageProbeRequest(LocalPath InputPath);
```

结果：

```csharp
public sealed record ImageProbeResult(
    LocalPath InputPath,
    ImageFormatKind Format,
    int Width,
    int Height,
    long FileSizeBytes,
    bool HasAlphaChannel,
    bool HasTransparency,
    bool IsAnimated,
    int FrameCount,
    bool HasMetadata,
    bool HasColorProfile);
```

语义：

- `Width` / `Height` 表示按图片方向处理后的可展示尺寸。
- `HasAlphaChannel` 只表示像素格式包含 Alpha 通道，不代表存在可见透明区域。
- `HasTransparency` 表示至少一个像素的 Alpha 小于完全不透明，是转换提示和透明铺底规则的权威输入事实。
- `HasTransparency = true` 必须同时满足 `HasAlphaChannel = true`；完全不透明的 RGBA 图片只能报告 `HasAlphaChannel = true, HasTransparency = false`。
- `IsAnimated` 和 `FrameCount` 用于 GIF、WebP、TIFF 等多帧或动画格式。
- 第一阶段即使不完整处理动画，也应识别它是否为动画。
- `HasMetadata` 表示存在 EXIF、GPS、IPTC、XMP、注释、内嵌缩略图等拍摄或描述性元数据，用于 UI 提示和 `MetadataPolicy`。
- `HasColorProfile` 单独表示存在 ICC / ICM 色彩配置；它不包含在 `HasMetadata` 的产品语义中，也不受 `MetadataPolicy.Remove` 控制。

### 8.4 ImagePreviewRequest / ImagePreviewResult

请求：

```csharp
public sealed record ImagePreviewRequest(
    LocalPath InputPath,
    int MaxPixelSize);
```

语义：

- `MaxPixelSize` 表示预览图最长边不超过该像素值。
- 例如 Desktop 可请求最长边不超过 `2048` 的预览图。

结果：

```csharp
public sealed record ImagePreviewResult(
    byte[] EncodedBytes,
    string MimeType,
    int Width,
    int Height);
```

约束：

- 预览结果不返回 Avalonia `Bitmap`。
- Desktop 负责将 `EncodedBytes` 转换为 Avalonia 可显示对象。
- 具体预览编码格式由图片引擎实现决定，可以是 PNG 或 JPEG。
- 预览生成必须自动处理 EXIF 方向。

### 8.5 ImageCompressRequest / ImageCompressResult

请求：

```csharp
public sealed record ImageCompressRequest(
    LocalPath InputPath,
    LocalPath OutputPath,
    CompressionProfile Profile);
```

结果：

```csharp
public sealed record ImageCompressResult(
    LocalPath InputPath,
    LocalPath OutputPath,
    ImageFormatKind InputFormat,
    ImageFormatKind OutputFormat,
    long InputSizeBytes,
    long OutputSizeBytes,
    ImageQuality? AppliedQuality);
```

语义：

- `OutputPath` 由 Workflows 根据 `OutputPolicy`、输入路径、探测得到的同一格式和重名策略解析后传入。
- `Profile` 表达压缩意图。
- 压缩必须保持输入格式；`InputFormat` 与 `OutputFormat` 必须相等，输出扩展名也必须表达同一格式。PNG 转 WebP 等行为属于 `ConvertAsync`。
- `ImageCompressResult` 记录压缩前后大小，供任务结果和 UI 统计使用。
- `AppliedQuality` 是处理器实际采用的有损质量。JPEG/WebP 等有损输出成功时必须有值；PNG 等无损输出必须为 `null`。
- `AppliedQuality` 不能由 Desktop 根据模式反推：Smart 可能经过自适应重试，Custom/固定档位也必须以处理器实际执行结果为准。
- Imaging 结果只报告实际 `InputSizeBytes / OutputSizeBytes`，不定义 Saved 或 SizeDelta 字段；跨压缩、转换、Resize、Crop 的中性体积变化由 Core `ImageJobResult` 统一派生。

### 8.6 ImageConvertRequest / ImageConvertResult

请求：

```csharp
public sealed record ImageConvertRequest(
    LocalPath InputPath,
    LocalPath OutputPath,
    ConversionProfile Profile);
```

结果：

```csharp
public sealed record ImageConvertResult(
    LocalPath InputPath,
    LocalPath OutputPath,
    ImageFormatKind InputFormat,
    ImageFormatKind OutputFormat,
    long InputSizeBytes,
    long OutputSizeBytes,
    TransparencyProcessingResult Transparency);

public sealed record TransparencyProcessingResult(
    TransparencyOutcome Outcome,
    RgbColor? BackgroundColor);

public enum TransparencyOutcome
{
    NotPresent,
    Preserved,
    Flattened
}
```

语义：

- `Profile.OutputFormat` 表达目标输出格式。
- `OutputPath` 已由 Workflows 解析好，图片引擎不负责输出命名策略。
- 转换结果记录输入格式、输出格式和前后文件大小。
- `Transparency` 报告处理器实际执行的透明度行为，Desktop 和 Workflows 不根据输入与请求参数自行推断。
- 文件大小允许输出大于、等于或小于输入；这三种情况都不改变处理器成功语义。

透明度结果不变量：

| `Outcome` | `BackgroundColor` | 语义 |
| --- | --- | --- |
| `NotPresent` | 必须为 `null` | 源图没有真实透明像素。 |
| `Preserved` | 必须为 `null` | 源图真实透明且目标格式支持 Alpha，透明度已保留。 |
| `Flattened` | 必须有值 | 源图真实透明且目标格式不支持 Alpha，已经按返回颜色铺底。 |

### 8.7 ImageResizeRequest / ImageResizeResult

请求：

```csharp
public sealed record ImageResizeRequest(
    LocalPath InputPath,
    LocalPath OutputPath,
    ResolvedResizeSize TargetSize,
    SameFormatEncodingPolicy EncodingPolicy);
```

结果：

```csharp
public sealed record ImageResizeResult(
    LocalPath InputPath,
    LocalPath OutputPath,
    ImageFormatKind Format,
    ImageSize InputSize,
    ImageSize OutputSize,
    long InputSizeBytes,
    long OutputSizeBytes);
```

语义：

- `OutputPath` 已由 Workflows 根据输入文件扩展名和 `OutputPolicy` 解析完毕；图片引擎不负责命名、覆盖或自动重命名。
- `TargetSize` 已由 Core 使用原图逻辑尺寸和 `ResizePolicy` 解析完毕；图片引擎不得再次解释比例、百分比或双边约束。
- Resize 保持输入格式，`OutputPath` 的扩展名必须与探测得到的输入格式一致；它不是隐式格式转换。
- `EncodingPolicy` 只规定同格式重新编码时的有损质量与元数据策略，不包含 Resize 业务规则。
- 图片引擎必须先自动处理 EXIF 方向，并以校正后的逻辑宽高执行 Resize。
- `OutputSize` 必须严格等于 `TargetSize`；不得裁剪、补边或擅自改写宽高。
- 前后文件字节数供 Workflow 构造 `ImageJobResult` 和 UI 统计使用。

`ImageResizeRequest` 是确定执行契约，不接收高层 `ResizePolicy`；用户意图到目标像素尺寸的解析只能发生在 Core。

### 8.8 ImageCropRequest / ImageCropResult

请求：

```csharp
public sealed record ImageCropRequest(
    LocalPath InputPath,
    LocalPath OutputPath,
    CropRectangle CropArea,
    SameFormatEncodingPolicy EncodingPolicy);
```

结果：

```csharp
public sealed record ImageCropResult(
    LocalPath InputPath,
    LocalPath OutputPath,
    ImageFormatKind Format,
    ImageSize InputSize,
    ImageSize OutputSize,
    long InputSizeBytes,
    long OutputSizeBytes);
```

语义：

- `OutputPath` 已由 Workflows 根据输入文件扩展名和 `OutputPolicy` 解析完毕；图片引擎不负责命名、覆盖或自动重命名。
- `CropArea` 是自动方向校正后的原图逻辑坐标系中的最终执行矩形；图片引擎不接收或解释比例、画布缩放和 UI 选框状态。
- Crop 保持输入格式，`OutputPath` 的扩展名必须与探测得到的输入格式一致；它不是隐式格式转换。
- `EncodingPolicy` 只规定同格式重新编码时的有损质量与元数据策略，不包含 Crop 业务规则。
- 图片引擎必须先自动处理 EXIF 方向，再使用同一逻辑坐标系解释 `CropArea`。
- `OutputSize.Width / Height` 必须严格等于 `CropArea.Width / Height`；不得二次 Resize、补边或移动选区。
- 前后文件字节数供 Workflow 构造 `ImageJobResult` 和 UI 统计使用。

### 8.9 ImageProcessorCapabilities

图片引擎能力声明：

```csharp
public sealed record ImageProcessorCapabilities(
    IReadOnlySet<ImageFormatKind> SupportedInputFormats,
    IReadOnlySet<OutputImageFormat> SupportedOutputFormats,
    bool SupportsMetadata,
    bool SupportsAnimatedImages,
    ImageResourceCapabilities Resources,
    ImageResizeCapabilities? Resize,
    ImageCropCapabilities? Crop);

public sealed record ImageResourceCapabilities(
    long MaxInputFileSizeBytes,
    int MaxInputWidth,
    int MaxInputHeight,
    long MaxInputPixelCount,
    int MaxOutputWidth,
    int MaxOutputHeight,
    long MaxOutputPixelCount);

public sealed record ImageResizeCapabilities(
    IReadOnlySet<ImageFormatKind> SupportedSameFormatFormats,
    int MaxWidth,
    int MaxHeight,
    long MaxPixelCount);

public sealed record ImageCropCapabilities(
    IReadOnlySet<ImageFormatKind> SupportedSameFormatFormats,
    int MaxInputWidth,
    int MaxInputHeight,
    long MaxInputPixelCount);
```

用途：

- Workflows 可据此校验请求是否可执行。
- Desktop 可据此决定格式选项和能力提示。
- 后续如果增加 SkiaSharp、ImageSharp 等实现，不同引擎可以声明不同能力。
- `SupportedInputFormats` 是 Probe、Preview 和转换输入的公共可读集合，不表示每种格式都支持每一种处理。第一阶段 Compress 只接受可同格式写出的 JPEG、PNG、WebP；Resize/Crop 分别以自己的 `SupportedSameFormatFormats` 为准。
- `Resources` 是所有 Probe 后处理操作的公共硬边界；输入文件字节数、输入逻辑尺寸和计划输出尺寸分别与对应上限比较。
- 公共资源能力是最大承诺；Resize/Crop 的操作专用能力可以相同或更严格，Workflow 必须同时满足公共和操作专用边界。
- 能力值表示允许的最大使用范围，不表示应用启动时已经预分配了对应内存或磁盘空间。
- `Resize = null` 表示当前图片引擎没有独立 Resize 能力。
- `SupportedSameFormatFormats` 声明可保持原格式写出的 Resize 格式；最大宽高和最大像素数是执行资源边界。
- Workflows 必须在创建 Job 前用已经解析的 `TargetSize` 完成能力预检；能力对象不参与目标尺寸计算。
- `Crop = null` 表示当前图片引擎没有独立 Crop 能力；Crop 的格式集合与最大输入尺寸限制由 `ImageCropCapabilities` 单独声明。
- Workflows 必须在创建 Job 前使用 Probe 的逻辑输入尺寸完成 Crop 能力预检；能力对象不参与选区生成或边界校验。

### 8.10 错误处理约定

图片处理失败统一返回 `OperationResult<T>.Failure(AtomPixError)`。

常见错误码使用 Core 中已定义的错误码：

```text
UnsupportedInputFormat
UnsupportedOutputFormat
ImageReadFailed
ImageWriteFailed
ImageCompressFailed
ImageConvertFailed
ImageResizeFailed
ImageCropFailed
ImagePreviewFailed
InputFileTooLarge
ImageDimensionsExceedLimit
ImageResourceLimitExceeded
InsufficientDiskSpace
OperationCanceled
```

具体图片库异常必须在实现层转换，不得穿透到 Workflows 或 Desktop。

### 8.11 路径约定

本模块所有公共请求和结果使用 `LocalPath`，不使用裸 `string` 表达本地路径。

路径存在性、权限、目录创建和自动重命名不属于 Imaging.Abstractions 的职责。Workflows 负责输出路径与冲突策略，Infrastructure 只通过端口提供文件系统事实和目录创建能力；具体图片实现只按传入的既有目录和最终路径执行读写。
## 9. Imaging.Abstractions 工业级硬化基线

图片处理契约层不是被动 DTO 集合。第一阶段开始，所有公开请求、结果和能力对象都必须守住自身不变量，避免非法状态穿透到具体图片引擎。

### 9.1 能力声明不变量

`ImageProcessorCapabilities` 必须满足：

- `SupportedInputFormats` 不能为 null 或空集合。
- `SupportedOutputFormats` 不能为 null 或空集合。
- `SupportedInputFormats` 不能包含 `ImageFormatKind.Unknown`。
- 构造时必须防御性复制集合，调用方后续修改原集合不能影响能力声明。
- `Resources` 不能为 null；文件字节数、宽高和像素数的七个上限都必须大于 `0`。
- `MaxInputPixelCount` 和 `MaxOutputPixelCount` 使用 `long`；实际宽高乘积必须使用受检查的 `long` 运算。
- `Resize` 非 null 时，`SupportedSameFormatFormats` 不能为 null、空集合或包含 `Unknown`，且其格式必须是 `SupportedInputFormats` 的子集。
- `Resize` 非 null 时，`MaxWidth`、`MaxHeight` 和 `MaxPixelCount` 必须大于 0。
- `Crop` 非 null 时，`SupportedSameFormatFormats` 不能为 null、空集合或包含 `Unknown`，且其格式必须是 `SupportedInputFormats` 的子集。
- `Crop` 非 null 时，`MaxInputWidth`、`MaxInputHeight` 和 `MaxInputPixelCount` 必须大于 0。

能力声明表达的是图片引擎对 Workflows 和 Desktop 的稳定承诺，不能依赖外部可变集合。

### 9.2 探测结果不变量

`ImageProbeResult` 必须满足：

- `Format` 不能是 `Unknown`。
- `Width` 和 `Height` 必须大于 0。
- `FileSizeBytes` 不能小于 0。
- `FrameCount` 必须大于 0。
- `IsAnimated = true` 时，`FrameCount` 必须大于 1。
- `HasTransparency = true` 时，`HasAlphaChannel` 必须为 `true`。

图片引擎如果无法识别格式，应返回失败结果，而不是返回 `Unknown` 成功结果。

### 9.3 预览契约不变量

`ImagePreviewRequest` 必须满足：

- `MaxPixelSize` 必须大于 0。

`ImagePreviewResult` 必须满足：

- `EncodedBytes` 不能为 null 或空数组。
- `MimeType` 不能为空白。
- `Width` 和 `Height` 必须大于 0。
- 构造时必须复制 `EncodedBytes`，避免结果被外部数组修改。

预览结果依然只返回编码后的图片数据，不返回 Avalonia、AtomUI 或具体图片库对象。

### 9.4 压缩与转换契约不变量

`ImageCompressRequest` 和 `ImageConvertRequest` 必须满足：

- `Profile` 不能为 null。
- `InputPath` 与 `OutputPath` 使用 `LocalPath`，不使用裸 `string`。
- `InputPath` 与 `OutputPath` 不能指向按当前平台路径规则判断的同一路径；处理器必须把该情况拒绝为 `OutputPathConflictsWithInput`，不得原地覆盖。

`ImageCompressResult` 和 `ImageConvertResult` 必须满足：

- 输入或输出格式不能是 `Unknown`。
- 输入和输出文件大小不能小于 0。
- `ImageCompressResult.InputFormat` 必须等于 `OutputFormat`，并与请求输入的探测格式及输出扩展名一致；压缩不得借输出扩展名执行格式转换。
- `ImageCompressResult` 的有损输出必须携带合法 `AppliedQuality`，无损输出不得携带伪造质量。
- `ImageConvertResult.Transparency` 不能为空，并满足 `Outcome` 与 `BackgroundColor` 的组合不变量。
- 目标格式支持 Alpha 且源图真实透明时只能返回 `Preserved`；目标格式不支持 Alpha 且源图真实透明时只能返回 `Flattened`。

输出路径仍由 Workflows 决策并传入。图片引擎只按最终路径执行读写，不处理覆盖、跳过、自动重命名等策略。

### 9.5 Resize 契约不变量

`ImageResizeRequest` 必须满足：

- `TargetSize` 和 `EncodingPolicy` 不能为 null，且必须分别满足 Core 对应值对象的不变量。
- `InputPath` 与 `OutputPath` 使用 `LocalPath`；两者扩展名必须表达同一种受支持格式。
- `InputPath` 与 `OutputPath` 不能指向按当前平台路径规则判断的同一路径；违反时返回 `OutputPathConflictsWithInput`。

`ImageResizeResult` 必须满足：

- `Format` 不能是 `Unknown`。
- `InputSize` 与 `OutputSize` 都必须是正尺寸。
- `OutputSize` 必须严格等于请求中的 `TargetSize`。
- 输入和输出文件大小不能小于 0。

### 9.6 Crop 契约不变量

`ImageCropRequest` 必须满足：

- `CropArea` 和 `EncodingPolicy` 不能为 null，且必须分别满足 Core 对应值对象的不变量。
- `InputPath` 与 `OutputPath` 使用 `LocalPath`；两者扩展名必须表达同一种受支持格式。
- `InputPath` 与 `OutputPath` 不能指向按当前平台路径规则判断的同一路径；违反时返回 `OutputPathConflictsWithInput`。

`ImageCropResult` 必须满足：

- `Format` 不能是 `Unknown`。
- `InputSize` 与 `OutputSize` 都必须是正尺寸。
- `OutputSize.Width / Height` 必须严格等于请求 `CropArea.Width / Height`。
- 输入和输出文件大小不能小于 0。

### 9.7 测试要求

`AtomPix.Imaging.Abstractions.Tests` 负责验证契约对象的不变量。新增图片契约类型时，必须补充对应的非法状态测试和有效状态测试。
## 10. 处理结果细节基线

压缩和转换结果可以携带 `ImageProcessingDetails`，用于描述图片处理事实：

- 输入宽高。
- 输出宽高。
- 是否移除拍摄、位置和描述性 metadata；ICC 不计入这个布尔值。
- 输出是否有损。

该对象属于图片处理契约，不属于 UI 展示模型。Workflows 可以选择只消费大小和路径；未来 UI 如需展示详细处理报告，应优先从该模型派生。

`ImageProcessingDetails` 的宽高必须大于 0。Compress 和 Convert 的输入、输出宽高必须相等，因为二者都不承担 Resize；尺寸变化由独立 `ImageResizeResult` / `ImageCropResult` 表达。图片引擎无法可靠得出细节时，可以暂时返回 null，但 Magick 第一阶段实现必须填充。

早期代码中的旧 `ResizeApplied` 字段与压缩/转换内嵌 Resize 行为已经在迁移中移除；现行契约不包含该字段，也不保留由宽高差异推断的兼容语义。
