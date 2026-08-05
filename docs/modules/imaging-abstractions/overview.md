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
- 订阅和使用额度、订阅计划等商业规则。
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

`AtomPix.Imaging.Abstractions` 尽量不依赖其他 AtomPix 项目。若后续确实需要复用 Core 中的基础结果模型，也必须保持克制，避免让图片契约层变成业务规则层。
## 8. 图片处理契约基线

`AtomPix.Imaging.Abstractions` 定义 AtomPix 需要图片引擎提供的公共能力。第一阶段围绕四类能力设计：

```text
Probe      探测图片信息
Preview    生成预览图
Compress   压缩图片
Convert    转换格式
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
}
```

设计说明：

- 所有用户可预期失败使用 `OperationResult<T>` 返回。
- 图片库异常由具体实现转换为 `AtomPixError`。
- 所有异步方法必须支持 `CancellationToken`。
- `Capabilities` 用于向 Workflows 和 Desktop 暴露当前图片引擎支持的输入、输出和元数据能力。

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
    bool HasAlpha,
    bool IsAnimated,
    int FrameCount,
    bool HasMetadata);
```

语义：

- `Width` / `Height` 表示按图片方向处理后的可展示尺寸。
- `IsAnimated` 和 `FrameCount` 用于 GIF、WebP、TIFF 等多帧或动画格式。
- 第一阶段即使不完整处理动画，也应识别它是否为动画。
- `HasMetadata` 用于 UI 提示和后续元数据策略。

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
    ImageFormatKind OutputFormat,
    long InputSizeBytes,
    long OutputSizeBytes);
```

语义：

- `OutputPath` 由 Workflows 根据 `OutputPolicy`、输入路径、目标格式和重名策略解析后传入。
- `Profile` 表达压缩意图。
- `ImageCompressResult` 记录压缩前后大小，供任务结果和 UI 统计使用。

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
    long OutputSizeBytes);
```

语义：

- `Profile.OutputFormat` 表达目标输出格式。
- `OutputPath` 已由 Workflows 解析好，图片引擎不负责输出命名策略。
- 转换结果记录输入格式、输出格式和前后文件大小。

### 8.7 ImageProcessorCapabilities

图片引擎能力声明：

```csharp
public sealed record ImageProcessorCapabilities(
    IReadOnlySet<ImageFormatKind> SupportedInputFormats,
    IReadOnlySet<OutputImageFormat> SupportedOutputFormats,
    bool SupportsMetadata,
    bool SupportsAnimatedImages);
```

用途：

- Workflows 可据此校验请求是否可执行。
- Desktop 可据此决定格式选项和能力提示。
- 后续如果增加 SkiaSharp、ImageSharp 等实现，不同引擎可以声明不同能力。

### 8.8 错误处理约定

图片处理失败统一返回 `OperationResult<T>.Failure(AtomPixError)`。

常见错误码使用 Core 中已定义的错误码：

```text
UnsupportedInputFormat
UnsupportedOutputFormat
ImageReadFailed
ImageWriteFailed
ImageCompressFailed
ImageConvertFailed
ImagePreviewFailed
OperationCanceled
```

具体图片库异常必须在实现层转换，不得穿透到 Workflows 或 Desktop。

### 8.9 路径约定

本模块所有公共请求和结果使用 `LocalPath`，不使用裸 `string` 表达本地路径。

路径存在性、权限、目录创建和自动重命名不属于 Imaging.Abstractions 的职责。Workflows 和 Infrastructure 负责输出路径策略解析；具体图片实现只按传入路径执行读写。
## 9. Imaging.Abstractions 工业级硬化基线

图片处理契约层不是被动 DTO 集合。第一阶段开始，所有公开请求、结果和能力对象都必须守住自身不变量，避免非法状态穿透到具体图片引擎。

### 9.1 能力声明不变量

`ImageProcessorCapabilities` 必须满足：

- `SupportedInputFormats` 不能为 null 或空集合。
- `SupportedOutputFormats` 不能为 null 或空集合。
- `SupportedInputFormats` 不能包含 `ImageFormatKind.Unknown`。
- 构造时必须防御性复制集合，调用方后续修改原集合不能影响能力声明。

能力声明表达的是图片引擎对 Workflows 和 Desktop 的稳定承诺，不能依赖外部可变集合。

### 9.2 探测结果不变量

`ImageProbeResult` 必须满足：

- `Format` 不能是 `Unknown`。
- `Width` 和 `Height` 必须大于 0。
- `FileSizeBytes` 不能小于 0。
- `FrameCount` 必须大于 0。
- `IsAnimated = true` 时，`FrameCount` 必须大于 1。

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

`ImageCompressResult` 和 `ImageConvertResult` 必须满足：

- 输入或输出格式不能是 `Unknown`。
- 输入和输出文件大小不能小于 0。

输出路径仍由 Workflows 决策并传入。图片引擎只按最终路径执行读写，不处理覆盖、跳过、自动重命名等策略。

### 9.5 测试要求

`AtomPix.Imaging.Abstractions.Tests` 负责验证契约对象的不变量。新增图片契约类型时，必须补充对应的非法状态测试和有效状态测试。
## 10. 处理结果细节基线

压缩和转换结果可以携带 `ImageProcessingDetails`，用于描述图片处理效果：

- 输入宽高。
- 输出宽高。
- 是否应用 resize。
- 是否移除 metadata。
- 输出是否有损。

该对象属于图片处理契约，不属于 UI 展示模型。Workflows 可以选择只消费大小和路径；未来 UI 如需展示详细处理报告，应优先从该模型派生。

`ImageProcessingDetails` 的宽高必须大于 0。图片引擎无法可靠得出细节时，可以暂时返回 null，但 Magick 第一阶段实现必须填充。