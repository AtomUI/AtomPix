# AtomPix.Core 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Core` 是 AtomPix 的业务核心，位于洋葱模型最中心。

它定义 AtomPix 的产品语言、业务模型、值对象、策略、任务状态、错误模型和纯业务规则。其他模块可以依赖 Core，但 Core 不依赖任何外层模块。

Core 不是通用工具库，也不是 UI、图片库或存储实现的集合。

## 2. 允许包含

- 图片任务、批量任务、任务状态等核心模型。
- 压缩策略、转换策略、输出命名策略、覆盖策略。
- 应用设置模型。
- 统一结果模型和错误模型。
- 业务值对象，例如本地路径、输出路径、质量参数、尺寸限制。
- 不依赖外部 IO 的业务规则和策略判断。
- Core 需要的外部能力端口，例如设置存储、最近记录存储、文件系统端口。

## 3. 禁止包含

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- Magick.NET、SkiaSharp、ImageSharp 等具体图片库类型。
- JSON 文件读写、SQLite 访问、系统路径解析、注册表、Keychain 等基础设施实现。
- HTTP API 具体调用和崩溃上报实现。
- Serilog、NLog 等具体日志框架。
- `FileUtils`、`StringUtils`、`JsonUtils` 这类泛工具集合。
- DI 注册和应用启动逻辑。

## 4. 推荐目录

```text
src/AtomPix.Core/
  AtomPix.Core.csproj
  Compression/
  Conversion/
  Jobs/
  
  Results/
  Settings/
  ValueObjects/
```

## 5. 首批对象范围

压缩与转换：

- `CompressionProfile`
- `CompressionMode`
- `ImageQuality`
- `ResizePolicy`
- `SameFormatEncodingPolicy`
- `CropRectangle`
- `CropAspectRatio`
- `MetadataPolicy`
- `ConversionProfile`
- `RgbColor`
- `TransparencyPolicy`
- `OutputImageFormat`
- `OverwritePolicy`
- `OutputNamingPolicy`

任务：

- `ImageJob`
- `ImageJobId`
- `ImageJobType`
- `ImageJobStatus`
- `ImageJobResult`
- `BatchJob`
- `BatchJobStatus`

设置与结果：

- `AppSettings`
- `OperationResult`
- `OperationResult<T>`
- `AtomPixError`
- `AtomPixErrorCode`

端口：

- `IAppSettingsStore`
- `IRecentItemsStore`
- `IFileSystemService`
- `IAppPathProvider`

## 6. 设计约束

- Core 可以有业务代码，但只能是纯业务规则。
- Core 不知道设置、最近记录或文件系统能力如何持久化。
- Core 不知道图片处理由 Magick.NET、SkiaSharp 还是其他库完成。
- Core 中的公共接口签名只能使用 Core 类型和 .NET BCL 类型。
- 图片引擎调用契约不放在 Core，统一放在 `AtomPix.Imaging.Abstractions`。
## 7. 结果与错误模型基线

Core 使用结构化结果表达用户可预期失败，不把业务失败作为常规异常流程。

原则：

- 用户可预期失败使用 `OperationResult` 或 `OperationResult<T>` 返回。
- 程序缺陷、不可恢复状态和违反编程约束的情况可以抛异常。
- 错误必须结构化，不能只返回一段 UI 文案。
- Core 中的错误 `Message` 只作为开发诊断信息或默认信息，不作为最终 UI 本地化文案。
- Desktop 后续应根据 `AtomPixErrorCode` 映射具体展示文案。

### 7.1 OperationResult

基础结果模型：

```csharp
public sealed record OperationResult(
    bool Succeeded,
    AtomPixError? Error)
{
    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(AtomPixError error) => new(false, error);
}
```

带值结果模型：

```csharp
public sealed record OperationResult<T>(
    bool Succeeded,
    T? Value,
    AtomPixError? Error)
{
    public static OperationResult<T> Success(T value) => new(true, value, null);

    public static OperationResult<T> Failure(AtomPixError error) => new(false, default, error);
}
```

约束：

- `Succeeded = true` 时，`Error` 必须为 `null`。
- `Succeeded = true` 时，`OperationResult<T>.Value` 必须有值。
- `Succeeded = false` 时，`Error` 必须有值。
- `Succeeded = false` 时，调用方不应使用 `Value`。

### 7.2 AtomPixError

统一错误模型：

```csharp
public sealed record AtomPixError(
    AtomPixErrorCode Code,
    AtomPixErrorCategory Category,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
```

`Details` 用于保存结构化上下文，例如路径、格式、参数名、实际值等。不要把大对象、异常实例、UI 控件或图片库对象放入 `Details`。

`Details` 是进程内错误上下文，不等于可直接落盘的日志字段。日志层必须显式挑选并脱敏字段，不能整体序列化 `Details`。未预期错误可以由外层诊断边界增加 `Details["DiagnosticId"]`，Core 业务规则本身不创建诊断编号，也不依赖日志作用域。

### 7.3 AtomPixErrorCategory

首批错误分类：

```csharp
public enum AtomPixErrorCategory
{
    Validation,
    FileSystem,
    ImageProcessing,
    UnsupportedFormat,
    Permission,
    Configuration,
    Cancellation,
    Unexpected
}
```

分类含义：

| 分类 | 含义 |
| --- | --- |
| `Validation` | 用户输入、参数组合或业务规则校验失败。 |
| `FileSystem` | 本地文件、目录、路径、覆盖策略等文件系统相关失败。 |
| `ImageProcessing` | 图片读取、预览、压缩、转换、调整尺寸、裁剪或写入过程失败。 |
| `UnsupportedFormat` | 输入或输出格式不支持。 |
| `Permission` | 文件访问权限、目录写入权限等权限问题。 |
| `Configuration` | 设置读取、保存或配置内容无效。 |
| `Cancellation` | 用户或系统触发取消。 |
| `Unexpected` | 未预期错误，应尽量收敛到外层统一处理。 |

### 7.4 AtomPixErrorCode

首批错误码：

```csharp
public enum AtomPixErrorCode
{
    Unknown,
    OperationCanceled,

    InputFileNotFound,
    InputDirectoryNotFound,
    InputFileTooLarge,
    OutputDirectoryNotFound,
    OutputFileAlreadyExists,
    OutputPathConflictsWithInput,
    InvalidInputPath,
    InvalidOutputPath,
    InvalidOutputNamingPattern,

    UnsupportedInputFormat,
    UnsupportedOutputFormat,
    InvalidImageFile,

    InvalidCompressionQuality,
    InvalidResizeOptions,
    InvalidCropOptions,
    InvalidConversionOptions,
    InvalidMetadataOptions,
    ImageDimensionsExceedLimit,

    ImageReadFailed,
    ImageWriteFailed,
    ImageCompressFailed,
    ImageConvertFailed,
    ImageResizeFailed,
    ImageCropFailed,
    ImagePreviewFailed,
    ImageResourceLimitExceeded,
    InsufficientDiskSpace,

    SettingsLoadFailed,
    SettingsSaveFailed,
    RecentItemsSaveFailed
}
```

错误码应保持稳定，作为 Desktop 本地化展示、日志筛选、测试断言和后续错误统计的依据。

### 7.5 批量任务结果约定

批量流程整体返回：

```csharp
OperationResult<BatchResult>
```

语义：

- 整体 `Failure` 表示批量流程在创建 Job 前被拒绝，例如输入为空或输出目录非法；Workflow 将其投影为 `StartRejected`。
- 整体 `Success` 表示批量流程成功启动并完成调度或执行。
- 单项失败记录在 `BatchResult.Items` 中，不让整个批量结果直接失败。
- 批量结果允许部分成功、部分失败。

`BatchResult` 的结构与不变量见 11.9 和 16.5；Workflow 创建边界与状态编排见 [Workflow 任务状态机编排设计](../workflows/job-state-orchestration.md)。

### 7.6 取消语义

取消属于受控结果，不属于未预期异常。

取消统一使用：

```text
Code = OperationCanceled
Category = Cancellation
```

调用方可以根据该错误码决定是否显示普通错误提示。Desktop 默认不应把用户主动取消展示为严重错误。
## 8. 压缩配置模型基线

`CompressionProfile` 表达 AtomPix 的一套压缩方案组合。

从产品视角看，它由两类用户可理解的设置组成：

```text
压缩强度
是否保留拍摄、位置和描述性信息
```

从工程视角看，第一阶段拆为四个核心概念：

```text
CompressionProfile
CompressionMode
ImageQuality
MetadataPolicy
```

### 8.1 CompressionProfile

`CompressionProfile` 是压缩方案的聚合对象，不是单一参数。

建议模型：

```csharp
public sealed record CompressionProfile(
    CompressionMode Mode,
    ImageQuality? Quality,
    MetadataPolicy MetadataPolicy);
```

约束：

- `Mode` 决定压缩强度。
- `Quality` 表达质量参数，主要用于 JPEG / WebP 等有损压缩格式。
- `MetadataPolicy` 决定是否保留 EXIF、GPS、IPTC、XMP、注释和内嵌缩略图等拍摄或描述性元数据；ICC 色彩配置不受该策略控制。
- `Smart` 模式下 `Quality` 必须为空，具体候选序列由 AtomPix 内置格式策略决定，用户不能覆盖。
- `Custom` 模式下 `Quality` 必须由用户显式指定；非 `Custom` 模式不得把页面暂存的自定义质量作为有效请求参数。

### 8.2 CompressionMode

第一阶段压缩模式：

```csharp
public enum CompressionMode
{
    Smart,
    HighQuality,
    Balanced,
    Maximum,
    Custom
}
```

语义：

| 模式 | 含义 |
| --- | --- |
| `Smart` | 默认推荐模式，根据格式和场景自动选择压缩策略。 |
| `HighQuality` | 尽量保持清晰度，压缩幅度较小。 |
| `Balanced` | 在清晰度和文件体积之间取平衡。 |
| `Maximum` | 尽量减小文件体积，允许更明显的压缩损耗。 |
| `Custom` | 用户手动控制有损输出质量；元数据仍由独立的 `MetadataPolicy` 控制。 |

### 8.3 ImageQuality

`ImageQuality` 表达有损压缩质量。

建议模型：

```csharp
public readonly record struct ImageQuality(int Value);
```

约束：

- 合法范围为 `1` 到 `100`。
- `100` 表示最高质量，不代表无损。
- `1` 表示最低质量，通常只适合极限压缩场景。
- Core 负责表达和校验质量意图，不负责决定不同图片格式如何映射该值。

第一阶段默认值：

| 模式 | 默认质量 |
| --- | --- |
| `HighQuality` | `90` |
| `Balanced` | `80` |
| `Maximum` | `65` |
| `Smart` | 由格式策略决定 |
| `Custom` | 用户指定 |

自定义质量是 `1..100` 的整数值。Desktop 可以使用滑块和整数输入框编辑同一个值，但 Core 只接收最终的 `ImageQuality`。页面草稿可以保留未被当前模式选用的最近合法值；提交的 `CompressionProfile` 只能包含当前模式的有效事实。

PNG 等无损格式不使用 `ImageQuality`。具体无损优化由 `AtomPix.Imaging.Magick` 根据格式能力决定，但压缩流程不得为了使用质量参数而擅自改变输出格式。

### 8.4 独立 ResizePolicy（调整尺寸契约）

`ResizePolicy` 表达独立“调整尺寸”任务如何改变完整图片的 width 和 height，不是 `CompressionProfile` 或 `ConversionProfile` 的字段，也不表达裁剪或补边。

`ResizePolicy` 是封闭的二选一业务意图，不能把 Pixel 与 Percentage 的字段平铺为一组可以任意组合的可空属性。概念模型：

```csharp
public abstract record ResizePolicy;

public sealed record PixelResizePolicy(
    int? Width,
    int? Height,
    bool MaintainAspectRatio,
    bool PreventUpscaling = false) : ResizePolicy;

public sealed record PercentageResizePolicy(
    decimal Percentage) : ResizePolicy;
```

最终实现可以使用派生 record、私有构造函数与静态工厂，或其他等价的封闭模型，但必须让 Pixel/Percentage 非法混合在构造阶段不可表达。

语义：

| 模式 | 含义 |
| --- | --- |
| `PixelResizePolicy` | 按用户输入的 width / height 调整尺寸；是否保持比例由 `MaintainAspectRatio` 决定。 |
| `PercentageResizePolicy` | 按百分比缩放，并保持原始比例。 |

约束：

- `PixelResizePolicy + MaintainAspectRatio = false` 时，`Width` 和 `Height` 都必须是正整数；输出严格使用这两个尺寸，允许变形。
- `PixelResizePolicy + MaintainAspectRatio = true` 时，至少提供 `Width` 或 `Height` 之一。
- 保持比例且只提供一边时，另一边按原始宽高比计算。
- 保持比例且同时提供两边时，两者表示最大边界，按 `min(Width / OriginalWidth, Height / OriginalHeight)` 计算缩放比例。
- `PreventUpscaling` 只属于 Pixel 模式，默认 `false`。开启后不得把任何输出边放大到超过原图对应边。
- 开启 `PreventUpscaling` 且保持比例时，如果正常解析结果会放大任意一边，则最终尺寸回退为原图尺寸。
- 开启 `PreventUpscaling` 且关闭保持比例时，两边分别使用 `min(Target, Original)`；因此允许一边缩小、另一边保持原尺寸。
- `PercentageResizePolicy.Percentage` 使用十进制正数，允许小数和大于 `100%` 的放大，例如 `12.5%`、`125%`、`200%`。
- 百分比同时作用于两边，天然保持比例，不再携带 `MaintainAspectRatio`、`Width` 或 `Height`。
- 所有计算后的像素尺寸必须大于 0；小数像素按照 Core 统一的取整规则转换为整数。
- Resize 不裁剪、不补边；裁剪必须使用后续独立模型。

Desktop 第一阶段只提供百分比滑块与整数输入框，两者双向同步；不提供 `25%`、`50%`、`75%` 快捷选择。该取舍只影响 UI，Core 仍接收任意合法正数百分比。

### 8.4.1 尺寸值对象与统一解析

Core 增加两个纯业务值对象：

```csharp
public readonly record struct ImageSize(int Width, int Height);

public sealed record ResolvedResizeSize(
    int Width,
    int Height);
```

- `ImageSize` 表达自动校正 EXIF 方向后的原图逻辑尺寸。
- `ResolvedResizeSize` 表达图片处理器最终必须执行的确定尺寸。
- 两者的 `Width` 和 `Height` 都必须是正整数。
- 宽高乘法和比例计算必须防止数值溢出。
- 缩放比例可以从原始尺寸与解析尺寸派生，不要求作为可写状态重复保存。

Core 提供纯计算语义：

```text
ResizePolicy + ImageSize
    -> Resolve
    -> ResolvedResizeSize
```

解析规则：

```text
Pixel，不保持比例：
  ResolvedWidth  = Width
  ResolvedHeight = Height

Pixel，保持比例，只提供 Width：
  ResolvedWidth  = Width
  ResolvedHeight = round(OriginalHeight * Width / OriginalWidth)

Pixel，保持比例，只提供 Height：
  ResolvedWidth  = round(OriginalWidth * Height / OriginalHeight)
  ResolvedHeight = Height

Pixel，保持比例，同时提供 Width / Height：
  Scale = min(Width / OriginalWidth, Height / OriginalHeight)
  ResolvedWidth  = floor(OriginalWidth * Scale)
  ResolvedHeight = floor(OriginalHeight * Scale)

Percentage：
  Scale = Percentage / 100
  ResolvedWidth  = round(OriginalWidth * Scale)
  ResolvedHeight = round(OriginalHeight * Scale)

Pixel，PreventUpscaling = true：
  保持比例：若上述结果任一边大于原图对应边，则回退为 OriginalWidth × OriginalHeight
  不保持比例：ResolvedWidth = min(ResolvedWidth, OriginalWidth)
             ResolvedHeight = min(ResolvedHeight, OriginalHeight)
```

只提供一边时，该边严格等于用户输入；同时提供两个最大边界时使用向下取整，保证结果不超过任一边界。启用禁止放大后，由上述 Pixel 规则对解析结果进行最后钳制。百分比计算使用统一的中点远离零取整；任何结果最小钳制为 `1 × 1`。即使禁止放大最终得到原图尺寸，任务仍按用户显式操作正常编码并生成输出，不映射为 `Skipped`。

Workflow 在 Probe 后把逻辑 `ImageSize` 和 `ResizePolicy` 交给 Core 解析，再根据 Imaging Capabilities 检查极端尺寸、内存或引擎限制。Core 不冻结特定图片库的最大尺寸常量。

图片处理器只接收 `ResolvedResizeSize.Width / Height`，不再自行解释保持比例、双边最小约束或百分比，避免不同图片引擎产生不一致结果。

### 8.4.2 SameFormatEncodingPolicy

Resize 和 Crop 都保持输入图片格式，但两者仍需要重新编码。Core 使用共享策略冻结有损格式质量与元数据行为：

```csharp
public sealed record SameFormatEncodingPolicy(
    ImageQuality LossyQuality,
    MetadataPolicy MetadataPolicy);
```

约束：

- JPEG / WebP 等有损格式使用 `LossyQuality`。
- PNG、BMP 等无损格式忽略 `LossyQuality`，但仍执行 `MetadataPolicy`。
- 输出格式与输入格式一致；该策略不承担格式转换。
- Resize/Crop 处理面板不暴露质量控件；Desktop 或默认设置流程必须把已经解析完成的明确策略传入正式 Workflow Request。
- 第一阶段产品值固定为 `LossyQuality = 90`，但仍作为明确请求字段传递，避免依赖图片引擎默认值；MVP UI 不允许编辑它。
- 默认 `MetadataPolicy = Remove`，并允许用户通过公共处理默认值修改；运行中的 Request 使用提交时快照，不受随后设置变更影响。
- 多帧或动画输入在 Job 创建前拒绝，不进入同格式重新编码。

该策略由 Resize 与 Crop 共用，不能复用 `CompressionProfile` 或 `ConversionProfile`，避免再次把四个同级功能耦合成附属选项。

### 8.5 MetadataPolicy

`MetadataPolicy` 表达压缩、转换、Resize 或 Crop 输出时，如何处理拍摄信息、位置数据和描述性元数据。ICC 色彩配置属于保证颜色正确显示的渲染信息，不纳入这个二选一策略。

第一阶段模型：

```csharp
public enum MetadataPolicy
{
    Preserve,
    Remove
}
```

语义：

| 策略 | 含义 |
| --- | --- |
| `Preserve` | 在目标格式支持的范围内，尽量保留仍然有效的 EXIF、GPS、拍摄时间、相机信息、IPTC、XMP、注释和内嵌缩略图。 |
| `Remove` | 删除上述拍摄、位置和描述性信息，以减少隐私泄漏并降低部分体积。 |

两项互斥，每个处理请求必须选择且只能选择一个。Desktop 可以用一个“移除拍摄信息与位置数据”复选框表达：勾选映射 `Remove`，未勾选映射 `Preserve`。

共同规则：

- ICC / ICM 色彩配置在两种策略下都应尽量保留；不能因为选择 `Remove` 而直接删除仍用于解释像素颜色的 Profile。
- 图片在写出前统一完成 AutoOrient。像素方向已经校正后，EXIF Orientation 必须删除或规范为 TopLeft，不能在 `Preserve` 下保留失效的旧方向值。
- `Preserve` 是语义保留而不是原始字节逐项复制。目标格式无法表达的字段可以丢失；因处理而失效的尺寸、缩略图或方向字段必须更新或移除。
- 第一阶段不提供“极限瘦身并删除 ICC”选项；如未来需要，应作为独立的色彩配置策略设计，不能复用 `MetadataPolicy.Remove` 偷偷实现。

### 8.6 默认压缩方案

第一阶段内置模式的初始默认组合建议：

| 模式 | 质量 |
| --- | --- |
| `Smart` | 由格式策略决定 |
| `HighQuality` | `90` |
| `Balanced` | `80` |
| `Maximum` | `65` |
| `Custom` | 用户指定 |

默认行为说明：

- 压缩模式只决定质量或编码策略，不决定元数据策略。
- 新安装或恢复默认值时，公共 `MetadataPolicy` 为 `Remove`；设置页面修改时同时更新压缩、转换和同格式编码三个默认 Profile 的该字段。
- 页面切换 Smart、HighQuality、Balanced、Maximum 或 Custom 时保留当前独立元数据选择，不因模式变化重置；任何选择都不删除 ICC。
- 保存 `Custom` 为默认方案时，`DefaultCompressionProfile` 必须同时保存其合法 `Quality`；只保存模式而遗漏质量属于非法设置。

### 8.7 设计边界

- Core 只表达压缩意图和业务约束，不调用图片库。
- 具体图片格式如何映射 `CompressionProfile`，由 `AtomPix.Imaging.Magick` 实现。
- Desktop 可以把 `CompressionProfile` 展示为内置模式、表单或唯一默认配置，但不能改变 Core 的业务语义；用户命名预设不进入 MVP。
- Workflows 使用 `CompressionProfile` 编排压缩流程，并在入口执行参数校验和结果组织。
- 当前源代码中的 `CompressionProfile.ResizePolicy` 属于待迁移的旧组合契约；目标设计将其拆到独立 `ResizeImageWorkflow`。
## 9. 转换配置模型基线

`ConversionProfile` 表达 AtomPix 的格式转换方案。

它和 `CompressionProfile` 的区别在于：

```text
CompressionProfile = 怎么在保持格式和尺寸的前提下优化体积
ConversionProfile = 怎么把图片编码为指定输出格式
```

二者在底层图片处理上可能都表现为“读取图片、处理图片、写出图片”，但业务语义不同：

- 用户触发“压缩”时，使用 `CompressionProfile`。
- 用户触发“转换格式”时，使用 `ConversionProfile`。

### 9.1 ConversionProfile

建议模型：

```csharp
public sealed record ConversionProfile(
    OutputImageFormat OutputFormat,
    ImageQuality? Quality,
    MetadataPolicy MetadataPolicy,
    TransparencyPolicy TransparencyPolicy);
```

语义：

- `OutputFormat` 表示目标输出格式，转换流程必须指定。
- `Quality` 表示目标格式支持有损压缩时的质量参数，例如 JPEG / WebP。
- `MetadataPolicy` 表示转换输出时如何处理拍摄、位置和描述性元数据；ICC 独立保留，AutoOrient 后方向信息始终规范化。
- `TransparencyPolicy` 表示目标格式不能表达透明像素时使用的确定性铺底策略。

透明区域规则不交给图片引擎默认行为决定：

```text
源图没有真实透明像素 -> 不执行透明度处理
源图有真实透明像素 + 目标支持 Alpha -> 保留透明度
源图有真实透明像素 + 目标不支持 Alpha -> 按 TransparencyPolicy 铺底
```

第一阶段 PNG / WebP 支持透明度，JPEG 不支持透明度。第一阶段不提供“主动把 PNG / WebP 去透明”的模式。

### 9.1.1 RgbColor 与 TransparencyPolicy

`RgbColor` 是 Core 自有的不透明 sRGB 颜色值对象：

```csharp
public sealed record RgbColor(byte Red, byte Green, byte Blue)
{
    public static RgbColor White { get; } = new(255, 255, 255);
    public static RgbColor Black { get; } = new(0, 0, 0);
}

public sealed record TransparencyPolicy(
    RgbColor OpaqueBackgroundColor)
{
    public static TransparencyPolicy Default { get; } = new(RgbColor.White);
}
```

约束：

- 文本表示统一为大写六位 `#RRGGBB`；解析和格式化不能接受或产生 Alpha 分量。
- `OpaqueBackgroundColor` 不能为空，默认值固定为白色 `#FFFFFF`。
- Core 不引用 Avalonia `Color`、Magick.NET `MagickColor` 或存储格式专用类型。
- `TransparencyPolicy` 不包含模式枚举；是否保留或铺底由真实透明像素和目标格式能力唯一决定。

### 9.2 OutputImageFormat

第一阶段输出格式：

```csharp
public enum OutputImageFormat
{
    Jpeg,
    Png,
    WebP
}
```

这些格式是 AtomPix 第一阶段对用户承诺的输出格式，属于产品能力，因此放在 Core 中表达。

输入格式识别更偏图片探测和图片引擎能力，优先放在 `AtomPix.Imaging.Abstractions` 的 `ImageFormatKind` 中表达。

### 9.3 与 CompressionProfile 的复用关系

`CompressionProfile` 和 `ConversionProfile` 复用以下基础对象：

```text
ImageQuality
MetadataPolicy
```

复用原因：

- JPEG / WebP 的压缩质量在“压缩”和“转换”中都需要表达。
- 元数据保留或移除是输出行为，不只属于压缩，也属于转换。

复用边界：

| 对象 | 压缩流程 | 转换流程 |
| --- | --- | --- |
| `ImageQuality` | 表达压缩质量。 | 表达目标格式输出质量。 |
| `MetadataPolicy` | 表达压缩输出是否保留拍摄、位置和描述性信息；不控制 ICC。 | 表达转换输出是否保留拍摄、位置和描述性信息；不控制 ICC。 |

### 9.4 二者差异

| 维度 | `CompressionProfile` | `ConversionProfile` |
| --- | --- | --- |
| 主要目标 | 减小文件体积。 | 改变图片格式。 |
| 是否改变格式 | 否，必须保持探测得到的输入格式。 | 必须指定目标格式。 |
| 是否有压缩模式 | 有，`Smart` / `HighQuality` / `Balanced` / `Maximum` / `Custom`。 | 无，直接指定目标格式和输出参数。 |
| 是否有目标格式 | 没有可选目标格式；输出格式固定等于输入格式。 | 必须有 `OutputFormat`。 |
| 是否可调整尺寸 | 否；进入独立“调整尺寸”任务。 | 否；进入独立“调整尺寸”任务。 |
| 是否可处理元数据 | 可以。 | 可以。 |
| 是否处理透明区域 | 保持原格式自身能力。 | 支持 Alpha 的目标保留；不支持 Alpha 的目标按明确背景色铺底。 |

批量转换共享一个目标格式，因此某个输入可能本来就是该格式。此时仍按 `ConversionProfile` 重新编码并应用质量、元数据和透明度策略，不产生特殊 Skipped/NoOp 状态；“转换”在契约上表示指定输出格式，不要求每个项目的格式枚举都发生变化。

### 9.5 设计边界

- Core 只表达转换意图，不调用图片库。
- 具体输出格式如何映射到 Magick.NET 编码参数，由 `AtomPix.Imaging.Magick` 实现。
- `ConversionProfile` 不表达输入格式；输入格式由图片探测结果决定。
- Workflows 使用 `ConversionProfile` 编排转换流程，并负责输出路径、覆盖策略和结果组织。
- Desktop 可以把 `ConversionProfile` 展示为格式选择器、质量设置、元数据设置和条件透明背景色设置。
- 当前源代码中的 `ConversionProfile.ResizePolicy` 属于待迁移的旧组合契约；目标设计将其拆到独立 `ResizeImageWorkflow`。
## 10. 输出策略模型基线

输出策略用于回答处理后的文件如何落盘：

```text
处理后的文件放哪里？
文件名叫什么？
如果重名怎么办？
```

第一阶段输出策略拆为四个核心概念：

```text
OutputPolicy
OutputLocationPolicy
OutputNamingPolicy
OverwritePolicy
```

### 10.1 OutputPolicy

`OutputPolicy` 是完整输出行为的聚合对象。

建议模型：

```csharp
public sealed record OutputPolicy(
    OutputLocationPolicy LocationPolicy,
    OutputNamingPolicy NamingPolicy,
    OverwritePolicy OverwritePolicy);
```

语义：

- `LocationPolicy` 决定输出目录。
- `NamingPolicy` 决定输出文件名。
- `OverwritePolicy` 决定目标文件已存在时如何处理。

### 10.2 OutputLocationPolicy

`OutputLocationPolicy` 表达输出位置策略。

建议模型：

```csharp
public sealed record OutputLocationPolicy(
    OutputLocationMode Mode,
    string? CustomDirectory,
    string? SubfolderName);
```

```csharp
public enum OutputLocationMode
{
    SameAsInput,
    Subfolder,
    CustomDirectory
}
```

语义：

| 模式 | 含义 |
| --- | --- |
| `SameAsInput` | 输出到原图所在目录。 |
| `Subfolder` | 输出到原图目录下的子文件夹。 |
| `CustomDirectory` | 输出到用户选择的固定目录。 |

约束：

- `SameAsInput` 模式下，`CustomDirectory` 和 `SubfolderName` 应为空。
- `Subfolder` 模式下，`SubfolderName` 必须有值，`CustomDirectory` 应为空。
- `CustomDirectory` 模式下，`CustomDirectory` 必须有值，`SubfolderName` 应为空。

第一阶段默认：

```text
Mode = Subfolder
SubfolderName = AtomPix_Output
CustomDirectory = null
```

默认使用子目录是为了避免污染原图目录，降低误覆盖源文件的风险。

### 10.3 OutputNamingPolicy

`OutputNamingPolicy` 表达输出文件基础命名策略。UI 中的“保留原名”和“追加 `_atompix`”是稳定预设，“自定义格式”允许使用受控占位符；扩展名始终由任务输出格式决定，不属于命名格式。

建议模型：

```csharp
public sealed record OutputNamingPolicy(
    OutputNamingMode Mode,
    string? Suffix,
    string? Pattern);
```

```csharp
public enum OutputNamingMode
{
    KeepOriginalName,
    AppendSuffix,
    CustomPattern
}
```

语义：

| 模式 | 含义 |
| --- | --- |
| `KeepOriginalName` | 保留原文件名，只根据输出格式改变扩展名。 |
| `AppendSuffix` | 在原文件名后追加后缀，再根据输出格式改变扩展名。 |
| `CustomPattern` | 使用用户输入的文件名格式，并展开受支持占位符。 |

约束：

- `KeepOriginalName` 模式下，`Suffix` 与 `Pattern` 均为空，基础格式等价于 `{name}`。
- `AppendSuffix` 模式下，`Suffix` 必须有值、`Pattern` 为空，基础格式等价于 `{name}{Suffix}`。
- `Suffix` 是普通文件名文本，不能包含占位符、目录分隔符或非法文件名字符。
- `CustomPattern` 模式下，`Pattern` 必须有值、`Suffix` 为空。
- 第一阶段只支持 `{name}` 和 `{index}`；未知、未闭合或大小写不匹配的占位符返回 `InvalidOutputNamingPattern / Validation`。
- 格式表达不含扩展名的文件名主体，不能包含目录分隔符或展开后非法的文件名；文件系统相关非法结果返回 `InvalidOutputPath`。
- `{index}` 最多出现一次；`{name}` 可以省略，因此纯文本 `holiday` 是合法自定义格式。

第一阶段默认：

```text
Mode = AppendSuffix
Suffix = _atompix
Pattern = null
```

示例：

```text
photo.jpg -> photo_atompix.jpg
screenshot.png -> screenshot_atompix.webp
```

`_atompix` 同时适用于压缩和格式转换，因此作为第一阶段默认后缀。

占位符语义：

| 占位符 | 展开值 |
| --- | --- |
| `{name}` | 输入文件名主体，不含目录和扩展名。 |
| `{index}` | 任务中的一基序号，至少三位补零。 |

序号宽度为 `max(3, TotalCount 的十进制位数)`：8 项使用 `001..008`，1000 项使用 `0001..1000`。单张任务或只有一项的批量任务不自动增加 `{index}`；用户显式写入时展开为 `001`。

批量任务数量大于 1 时必须得到包含序号的有效格式：

```text
BasePattern 包含 {index}
    -> EffectivePattern = BasePattern

BasePattern 不包含 {index}
    -> EffectivePattern = BasePattern + _{index}
```

因此默认批量格式为 `{name}_atompix_{index}`；自定义纯文本 `holiday` 的实际格式为 `holiday_{index}`。这是确定性的批量命名规则，不属于目标文件已经存在时的 `AutoRename`。

### 10.4 OverwritePolicy

`OverwritePolicy` 表达目标文件已存在时的处理方式。

第一阶段模型：

```csharp
public enum OverwritePolicy
{
    Skip,
    Overwrite,
    AutoRename
}
```

语义：

| 策略 | 含义 |
| --- | --- |
| `Skip` | 目标文件存在时跳过该项。 |
| `Overwrite` | 目标文件存在时直接覆盖。 |
| `AutoRename` | 目标文件存在时自动重命名。 |

第一阶段默认：

```text
AutoRename
```

默认自动重命名是为了避免覆盖用户文件，同时避免因为目标文件存在而打断批量处理。

自动重命名示例：

```text
photo_atompix.jpg
photo_atompix_1.jpg
photo_atompix_2.jpg
```

### 10.5 默认输出策略

第一阶段默认 `OutputPolicy`：

```text
LocationPolicy:
  Mode = Subfolder
  SubfolderName = AtomPix_Output
  CustomDirectory = null

NamingPolicy:
  Mode = AppendSuffix
  Suffix = _atompix
  Pattern = null

OverwritePolicy:
  AutoRename
```

输入示例：

```text
D:\Pictures\photo.jpg
```

默认输出：

```text
D:\Pictures\AtomPix_Output\photo_atompix.jpg
```

如果目标文件已存在：

```text
D:\Pictures\AtomPix_Output\photo_atompix_1.jpg
```

### 10.6 设计边界

- Core 定义输出策略和业务约束，不直接创建目录或写文件。
- Workflows 使用 `OutputPolicy` 计算和校验输出意图，并调用外部端口或图片处理契约完成实际输出。
- Infrastructure 可提供文件系统、路径解析、目录创建等能力。
- Desktop 可以把 `OutputPolicy` 展示为输出目录选择、文件名规则和重名处理选项。
- 第一阶段允许用户选择覆盖，但默认绝不覆盖源文件或已有目标文件。
- 批量数量大于 1 时，Core 命名规则保证 `EffectivePattern` 包含 `{index}`；Workflow 仍必须验证展开后的计划路径在批次内唯一，不能依赖 UI 提示保证。

源文件保护是 `OverwritePolicy` 之上的强制不变量：`Overwrite` 只能替换不属于本次任务输入集合的目标文件，不能授权原地覆盖输入图片。单张计划输出等于输入路径，或批量任一计划输出命中该批次任意输入路径时，使用 `OutputPathConflictsWithInput / Validation` 在 Job 创建前拒绝。错误 `Details` 至少携带规范化输入路径、计划输出路径；批量同时携带 `ConflictCount`，无需塞入无限长度的完整路径列表。

`AutoRename` 和 `Skip` 不违反该不变量：输入文件本身被视为已经存在的路径，因此前者选择新名称，后者形成 `Skipped + OutputFileAlreadyExists`。第一阶段路径身份只按规范化绝对路径和平台大小写规则判断，不扩展到符号链接或硬链接文件身份。
## 11. 图片任务模型基线

任务模型用于表达 AtomPix 中单张图片和批量图片处理的状态、结果和统计。

这部分回答：

```text
一张图的处理任务怎么表达？
一批图的处理任务怎么表达？
任务有哪些状态？
成功、失败、取消、跳过、部分成功如何记录？
```

第一阶段任务模型包含：

```text
ImageJob
ImageJobType
ImageJobStatus
ImageJobResult
BatchJob
BatchJobStatus
BatchResult
ImageJobId
BatchJobId
```

### 11.1 ImageJobType

第一阶段任务类型：

```csharp
public enum ImageJobType
{
    Compress,
    Convert,
    Resize,
    Crop
}
```

设计约束：

- 预览不进入任务队列；预览属于浏览体验，不属于批量处理任务。
- 压缩、转换、调整尺寸和裁剪是第一阶段同级单张任务类型；批量流程只开放压缩、转换和调整尺寸，批量裁剪暂缓。
- 水印等后续能力如进入批量导出流程，再扩展新的任务类型。

### 11.1.1 CropRectangle

`CropRectangle` 表达自动校正 EXIF 方向后的原图逻辑坐标系中，已经解析完成、可以直接执行的像素矩形：

```csharp
public sealed record CropRectangle(
    int X,
    int Y,
    int Width,
    int Height);
```

约束：

- 坐标原点位于左上角。
- `X` 和 `Y` 必须大于等于 0。
- `Width` 和 `Height` 必须大于 0。
- `X + Width` 不能超过逻辑原图 width，`Y + Height` 不能超过逻辑原图 height。
- 边界计算必须防止整数溢出。
- Crop 只提取矩形区域，不执行 Resize、补边或格式转换。
- `CropRectangle` 不携带比例；`Width / Height` 已经唯一决定实际像素比例。

单张自由裁剪和比例裁剪最终都提交 `CropRectangle`。比例只在 Desktop 编辑选框时作为约束，不重复进入最终执行策略，避免“声明比例”与实际矩形互相矛盾。

`CropRectangle` 自身只能守住坐标和尺寸的内在不变量；是否位于某张图片内属于带上下文的 Core 纯规则：

```csharp
OperationResult<CropRectangle> ValidateCropRectangle(
    ImageSize inputSize,
    CropRectangle cropArea);
```

- 校验使用自动方向校正后的逻辑 `ImageSize`。
- 使用扩大精度计算 `X + Width` 和 `Y + Height`，不能发生整数溢出。
- 合法时原样返回同一个确定矩形，不做钳制、平移、缩放或比例重算。
- 越界时返回 `InvalidCropOptions`；Workflow 在创建 Job 前调用该规则。

### 11.1.2 CropAspectRatio

`CropAspectRatio` 表达生成或约束裁剪矩形的业务意图：

```csharp
public sealed record CropAspectRatio(
    int WidthUnits,
    int HeightUnits);
```

约束：

- `WidthUnits` 和 `HeightUnits` 必须为正整数。
- 比例在构造时按最大公约数归一化，例如 `6:4` 归一为 `3:2`。
- 第一阶段比例预设为 `3:2`、`4:3`、`5:4`、`1:1`、`4:5`、`3:4`、`2:3`。
- 预设按钮属于 Desktop 展示；Core 值对象可以表达其他合法正数比例。
- `CropAspectRatio` 本身不能交给图片处理器执行，必须先解析为 `CropRectangle`。

### 11.1.3 批量裁剪方案备忘（MVP 暂缓）

> 本节只保留此前讨论结论，属于非规范后续备忘。`BatchCropPolicy`、`BatchCropPlacement` 不进入当前 Core 公共 API、Feature、Workflow 或测试基线；MVP 批量任务只有压缩、转换和调整尺寸。

后续如果根据真实需求重新启动批量裁剪，可以考虑以下候选模型：

```csharp
public sealed record BatchCropPolicy(
    CropAspectRatio AspectRatio,
    BatchCropPlacement Placement);

public enum BatchCropPlacement
{
    CenterMaximum
}
```

候选方案只支持 `CenterMaximum`：针对每张图片，在逻辑原图范围内生成目标比例下覆盖面积最大且居中的 `CropRectangle`。比例无法精确换算成整数像素时，Core 保留限制边的最大可用长度，另一边向下取整；最终 `CropRectangle` 是执行事实。

```text
BatchCropPolicy + ImageSize
    -> Core Resolve
    -> CropRectangle
```

设逻辑原图尺寸为 `OriginalWidth / OriginalHeight`，归一化比例为 `RatioWidth / RatioHeight`。Core 使用扩大精度的整数或十进制运算比较比例，避免交叉乘法溢出：

```text
如果 OriginalWidth / OriginalHeight >= RatioWidth / RatioHeight：
  CropHeight = OriginalHeight
  CropWidth  = floor(OriginalHeight * RatioWidth / RatioHeight)
否则：
  CropWidth  = OriginalWidth
  CropHeight = floor(OriginalWidth * RatioHeight / RatioWidth)

X = floor((OriginalWidth  - CropWidth)  / 2)
Y = floor((OriginalHeight - CropHeight) / 2)
```

规则说明：

- 不为了得到数学上严格整除的比例而额外缩短限制边。例如 `1000 × 1000` 按 `3:2` 生成 `1000 × 666`，而不是 `999 × 666`。
- 居中余量为奇数时，`X / Y` 向下取整，多出的一个像素稳定保留在右侧或下侧。
- 如果任一计算尺寸向下取整后为 `0`，解析返回 `InvalidCropOptions`，不能强制钳制为 `1` 后改变用户要求的比例。
- Imaging 只执行最终矩形，不重复计算比例、尺寸或居中位置。

基线示例：

| 原图 | 比例 | 结果矩形 |
| --- | --- | --- |
| `1000 × 1000` | `3:2` | `X=0, Y=167, Width=1000, Height=666` |
| `1001 × 1000` | `1:1` | `X=0, Y=0, Width=1000, Height=1000`，多余 1 px 在右侧 |
| `4000 × 3000` | `16:9` | `X=0, Y=375, Width=4000, Height=2250` |
| `1 × 1000` | `16:9` | 高度解析为 0，返回 `InvalidCropOptions` |

在该候选方案中，每张图片单独调整后的矩形属于 Workflow Request 的逐项覆盖数据，不写回共享 `BatchCropPolicy`。没有覆盖值的图片继续使用 Core 根据共享比例生成的默认矩形。

最终边界：

| 场景 | 比例位于哪里 | 最终执行数据 |
| --- | --- | --- |
| 单张自由裁剪 | Desktop UI 状态为空 | `CropRectangle` |
| 单张比例裁剪 | Desktop 用比例约束选框 | `CropRectangle` |
| 批量默认裁剪（后续备忘） | `BatchCropPolicy` | 每张图片解析出的 `CropRectangle` |
| 批量单项调整（后续备忘） | 保留共享策略，Workflow 保存该项覆盖 | 该图片自己的 `CropRectangle` |
| Imaging 执行 | 不接收比例 | `CropRectangle` |

### 11.2 ImageJobStatus

单项任务状态：

```csharp
public enum ImageJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled,
    Skipped
}
```

语义：

| 状态 | 含义 |
| --- | --- |
| `Pending` | 等待处理。 |
| `Running` | 正在处理。 |
| `Succeeded` | 处理成功。 |
| `Failed` | 处理失败。 |
| `Canceled` | 被用户或系统取消。 |
| `Skipped` | 根据策略跳过，例如目标文件存在且选择 `Skip`。 |

第一阶段不设计 `Paused`、`Interrupted`、`Retrying` 状态。暂停、恢复、关机后恢复等能力暂缓。

MVP 中 `ImageJobStatus.Skipped` 的唯一正式来源是：Workflow 已经解析出目标路径，目标文件存在，且请求中的 `OverwritePolicy = Skip`。此时不调用图片处理器，ImageJob 直接从 Pending 进入 Skipped。输入收集时被过滤的候选文件不属于 ImageJob，也不能计入该状态。

### 11.3 ImageJobId / BatchJobId

任务 ID 使用值对象，不在系统内长期传递裸 `Guid`。

建议模型：

```csharp
public readonly record struct ImageJobId(Guid Value);

public readonly record struct BatchJobId(Guid Value);
```

### 11.4 ImageJob

`ImageJob` 表达单张图片处理任务的状态。

建议模型：

```csharp
public sealed class ImageJob
{
    public ImageJobId Id { get; }
    public ImageJobType Type { get; }
    public string InputPath { get; }
    public string? OutputPath { get; private set; }
    public ImageJobStatus Status { get; private set; }
    public AtomPixError? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
}
```

设计约束：

- `InputPath` 创建任务时已知。
- `OutputPath` 可以为空，因为最终输出路径可能需要执行前根据输出策略、目标格式和重名处理结果解析。
- 单项 `ImageJob` 不直接保存 `CompressionProfile` 或 `ConversionProfile`。
- 一个批次共享其任务类型对应的压缩、转换或 Resize 配置，配置应由 Workflow 请求或不可变执行上下文持有。
- 如果后续支持每张图片不同配置，再扩展任务模型。

### 11.5 OutputPath 解析边界

单项任务的最终输出路径由多个因素共同决定：

```text
InputPath
OutputPolicy
任务类型
目标格式
OverwritePolicy 的实际处理结果
```

示例：

```text
InputPath = D:\Pictures\photo.jpg
OutputPolicy = Subfolder + _atompix + AutoRename
ConversionProfile.OutputFormat = WebP
```

最终输出可能是：

```text
D:\Pictures\AtomPix_Output\photo_atompix.webp
```

如果目标文件已存在，且策略为 `AutoRename`，最终输出可能是：

```text
D:\Pictures\AtomPix_Output\photo_atompix_1.webp
```

约束：

- `OutputPolicy` 不单独决定 `OutputPath`。
- `ConversionProfile` 决定转换任务的目标扩展名。
- 压缩任务的扩展名固定来自探测得到的输入格式；`CompressionProfile` 不决定扩展名，也不允许转格式。
- Workflows 负责结合任务类型、对应 Profile/Policy、`OutputPolicy`、输入路径和文件系统状态解析最终输出路径。
- Infrastructure 提供存在性查询、路径组合、目录创建和索引路径构造等端口实现；是否 Skip、Overwrite 或 AutoRename 仍由 Workflows 决策。

### 11.6 BatchJobStatus

批量任务需要表达部分成功，因此不复用 `ImageJobStatus`。

建议模型：

```csharp
public enum BatchJobStatus
{
    Pending,
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    Canceled
}
```

语义：

| 状态 | 含义 |
| --- | --- |
| `Pending` | 批量任务等待开始。 |
| `Running` | 批量任务正在执行。 |
| `Succeeded` | 所有单项均成功或按策略正常跳过，没有失败和取消。 |
| `PartiallySucceeded` | 已产生至少一个成功项，同时存在失败项，或批量级错误导致提前中止。 |
| `Failed` | 没有成功项且存在失败，或批量级错误在产生成功项前终止任务。 |
| `Canceled` | 用户取消了批量流程，结果中保留已完成项。 |

规则：

- 全部 `Succeeded` -> `BatchJobStatus.Succeeded`。
- `Succeeded` 与 `Skipped` 混合且没有失败 -> `BatchJobStatus.Succeeded`。
- 全部 `Skipped` -> `BatchJobStatus.Succeeded`，但统计中显示 skipped 数量。
- 部分 `Succeeded`，部分 `Failed`，可以同时存在 `Skipped` -> `BatchJobStatus.PartiallySucceeded`。
- `Failed` 与 `Skipped` 混合但没有 `Succeeded` -> `BatchJobStatus.Failed`。
- 全部 `Failed` -> `BatchJobStatus.Failed`。
- 用户取消批量流程 -> `BatchJobStatus.Canceled`，无论取消前是否已有成功项。
- 批量级错误导致提前中止时，已有成功项 -> `PartiallySucceeded`；没有成功项 -> `Failed`。

`Skipped` 是 OutputPolicy 产生的正常策略结果，不属于失败，也不会单独把批次降级为 `PartiallySucceeded`。已经结束的 ImageJob 或 BatchJob 不允许重新迁移到 Pending/Running；所谓重试、处理未完成项或处理跳过项都必须创建新的 Job。

### 11.7 BatchJob

`BatchJob` 表达一批图片处理任务的运行状态。

建议模型：

```csharp
public sealed class BatchJob
{
    public BatchJobId Id { get; }
    public ImageJobType Type { get; }
    public IReadOnlyList<ImageJob> Items { get; }
    public BatchJobStatus Status { get; private set; }
    public AtomPixError? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
}
```

设计约束：

- `BatchJob` 可以作为一批任务共享 Profile 和 OutputPolicy 的上下文。
- 第一阶段批量任务不支持暂停和恢复。
- 第一阶段批量任务允许取消。
- 批量任务允许部分成功。
- `Error` 只表达批量级取消或中止原因，不能替代各个 `ImageJob.Error`。
- 自然完成、取消和批量级中止必须通过不同的意图型迁移操作表达；调用方不能任意传入一个终态绕过 Core 汇总规则。
- Workflow 如何驱动这些迁移，以 [Workflow 任务状态机编排设计](../workflows/job-state-orchestration.md) 为准。

### 11.8 ImageJobResult

单项任务结果：

```csharp
public sealed record ImageJobResult(
    ImageJobId JobId,
    ImageJobType Type,
    string InputPath,
    string? OutputPath,
    ImageJobStatus Status,
    long? InputSizeBytes,
    long? OutputSizeBytes,
    AtomPixError? Error);
```

语义：

- `InputSizeBytes` 表示输入文件大小。
- `OutputSizeBytes` 表示输出文件大小，失败、取消或跳过时可以为空。
- `Error` 只在失败、取消或需要解释跳过原因时使用。
- MVP 的 `Skipped` 结果必须保留计划目标 `OutputPath`，并使用 `OutputFileAlreadyExists` 解释跳过原因；不能只返回一个无法解释的空 Error。
- 体积差值、比例和变化类型由输入/输出大小派生，不保存可能相互矛盾的重复事实。

派生统计：

```csharp
public enum FileSizeChangeKind
{
    Reduced,
    Unchanged,
    Increased
}

SizeDeltaBytes = OutputSizeBytes - InputSizeBytes
SizeDeltaRatio = SizeDeltaBytes / InputSizeBytes
```

- `SizeDeltaBytes < 0` 派生 `Reduced`，等于 `0` 派生 `Unchanged`，大于 `0` 派生 `Increased`。
- `SizeDeltaBytes`、`SizeDeltaRatio` 和 `FileSizeChangeKind? SizeChangeKind` 是 `ImageJobResult` 的只读计算属性，不进入构造参数或持久化结构。
- 输入或输出大小任一缺失时，三项均为 `null`；输入大小等于 `0` 时差值仍可计算，但比例为 `null`。
- 原 `SavedBytes / SavedRatio` 直接废止，不保留兼容别名。

### 11.9 BatchResult

批量任务结果：

```csharp
public sealed record BatchResult(
    BatchJobId BatchId,
    ImageJobType Type,
    BatchJobStatus Status,
    int TotalCount,
    IReadOnlyList<ImageJobResult> Items,
    AtomPixError? Error);
```

建议派生统计：

```text
TotalCount
SucceededCount
FailedCount
SkippedCount
CanceledCount
SizeComparedItemCount
ProcessedInputSizeBytes
ProcessedOutputSizeBytes
TotalSizeDeltaBytes
TotalSizeDeltaRatio
TotalSizeChangeKind
ReducedItemCount
UnchangedItemCount
IncreasedItemCount
```

设计约束：

- 批量流程整体返回 `OperationResult<BatchResult>`。
- 整体 `Failure` 表示批量流程在创建 Job 前被拒绝，例如输入为空或输出目录非法；Workflow 将其投影为 `StartRejected`。
- 整体 `Success` 表示批量流程成功启动并完成调度或执行。
- 单项失败记录在 `BatchResult.Items`，不直接导致整体 `OperationResult<BatchResult>` 失败。
- 批量结果允许部分成功、部分失败。
- `Error` 只表示批量级取消或中止原因；单项错误仍保存在对应 `ImageJobResult.Error`。
- Job 创建前的拒绝通过 Workflow `StartRejected` 返回，不构造 `BatchResult`。
- 已接受任务在第一项开始前取消或发生批量级错误时，`Items` 可以为空，但 `TotalCount` 仍保留原始计划数量。
- 体积统计只使用 `Succeeded` 且 `InputSizeBytes / OutputSizeBytes` 同时存在的项目；`SizeComparedItemCount` 是该集合的数量。
- `ProcessedInputSizeBytes / ProcessedOutputSizeBytes` 只对上述集合求和；集合为空时两者为 `0`，但不得据此声称“大小未变化”。
- `TotalSizeDeltaBytes = ProcessedOutputSizeBytes - ProcessedInputSizeBytes`，`TotalSizeDeltaRatio` 以 `ProcessedInputSizeBytes` 为分母；没有可比较项目或分母为零时比例为 `null`。
- 没有可比较项目时 `TotalSizeDeltaBytes / TotalSizeChangeKind` 为 `null`；否则按差值的负、零、正派生总体 `Reduced / Unchanged / Increased`。
- 三类 ItemCount 按每个可比较项目自己的 `SizeChangeKind` 计数，三者之和必须等于 `SizeComparedItemCount`。
- Failed、Canceled、Skipped 和未开始项目不参与任何体积和比例聚合，即使它们保留了输入大小；缺失输出大小不能按 `0` 代入。
- 原 `TotalInputSizeBytes / TotalOutputSizeBytes / TotalSavedBytes / TotalSavedRatio` 作为旧的混合口径直接废止，不保留兼容别名。

### 11.10 设计边界

- Core 定义任务状态、结果和基础状态流转规则。
- Workflows 负责创建任务、编排执行、调用 Core 意图型迁移并发布结果；终态汇总规则由 Core 维护。
- Imaging.Abstractions 负责图片处理契约，不关心批量任务模型。
- Imaging.Magick 只处理单次图片操作，不知道批量任务规则。
- Infrastructure 可提供任务历史持久化能力，但第一阶段不要求关机后恢复任务。
- Desktop 负责展示当前批次的项目列表、进度、统计和错误，不直接改变任务业务语义；第一阶段不提供多批次任务队列。

## 12. 应用设置模型基线

`AppSettings` 表达 AtomPix 第一阶段需要持久化的用户偏好。

它只保存会影响用户体验和处理结果的稳定设置，不保存任务历史、最近记录列表本体或窗口布局等高频变化数据。

### 12.1 AppSettings

建议模型：

```csharp
public sealed record AppSettings(
    int SchemaVersion,
    CompressionProfile DefaultCompressionProfile,
    ConversionProfile DefaultConversionProfile,
    SameFormatEncodingPolicy DefaultSameFormatEncodingPolicy,
    OutputPolicy DefaultOutputPolicy,
    ThemeMode ThemeMode,
    string? Language,
    RecentItemsSettings RecentItems);
```

语义：

- `DefaultCompressionProfile` 表示默认压缩方案。
- `DefaultConversionProfile` 表示默认格式转换方案。
- `DefaultSameFormatEncodingPolicy` 表示 Resize/Crop 保持原格式重新编码时的默认质量与元数据策略。
- `DefaultOutputPolicy` 表示默认输出目录、命名和覆盖策略。
- `ThemeMode` 表示应用主题。
- `Language` 表示语言设置，`null` 表示跟随系统。
- `RecentItems` 表示最近打开记录的偏好设置。

三个默认 Profile 中的 `MetadataPolicy` 是一个逻辑设置的请求就绪投影，必须始终相等：

```text
DefaultCompressionProfile.MetadataPolicy
  = DefaultConversionProfile.MetadataPolicy
  = DefaultSameFormatEncodingPolicy.MetadataPolicy
```

设置页面的公共开关一次更新三处。读取到不一致的持久化值视为 `SettingsLoadFailed`，不能任意选择其中一个；单次处理面板仍可只覆盖当前请求的 MetadataPolicy，且不写回默认设置。

### 12.2 ThemeMode

主题模式：

```csharp
public enum ThemeMode
{
    System,
    Light,
    Dark
}
```

第一阶段默认：

```text
System
```

即默认跟随系统主题。

### 12.3 Language

语言设置第一阶段使用：

```csharp
string? Language
```

默认：

```text
null
```

语义：

- `null` 表示跟随系统语言。
- 后续如需明确支持固定语言，可约定为 `zh-CN`、`en-US` 等 culture name。
- 第一阶段不提前设计复杂本地化模型。

### 12.4 RecentItemsSettings

最近打开记录设置：

```csharp
public sealed record RecentItemsSettings(
    bool Enabled,
    int MaxCount);
```

第一阶段默认：

```text
Enabled = true
MaxCount = 20
```

约束：

- `RecentItemsSettings` 只保存最近记录功能是否启用，以及最多保留多少条。
- 最近文件和最近目录列表本体不放入 `AppSettings`。
- 最近记录列表变化频繁，后续由 Infrastructure 使用独立存储管理，例如 `recent-items.json`。

### 12.5 默认压缩配置

第一阶段默认压缩配置：

```text
DefaultCompressionProfile:
  Mode = Smart
  Quality = null
  MetadataPolicy = Remove
```

语义：

- 默认使用智能压缩。
- 默认移除拍摄信息、位置数据和描述性元数据；ICC 色彩配置仍保留。
- 压缩任务不改变尺寸；调整尺寸使用独立任务及其当次参数。
- `Smart` 模式的质量候选由 AtomPix 内置格式策略决定，不进入用户设置；用户若需要指定质量，应把默认模式改为 `Custom` 并同时保存合法 `Quality`。

### 12.6 默认转换配置

第一阶段默认转换配置：

```text
DefaultConversionProfile:
  OutputFormat = WebP
  Quality = 80
  MetadataPolicy = Remove
  TransparencyPolicy.OpaqueBackgroundColor = #FFFFFF
```

选择 WebP 作为默认输出格式的原因：

- AtomPix 的产品方向偏图片压缩和体积优化。
- WebP 对常见图片场景有较好的体积表现。
- 质量 `80` 是第一阶段的平衡默认值。
- 白色 `#FFFFFF` 是转换到不支持透明度格式时的默认铺底色；单张或批量草稿可以覆盖，运行请求使用提交快照。

### 12.7 默认同格式编码策略

Resize 与 Crop 第一阶段不在功能页面展示质量控件，使用公共默认值构造明确的 Workflow Request：

```text
DefaultSameFormatEncodingPolicy:
  LossyQuality = 90
  MetadataPolicy = Remove
```

- JPEG / WebP 按高质量 `90` 重新编码。
- 无损格式忽略质量值。
- 用户可以在“处理默认值”中修改是否移除拍摄信息与位置数据；ICC 不受该开关控制，已经提交的任务保留自己的策略快照。
- AtomPix 尚处施工设计期，该字段直接进入 v1 初始 Schema，不升级到 v2。实现读取缺失字段时必须使用这里冻结的明确默认值，不得回退到不确定的图片引擎默认行为。

### 12.8 默认输出策略

第一阶段默认输出策略沿用输出策略模型基线：

```text
DefaultOutputPolicy:
  LocationPolicy:
    Mode = Subfolder
    SubfolderName = AtomPix_Output
    CustomDirectory = null

  NamingPolicy:
    Mode = AppendSuffix
    Suffix = _atompix
    Pattern = null

  OverwritePolicy:
    AutoRename
```

默认输出策略优先保证安全：不污染原图目录、不默认覆盖已有文件、不打断批量处理。

### 12.9 不进入 AppSettings 的内容

第一阶段不放入 `AppSettings`：

```text
最近打开列表本体
任务历史
窗口位置和尺寸
侧边栏宽度
上次选中的页面
缓存路径
日志级别
图片处理内部实现参数
```

边界说明：

- 最近记录列表属于使用历史，由 Infrastructure 单独保存。
- 窗口位置、布局、上次选中页面等属于 Desktop UI 状态，后续可在 Desktop 或 Infrastructure 中单独设计。
- 图片处理内部实现参数属于 `Imaging.Magick` 或图片引擎配置，不进入 Core 的用户设置模型。

### 12.10 设计边界

- Core 定义 `AppSettings` 模型和默认值语义。
- Infrastructure 实现设置的读取、保存、迁移和容错。
- Workflows 通过设置存储端口读取和保存设置。
- Desktop 展示设置页面并把用户选择转换为 `AppSettings`。
- `AppSettings` 不直接依赖 Avalonia、AtomUI、Magick.NET 或具体存储格式。
## 13. 本地路径值对象基线

AtomPix 第一阶段在核心模型和跨模块契约中使用 `LocalPath` 表达本地文件或目录路径，不长期使用裸 `string` 传递路径。

### 13.1 LocalPath

建议模型：

```csharp
public readonly record struct LocalPath(string Value);
```

语义：

- `LocalPath` 表达用户本地文件系统中的路径。
- 它可以指向文件，也可以指向目录，具体语义由使用场景决定。
- `Value` 保存平台原生路径字符串。

### 13.2 使用约束

- Core、Workflows、Imaging.Abstractions 中涉及本地路径的公共模型优先使用 `LocalPath`。
- 不在系统中长期用裸 `string` 表达输入文件、输出文件、输出目录等路径。
- `LocalPath` 不直接访问文件系统，不判断路径是否存在。
- 路径存在性、权限查询和目录创建由 Workflow 通过 Infrastructure 端口完成；AutoRename 决策属于 Workflow。图片处理实现只对已经确定的输入/输出路径执行防御性读写。
- Desktop 从文件选择器拿到路径后，应尽早转换为 `LocalPath`。

### 13.3 设计边界

`LocalPath` 是值对象，不是 `FileInfo`、`DirectoryInfo` 或活动文件句柄。

Core 可以对 `LocalPath.Value` 做基础非空校验，但不做跨平台文件系统访问。
## 14. 基础设施端口基线

Core 定义 AtomPix 需要外部世界提供的稳定端口，Infrastructure 提供具体实现。

第一阶段基础设施端口包括：

```text
IAppSettingsStore
IRecentItemsStore
IFileSystemService
IAppPathProvider
```

### 14.1 设置端口

```csharp
public interface IAppSettingsStore
{
    Task<OperationResult<AppSettings>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken);
}
```

### 14.2 最近记录模型与端口

最近记录模型：

```csharp
public sealed record RecentItem(
    LocalPath Path,
    RecentItemKind Kind,
    DateTimeOffset OpenedAt);
```

```csharp
public enum RecentItemKind
{
    File,
    Directory
}
```

最近记录存储端口：

```csharp
public interface IRecentItemsStore
{
    Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        IReadOnlyList<RecentItem> items,
        CancellationToken cancellationToken);
}
```

### 14.3 文件系统端口

`IFileSystemService` 只提供原子文件系统能力，不承载输出策略决策。

```csharp
public interface IFileSystemService
{
    bool FileExists(LocalPath path);

    bool DirectoryExists(LocalPath path);

    Task<OperationResult> CreateDirectoryAsync(
        LocalPath directory,
        CancellationToken cancellationToken);

    Task<OperationResult<long>> GetFileSizeAsync(
        LocalPath path,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesAsync(
        LocalPath directory,
        CancellationToken cancellationToken);

    OperationResult<LocalPath> NormalizePath(LocalPath path);

    bool PathsEqual(LocalPath left, LocalPath right);

    int ComparePaths(LocalPath left, LocalPath right);

    LocalPath Combine(LocalPath directory, string fileName);

    string GetFileName(LocalPath path);

    string GetFileNameWithoutExtension(LocalPath path);

    string GetExtension(LocalPath path);

    LocalPath ChangeExtension(LocalPath path, string extension);

    LocalPath BuildIndexedPath(LocalPath basePath, int index);
}
```

约束：

- 不接收 `OverwritePolicy`。
- 不决定跳过、覆盖或自动重命名。
- 不判断功能是否允许保存文件。
- 不调用图片处理引擎。
- 输出路径策略由 Workflows 根据 `OutputPolicy` 决策。
- `EnumerateFilesAsync` 只枚举目录当前层级的文件，不递归子目录；返回规范化绝对路径的不可变快照。
- 枚举端口不按图片格式过滤、不排序、不生成缩略图，也不区分浏览集合与批量输入计划；这些语义由对应 Workflow 决定。
- 目录不存在、无权限、枚举失败和取消必须返回结构化 `OperationResult`，不能静默转换为空集合。
- `NormalizePath` 负责把合法路径转换为规范化绝对路径；非法路径返回结构化失败。
- `PathsEqual` 和 `ComparePaths` 使用当前平台的文件系统路径语义；Windows 路径比较不区分大小写。Workflows 使用它们完成跨来源去重和稳定决胜，不自行猜测平台规则。

### 14.4 应用路径端口

```csharp
public interface IAppPathProvider
{
    LocalPath AppDataDirectory { get; }

    LocalPath TempDirectory { get; }
}
```

`IAppPathProvider` 只表达 AtomPix 需要应用数据目录和临时目录。具体跨平台路径选择由 Infrastructure 实现。
## 15. Core 工业级硬化基线

Core 中的模型必须主动维护自身不变量，不能依赖调用方“按约定传对参数”。

本节对前文代码示例做实现级约束补充。若早期示例展示的是简化 record 写法，实际实现应以本节的不变量为准。

### 15.1 结果模型不变量

`OperationResult` 和 `OperationResult<T>` 只能通过静态工厂创建。

约束：

- 成功结果不能携带 `AtomPixError`。
- 失败结果必须携带 `AtomPixError`。
- `OperationResult<T>` 成功结果必须携带非空 `Value`。
- `AtomPixError.Message` 不能为空。

### 15.2 策略模型不变量

压缩策略：

- `CompressionMode.Custom` 必须显式提供 `ImageQuality`。
- `CompressionMode.Smart` 必须不携带用户指定的 `ImageQuality`；其内部候选参数不进入 `AppSettings`。
- `ImageQuality` 范围必须是 `1..100`。
- `PixelResizePolicy` 在保持比例时至少携带一个正数宽高；不保持比例时必须同时携带正数 width 和 height。
- `PercentageResizePolicy` 只能携带十进制正数百分比，不携带宽高或保持比例字段。
- `ResizePolicy` 的两个分支互斥，不能构造同时包含 Pixel 与 Percentage 字段的对象。
- `ImageSize` 与 `ResolvedResizeSize` 的宽高必须为正整数，解析过程必须防止溢出并保证结果至少为 `1 × 1`。
- `SameFormatEncodingPolicy` 的有损质量必须有效，元数据策略不能为空；它保持输入格式，不携带转换目标格式。
- `ConversionProfile.TransparencyPolicy` 和 `OpaqueBackgroundColor` 不能为空；颜色只能表达不透明 sRGB RGB 值。
- `TransparencyPolicy` 对支持 Alpha 的目标格式不触发铺底，但仍作为完整转换请求快照保留。
- `CropRectangle` 的坐标和尺寸必须完整位于逻辑原图范围内，边界加法必须防止溢出。
- `CropRectangle` 不携带 `CropAspectRatio`；确定矩形与比例生成意图不能形成两个事实来源。
- `CropAspectRatio` 的两个单位必须为正整数并归一化。
- `CenterMaximum` 保留限制边、另一边向下取整；居中偏移向下取整，奇数余量保留在右侧或下侧。
- `CenterMaximum` 解析出的任一尺寸为 0 时必须返回 `InvalidCropOptions`，不能钳制为 1。

输出策略：

- `OutputLocationMode.SameAsInput` 不能携带自定义目录或子目录名。
- `OutputLocationMode.Subfolder` 必须携带子目录名，不能携带自定义目录。
- `OutputLocationMode.CustomDirectory` 必须携带自定义目录，不能携带子目录名。
- `OutputNamingMode.KeepOriginalName` 不能携带后缀或自定义格式。
- `OutputNamingMode.AppendSuffix` 必须携带后缀且不能携带自定义格式。
- `OutputNamingMode.CustomPattern` 必须携带合法格式且不能携带后缀；第一阶段只允许 `{name}` 与最多一个 `{index}`。
- 批量数量大于 1 时，实际格式必须包含 `{index}`；基础格式缺少时由 Core 在末尾派生 `_{index}`，而不是拒绝请求。

设置模型：

- `AppSettings` 的默认压缩、转换、同格式编码、输出策略和最近记录设置不能为 null。
- 三个默认 Profile 的 `MetadataPolicy` 必须相等，避免一个公共设置在持久化后形成相互矛盾的重复事实。
- `RecentItemsSettings.MaxCount` 必须大于 0。

### 15.3 任务状态流转不变量

`ImageJob` 状态流转：

```text
Pending -> Running
Pending / Running -> Succeeded
Pending / Running -> Failed
Pending / Running -> Canceled
Pending / Running -> Skipped
```

约束：

- 终态任务不能再次变更状态。
- `StartedAt` 不能早于 `CreatedAt`。
- `CompletedAt` 不能早于 `StartedAt`；如果未启动，则不能早于 `CreatedAt`。
- 失败和取消必须携带错误对象。
- 成功必须携带输出路径。

`BatchJob` 状态流转：

```text
Pending -> Running
Pending -> Failed / Canceled
Running -> Succeeded / PartiallySucceeded / Failed / Canceled
```

约束：

- `BatchJob` 必须至少包含一个 `ImageJob`。
- Workflow 只表达自然完成、取消或批量级中止意图；最终状态由 Core 根据结束原因和子任务终态决定。
- 自然完成要求所有子任务都已进入终态，不允许存在 `Pending` 或 `Running`。
- 自然完成时，成功和跳过混合仍为 `Succeeded`；只有存在成功项和失败项时才是 `PartiallySucceeded`。
- 取消或批量级中止可以保留尚未开始的 `Pending` 子任务，但不能保留 `Running` 子任务。
- 取消固定得到 `Canceled`，并携带 `OperationCanceled` / `Cancellation` 错误。
- 批量级中止必须携带错误；中止前已有成功项时为 `PartiallySucceeded`，否则为 `Failed`。
- 终态批量任务不能再次迁移。

### 15.4 结果快照不变量

`ImageJobResult`：

- 状态必须是终态，不能是 `Pending` 或 `Running`。
- 文件大小不能为负数。
- `Succeeded` 必须有输出路径和输出大小。
- `Failed` 必须有错误对象。
- `SizeDeltaBytes / SizeDeltaRatio / SizeChangeKind` 只能由输入、输出大小派生，调用方不能传入或覆盖。
- 输入或输出大小缺失时，三项变化统计为空；相同字节数只能派生 `Unchanged`。

`BatchResult`：

- 状态必须是终态，不能是 `Pending` 或 `Running`。
- `TotalCount` 必须大于 0，且不能小于 `Items.Count`。
- `CompletedCount = Items.Count`。
- `Succeeded` 和 `PartiallySucceeded` 必须至少包含一个 `ImageJobResult`。
- `Canceled` 可以在第一项开始前拥有空 `Items`，并必须携带取消错误。
- 携带批量级错误的 `Failed` 可以拥有空 `Items`。
- `Succeeded` 不携带批量级错误；`PartiallySucceeded` 只有在批量级中止时才携带批量级错误。
- 批量体积比较集合必须严格等于 `Succeeded` 且双边大小完整的 Items；`ReducedItemCount + UnchangedItemCount + IncreasedItemCount = SizeComparedItemCount`。
- 没有可比较项时总体差值、比例和变化类型为空，不能与有可比较项且总差值为零混淆。

### 15.5 测试要求

所有上述不变量必须有 Core 单元测试覆盖。

如果后续新增 Core 模型、策略、值对象或状态对象，必须同步补充：

```text
设计文档
对象自身校验
单元测试
```
### 15.6 设置版本不变量

`AppSettings` 必须携带 `SchemaVersion`：

- 当前版本为 `AppSettings.CurrentSchemaVersion = 1`。
- `SchemaVersion <= 0` 无效。
- 高于当前版本的设置文件不能静默当作当前版本读取。
- 旧 v1 文件缺少版本字段时，可由构造函数默认值按 v1 读取。
- AtomPix 尚处施工设计期；本轮为 `DefaultConversionProfile` 增加透明区域策略时仍直接更新 v1 基线，不引入 v2 或迁移流程。

该字段用于后续设置迁移，不用于保存最近记录或窗口布局。
## 16. 最近记录与批量进度补充基线

### 16.1 RecentItemsPolicy

最近记录的纯业务规则放在 Core：

- 新记录加入后排在最前。
- 相同 `Path + Kind` 的旧记录必须被去重。
- 列表按 `OpenedAt` 倒序排列。
- 列表长度不得超过 `RecentItemsSettings.MaxCount`。

Infrastructure 只负责保存和读取最近记录，Workflows 负责在用户动作成功后调用最近记录流程。

### 16.2 BatchProgressSnapshot

批量进度快照用于无 UI 和未来 UI 共同消费：

- `TotalCount` 必须大于 0。
- `CompletedCount` 不能超过 `TotalCount`。
- 成功、失败、跳过、取消数量之和必须等于 `CompletedCount`。
- `CompletionRatio` 由 `CompletedCount / TotalCount` 得到。
- `CurrentInputPath` 只在某个子任务已经进入 `Running` 时指向该输入；初始快照和单项终态后的快照为 null。
- 批量取消或中止时，`CompletedCount` 可以小于 `TotalCount`，因此父 Job 已终止不等同于 `IsCompleted = true`。

该模型只表达汇总进度快照，不定义消息序号、变化的单项、线程、事件分发、暂停或恢复策略。Workflow 使用 `BatchExecutionProgress<TItemResult>` 包装它并增加运行期交付信息；这不会形成第二套 Core 状态机。
### 16.3 用户可解释错误语义补充

`InvalidImageFile` 用于表达输入文件存在，但不是有效图片或图片内容已损坏。

语义边界：

- `InputFileNotFound`：路径指向的文件不存在。
- `UnsupportedInputFormat`：文件可被识别为图片，但格式不在 AtomPix 当前能力范围内，或第一阶段拒绝多帧/动画处理。
- `InvalidImageFile`：文件存在，但图片库无法把它作为有效图片读取，例如文本文件改扩展名、截断的图片、损坏的图片。
- `ImageReadFailed`：有效性之外的读取失败，例如 IO、权限、路径或平台层读取异常。
- `ImageCompressFailed` / `ImageConvertFailed` / `ImageResizeFailed` / `ImageCropFailed` / `ImagePreviewFailed`：图片处理动作本身失败。

Desktop 后续应优先根据 `AtomPixErrorCode` 做本地化展示，不直接展示底层异常消息。

### 16.4 图片资源错误语义补充

资源限制使用稳定错误码，不把底层 Magick.NET 异常名称暴露给 Desktop：

| 错误码 | Category | 语义 |
| --- | --- | --- |
| `InputFileTooLarge` | `Validation` | 输入文件实际字节数超过引擎声明的最大输入文件体积。 |
| `ImageDimensionsExceedLimit` | `Validation` | 输入或计划输出的单边或总像素数超过能力边界。 |
| `ImageResourceLimitExceeded` | `ImageProcessing` | 已通过静态边界的图片在实际解码、像素缓存或编码期间触发内存、映射或其他引擎资源上限。 |
| `InsufficientDiskSpace` | `FileSystem` | 输出卷或图片引擎私有临时缓存位置没有足够磁盘空间。 |

`Details` 至少在可获得时携带 `ResourceKind`、`ActualValue`、`MaximumValue`；尺寸错误额外携带实际和最大 Width、Height、PixelCount，文件体积错误携带实际和最大字节数。Core 不定义 `512 MiB / 32768 px / 128000000 px` 等具体数值，这些数值由 Imaging 能力声明，避免把 Magick 的实现边界固化为纯业务规则。

### 16.5 LocalPath 路径边界补充

`LocalPath` 只表达本地路径文本，不访问文件系统，也不判断路径是否存在。

约束：

- `LocalPath` 拒绝空值和空白值。
- `LocalPath` 保留调用方传入的原始路径文本。
- 文件是否存在、目录是否存在、权限是否足够，由 Infrastructure 或 Imaging 实现判断。
- Core 不根据当前操作系统解释路径是否合法，避免把平台 IO 语义引入核心模型。

## 17. 取消与处理统计模型补充

### 17.1 取消不是失败

Core 中 `ImageJobStatus.Canceled` 与 `ImageJobStatus.Failed` 必须区分：

- `Failed` 表示处理动作发生业务或技术失败。
- `Canceled` 表示用户或系统主动中断流程。
- `Canceled` 任务结果必须携带 `AtomPixErrorCode.OperationCanceled` / `AtomPixErrorCategory.Cancellation` 错误。

Desktop 后续不应把取消展示为严重错误。

### 17.2 BatchResult 总数口径

`BatchResult.TotalCount` 表示批量任务原始计划数量。

`BatchResult.CompletedCount` 表示已经产生终态结果的数量，即 `Items.Count`。

取消场景下允许：

```text
TotalCount > CompletedCount
CompletedCount = 0
Items = []
```

未开始的后续项不生成假任务结果。任务接受后、第一项开始前取消时，空 `Items` 是合法终态结果；批量级错误在产生任何单项结果前中止时同样允许空 `Items`。这样可以同时保留真实处理历史和未完成进度。

### 17.3 统计口径

`ImageJobResult` 提供中性派生统计：

- `SizeDeltaBytes = OutputSizeBytes - InputSizeBytes`
- `SizeDeltaRatio = SizeDeltaBytes / InputSizeBytes`
- `SizeChangeKind = Reduced / Unchanged / Increased`

`BatchResult` 只对 `Succeeded` 且双边文件大小完整的项目提供：

- `SizeComparedItemCount`
- `ProcessedInputSizeBytes / ProcessedOutputSizeBytes`
- `TotalSizeDeltaBytes / TotalSizeDeltaRatio / TotalSizeChangeKind`
- `ReducedItemCount / UnchangedItemCount / IncreasedItemCount`

失败、跳过、取消和未开始项完全不参与体积聚合。没有可比较成功项时总体差值、比例和类型为空；这与确实存在成功输出且字节数完全相同的 `Unchanged` 明确区分。
