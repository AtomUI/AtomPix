# AtomPix.Core 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Core` 是 AtomPix 的业务核心，位于洋葱模型最中心。

它定义 AtomPix 的产品语言、业务模型、值对象、策略、授权权益、额度规则、任务状态、错误模型和纯业务规则。其他模块可以依赖 Core，但 Core 不依赖任何外层模块。

Core 不是通用工具库，也不是 UI、图片库或存储实现的集合。

## 2. 允许包含

- 图片任务、批量任务、任务状态等核心模型。
- 压缩策略、转换策略、输出命名策略、覆盖策略。
- 订阅状态、支付周期、功能标识、功能访问判断。
- 应用设置模型。
- 统一结果模型和错误模型。
- 业务值对象，例如本地路径、输出路径、质量参数、尺寸限制。
- 不依赖外部 IO 的业务规则和策略判断。
- Core 需要的外部能力端口，例如设置存储、订阅状态存储、最近记录存储、文件系统端口。

## 3. 禁止包含

- Avalonia、AtomUI、View、ViewModel、Command、Binding 等 UI 类型。
- Magick.NET、SkiaSharp、ImageSharp 等具体图片库类型。
- JSON 文件读写、SQLite 访问、系统路径解析、注册表、Keychain 等基础设施实现。
- HTTP API 具体调用、订阅服务端实现、崩溃上报实现。
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
  Licensing/
  
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
- `MetadataPolicy`
- `ConversionProfile`
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

商业化：

- `SubscriptionState`
- `SubscriptionStatus`
- `BillingCycle`
- `FeatureId`
- `FeatureAccessPolicy`
- `FeatureAccessDecision`

设置与结果：

- `AppSettings`
- `OperationResult`
- `OperationResult<T>`
- `AtomPixError`
- `AtomPixErrorCode`

端口：

- `IAppSettingsStore`
- `ISubscriptionStore`
- `IRecentItemsStore`
- `IFileSystemService`
- `IAppPathProvider`

## 6. 设计约束

- Core 可以有业务代码，但只能是纯业务规则。
- Core 不知道设置保存在哪里，也不知道订阅状态如何持久化。
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
    FeatureAccess,
    Quota,
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
| `ImageProcessing` | 图片读取、预览、压缩、转换、写入过程失败。 |
| `UnsupportedFormat` | 输入或输出格式不支持。 |
| `Permission` | 文件访问权限、目录写入权限等权限问题。 |
| `FeatureAccess` | 功能访问策略不允许。 |
| `Quota` | 使用额度不足或达到限制。 |
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
    OutputDirectoryNotFound,
    OutputFileAlreadyExists,
    InvalidInputPath,
    InvalidOutputPath,

    UnsupportedInputFormat,
    UnsupportedOutputFormat,

    InvalidCompressionQuality,
    InvalidResizeOptions,
    InvalidConversionOptions,
    InvalidMetadataOptions,

    ImageReadFailed,
    ImageWriteFailed,
    ImageCompressFailed,
    ImageConvertFailed,
    ImagePreviewFailed,

    FeatureNotAvailable,
    QuotaExceeded,
    SubscriptionExpired,

    SettingsLoadFailed,
    SettingsSaveFailed
}
```

错误码应保持稳定，作为 Desktop 本地化展示、日志筛选、测试断言和后续错误统计的依据。

### 7.5 批量任务结果约定

批量流程整体返回：

```csharp
OperationResult<BatchResult>
```

语义：

- 整体 `Failure` 表示批量流程无法启动，例如输入为空、输出目录非法、权益不允许。
- 整体 `Success` 表示批量流程成功启动并完成调度或执行。
- 单项失败记录在 `BatchResult.Items` 中，不让整个批量结果直接失败。
- 批量结果允许部分成功、部分失败。

`BatchResult` 的具体结构在任务模型细化阶段再冻结。

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

从产品视角看，它由三类用户可理解的设置组成：

```text
压缩强度
是否调整尺寸
是否保留元数据
```

从工程视角看，第一阶段拆为五个核心概念：

```text
CompressionProfile
CompressionMode
ImageQuality
ResizePolicy
MetadataPolicy
```

### 8.1 CompressionProfile

`CompressionProfile` 是压缩方案的聚合对象，不是单一参数。

建议模型：

```csharp
public sealed record CompressionProfile(
    CompressionMode Mode,
    ImageQuality? Quality,
    ResizePolicy ResizePolicy,
    MetadataPolicy MetadataPolicy);
```

约束：

- `Mode` 决定压缩强度。
- `Quality` 表达质量参数，主要用于 JPEG / WebP 等有损压缩格式。
- `ResizePolicy` 决定是否改变图片尺寸。
- `MetadataPolicy` 决定是否保留 EXIF、GPS、ICC 等元数据。
- `Smart` 模式下 `Quality` 可以为空，由具体图片格式策略或图片引擎实现决定。
- `Custom` 模式下 `Quality` 应由用户显式指定。

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
| `Custom` | 用户手动控制质量、尺寸和元数据策略。 |

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

PNG 等无损格式不一定直接使用 `ImageQuality`。具体映射由 `AtomPix.Imaging.Magick` 根据格式能力决定，例如优化、降低颜色复杂度或转为 WebP。

### 8.4 ResizePolicy

`ResizePolicy` 表达压缩时是否调整图片尺寸。

建议模型：

```csharp
public sealed record ResizePolicy(
    ResizeMode Mode,
    int? MaxWidth,
    int? MaxHeight,
    int? Percentage);
```

```csharp
public enum ResizeMode
{
    None,
    FitWithinBounds,
    Percentage
}
```

语义：

| 模式 | 含义 |
| --- | --- |
| `None` | 不调整图片尺寸。 |
| `FitWithinBounds` | 限制最大宽高，并保持原始比例。 |
| `Percentage` | 按百分比缩放，并保持原始比例。 |

约束：

- `None` 模式下 `MaxWidth`、`MaxHeight`、`Percentage` 应为空。
- `FitWithinBounds` 模式下至少应提供 `MaxWidth` 或 `MaxHeight` 之一。
- `Percentage` 模式下必须提供合法 `Percentage`。
- 第一阶段所有 resize 都保持原始比例，不做拉伸变形。

### 8.5 MetadataPolicy

`MetadataPolicy` 表达压缩输出时如何处理图片元数据。

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
| `Preserve` | 尽量保留原图元数据，例如 EXIF、拍摄时间、ICC 色彩配置等。 |
| `Remove` | 尽量移除元数据，以减小体积并减少隐私信息泄漏。 |

第一阶段不细分 EXIF、GPS、ICC 等元数据类别。后续如有需要，可扩展为：

```text
PreserveColorProfileOnly
RemoveExifKeepColorProfile
```

### 8.6 默认压缩方案

第一阶段默认方案建议：

| 模式 | 质量 | 尺寸策略 | 元数据策略 |
| --- | --- | --- | --- |
| `Smart` | 由格式策略决定 | `None` | `Remove` |
| `HighQuality` | `90` | `None` | `Preserve` |
| `Balanced` | `80` | `None` | `Remove` |
| `Maximum` | `65` | `None` | `Remove` |
| `Custom` | 用户指定 | 用户指定 | 用户指定 |

默认行为说明：

- `Smart` 默认移除元数据，符合图片压缩工具的常见预期。
- `HighQuality` 默认保留元数据，因为它强调质量和信息保留。
- `Balanced` 和 `Maximum` 默认移除元数据，以优先降低体积。
- Resize 默认不启用；用户显式选择最大宽高或百分比缩放时才改变尺寸。

### 8.7 设计边界

- Core 只表达压缩意图和业务约束，不调用图片库。
- 具体图片格式如何映射 `CompressionProfile`，由 `AtomPix.Imaging.Magick` 实现。
- Desktop 可以把 `CompressionProfile` 展示为预设、表单或设置项，但不能改变 Core 的业务语义。
- Workflows 使用 `CompressionProfile` 编排压缩流程，并在入口执行权益检查、参数校验和结果组织。
## 9. 转换配置模型基线

`ConversionProfile` 表达 AtomPix 的格式转换方案。

它和 `CompressionProfile` 的区别在于：

```text
CompressionProfile = 怎么把图片变小
ConversionProfile = 怎么把图片变成另一种格式
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
    ResizePolicy ResizePolicy,
    MetadataPolicy MetadataPolicy);
```

语义：

- `OutputFormat` 表示目标输出格式，转换流程必须指定。
- `Quality` 表示目标格式支持有损压缩时的质量参数，例如 JPEG / WebP。
- `ResizePolicy` 表示转换时是否顺便调整尺寸。
- `MetadataPolicy` 表示转换输出时如何处理元数据。

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
ResizePolicy
MetadataPolicy
```

复用原因：

- JPEG / WebP 的压缩质量在“压缩”和“转换”中都需要表达。
- 图片尺寸调整既可能发生在压缩流程，也可能发生在转换流程。
- 元数据保留或移除是输出行为，不只属于压缩，也属于转换。

复用边界：

| 对象 | 压缩流程 | 转换流程 |
| --- | --- | --- |
| `ImageQuality` | 表达压缩质量。 | 表达目标格式输出质量。 |
| `ResizePolicy` | 表达压缩时是否通过缩小尺寸降低体积。 | 表达转换时是否顺便改变尺寸。 |
| `MetadataPolicy` | 表达压缩输出是否保留元数据。 | 表达转换输出是否保留元数据。 |

### 9.4 二者差异

| 维度 | `CompressionProfile` | `ConversionProfile` |
| --- | --- | --- |
| 主要目标 | 减小文件体积。 | 改变图片格式。 |
| 是否必须改变格式 | 不必须。 | 必须指定目标格式。 |
| 是否有压缩模式 | 有，`Smart` / `HighQuality` / `Balanced` / `Maximum` / `Custom`。 | 无，直接指定目标格式和输出参数。 |
| 是否有目标格式 | 通常沿用原格式或由压缩策略决定。 | 必须有 `OutputFormat`。 |
| 是否可调整尺寸 | 可以。 | 可以。 |
| 是否可处理元数据 | 可以。 | 可以。 |

### 9.5 设计边界

- Core 只表达转换意图，不调用图片库。
- 具体输出格式如何映射到 Magick.NET 编码参数，由 `AtomPix.Imaging.Magick` 实现。
- `ConversionProfile` 不表达输入格式；输入格式由图片探测结果决定。
- Workflows 使用 `ConversionProfile` 编排转换流程，并负责输出路径、覆盖策略、权益检查和结果组织。
- Desktop 可以把 `ConversionProfile` 展示为格式选择器、质量设置、尺寸设置和元数据设置。
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

`OutputNamingPolicy` 表达输出文件命名策略。

建议模型：

```csharp
public sealed record OutputNamingPolicy(
    OutputNamingMode Mode,
    string? Suffix);
```

```csharp
public enum OutputNamingMode
{
    KeepOriginalName,
    AppendSuffix
}
```

语义：

| 模式 | 含义 |
| --- | --- |
| `KeepOriginalName` | 保留原文件名，只根据输出格式改变扩展名。 |
| `AppendSuffix` | 在原文件名后追加后缀，再根据输出格式改变扩展名。 |

约束：

- `KeepOriginalName` 模式下，`Suffix` 应为空。
- `AppendSuffix` 模式下，`Suffix` 必须有值。

第一阶段默认：

```text
Mode = AppendSuffix
Suffix = _atompix
```

示例：

```text
photo.jpg -> photo_atompix.jpg
screenshot.png -> screenshot_atompix.webp
```

`_atompix` 同时适用于压缩和格式转换，因此作为第一阶段默认后缀。

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
    Convert
}
```

设计约束：

- 预览不进入任务队列；预览属于浏览体验，不属于批量处理任务。
- 裁剪、尺寸调整、水印等后续能力如进入批量导出流程，可扩展为新的任务类型。

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
- 一批任务通常共享同一个压缩或转换配置，Profile 应由 Workflow 请求或 `BatchJob` 上下文持有。
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
- `CompressionProfile` 通常不决定扩展名，除非后续压缩策略明确允许转格式。
- Workflows 负责结合 Profile、OutputPolicy、输入路径和文件系统状态解析最终输出路径。
- Infrastructure 可提供文件系统检查、目录创建、自动重命名等底层能力。

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
| `Succeeded` | 全部单项成功，或全部单项按策略跳过且流程正常结束。 |
| `PartiallySucceeded` | 部分成功，且存在失败、跳过或取消项。 |
| `Failed` | 全部失败，或批量流程无法启动。 |
| `Canceled` | 用户取消了批量流程，结果中保留已完成项。 |

规则：

- 全部 `Succeeded` -> `BatchJobStatus.Succeeded`。
- 部分 `Succeeded`，部分 `Failed` / `Skipped` / `Canceled` -> `BatchJobStatus.PartiallySucceeded`。
- 全部 `Skipped` -> `BatchJobStatus.Succeeded`，但统计中显示 skipped 数量。
- 全部 `Failed` -> `BatchJobStatus.Failed`。
- 用户取消批量流程 -> `BatchJobStatus.Canceled`，无论取消前是否已有成功项。

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
- 节省大小和节省比例可由输入/输出大小派生。

派生统计：

```text
SavedBytes = InputSizeBytes - OutputSizeBytes
SavedRatio = SavedBytes / InputSizeBytes
```

具体是否做成 Core 计算属性，留到实现阶段根据模型细节决定。

### 11.9 BatchResult

批量任务结果：

```csharp
public sealed record BatchResult(
    BatchJobId BatchId,
    ImageJobType Type,
    BatchJobStatus Status,
    IReadOnlyList<ImageJobResult> Items);
```

建议派生统计：

```text
TotalCount
SucceededCount
FailedCount
SkippedCount
CanceledCount
TotalInputSizeBytes
TotalOutputSizeBytes
TotalSavedBytes
```

设计约束：

- 批量流程整体返回 `OperationResult<BatchResult>`。
- 整体 `Failure` 表示批量流程无法启动，例如输入为空、输出目录非法、权益不允许。
- 整体 `Success` 表示批量流程成功启动并完成调度或执行。
- 单项失败记录在 `BatchResult.Items`，不直接导致整体 `OperationResult<BatchResult>` 失败。
- 批量结果允许部分成功、部分失败。

### 11.10 设计边界

- Core 定义任务状态、结果和基础状态流转规则。
- Workflows 负责创建任务、编排执行、更新状态、汇总结果。
- Imaging.Abstractions 负责图片处理契约，不关心批量任务模型。
- Imaging.Magick 只处理单次图片操作，不知道批量任务和权益规则。
- Infrastructure 可提供任务历史持久化能力，但第一阶段不要求关机后恢复任务。
- Desktop 负责展示任务队列、进度、统计和错误，不直接改变任务业务语义。
## 12. 订阅与功能访问模型基线

AtomPix 第一阶段只预留简单订阅模型，不设计复杂付费等级。

商业规则：

```text
免费用户：可使用基础功能
订阅有效用户：可使用全部功能
订阅过期用户：按免费用户能力处理，并可提示续费
```

支付周期只表达购买方式，不表达功能等级：

```text
月付
季付
年付
```

第一阶段不设计：

```text
Pro / Team / Enterprise
多档权益
功能包
角色权限
使用额度
```

### 12.1 FeatureId

`FeatureId` 是软件功能的稳定标识，用于统一判断某个功能是否可用。

第一阶段预留：

```csharp
public enum FeatureId
{
    SingleCompress,
    BatchCompress,
    SingleConvert,
    BatchConvert,
    WebpExport,
    MetadataControl,
    ResizeOnExport,
    AdvancedCompressionProfile
}
```

设计目的：

- 避免在 ViewModel、Workflows 或图片处理实现中散落“某功能是否收费”的判断。
- 支持后续快速把某个功能从免费切换为订阅，或从订阅放回免费。
- 保持功能访问规则集中可维护。

### 12.2 SubscriptionStatus

订阅状态：

```csharp
public enum SubscriptionStatus
{
    Free,
    Active,
    Expired
}
```

语义：

| 状态 | 含义 |
| --- | --- |
| `Free` | 未订阅用户。 |
| `Active` | 订阅有效，解锁全部功能。 |
| `Expired` | 曾经订阅但已过期，按免费用户能力处理。 |

### 12.3 BillingCycle

支付周期：

```csharp
public enum BillingCycle
{
    Monthly,
    Quarterly,
    Yearly
}
```

约束：

- `BillingCycle` 只表达购买周期。
- 月付、季付、年付不影响功能范围。
- 只要订阅有效，无论支付周期是什么，都可使用全部功能。

### 12.4 SubscriptionState

订阅状态快照：

```csharp
public sealed record SubscriptionState(
    SubscriptionStatus Status,
    BillingCycle? BillingCycle,
    DateTimeOffset? ExpiresAt);
```

约束：

```text
Free:
  BillingCycle = null
  ExpiresAt = null

Active:
  BillingCycle 有值
  ExpiresAt 有值

Expired:
  BillingCycle 可以有值
  ExpiresAt 有值
```

后续如需联网校验，可扩展：

```text
SubscriptionId
LastValidatedAt
```

但第一阶段不提前复杂化。

### 12.5 FeatureAccessPolicy

`FeatureAccessPolicy` 是功能访问统一判断点。

它不是复杂权限系统，而是集中回答：

```text
这个功能当前能不能用？
```

建议接口：

```csharp
public interface IFeatureAccessPolicy
{
    FeatureAccessDecision CanUse(
        FeatureId feature,
        SubscriptionState subscription);
}
```

核心规则：

```text
如果 subscription.Status == Active:
  允许所有 FeatureId

否则:
  只允许 FreeFeatures 白名单中的功能
```

付费用户不需要维护独立的 PaidFeatures 集合，因为订阅有效时默认解锁全部功能。

免费功能白名单示例：

```csharp
private static readonly HashSet<FeatureId> FreeFeatures =
[
    FeatureId.SingleCompress,
    FeatureId.SingleConvert
];
```

白名单的最终内容后续可根据商业策略调整。调整免费/订阅功能归属时，应优先修改 `FeatureAccessPolicy`，而不是修改 ViewModel、Workflows 或图片处理实现。

### 12.6 FeatureAccessDecision

功能访问判断结果：

```csharp
public sealed record FeatureAccessDecision(
    bool Allowed,
    FeatureAccessBlockReason? BlockReason);
```

阻止原因：

```csharp
public enum FeatureAccessBlockReason
{
    SubscriptionRequired,
    SubscriptionExpired
}
```

语义：

- `Allowed = true` 时，`BlockReason` 应为空。
- `Allowed = false` 时，`BlockReason` 应有值。
- Desktop 根据 `BlockReason` 决定展示购买、续费或升级提示。

### 12.7 设计边界

- Core 定义订阅状态、功能标识和访问规则。
- Workflows 在用户流程入口检查对应 `FeatureId`。
- Desktop 可以根据访问结果禁用按钮、展示订阅状态或显示购买入口，但不能作为最终裁判。
- Infrastructure 负责保存订阅状态，后续可实现激活、刷新和本地缓存。
- Imaging.Magick 完全不知道订阅、收费和功能访问。
- 第一阶段不设计 quota，不按使用次数限制功能。
## 13. 应用设置模型基线

`AppSettings` 表达 AtomPix 第一阶段需要持久化的用户偏好。

它只保存会影响用户体验和处理结果的稳定设置，不保存订阅状态、任务历史、最近记录列表本体或窗口布局等高频变化数据。

### 13.1 AppSettings

建议模型：

```csharp
public sealed record AppSettings(
    int SchemaVersion,
    CompressionProfile DefaultCompressionProfile,
    ConversionProfile DefaultConversionProfile,
    OutputPolicy DefaultOutputPolicy,
    ThemeMode ThemeMode,
    string? Language,
    RecentItemsSettings RecentItems);
```

语义：

- `DefaultCompressionProfile` 表示默认压缩方案。
- `DefaultConversionProfile` 表示默认格式转换方案。
- `DefaultOutputPolicy` 表示默认输出目录、命名和覆盖策略。
- `ThemeMode` 表示应用主题。
- `Language` 表示语言设置，`null` 表示跟随系统。
- `RecentItems` 表示最近打开记录的偏好设置。

### 13.2 ThemeMode

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

### 13.3 Language

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

### 13.4 RecentItemsSettings

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

### 13.5 默认压缩配置

第一阶段默认压缩配置：

```text
DefaultCompressionProfile:
  Mode = Smart
  Quality = null
  ResizePolicy = None
  MetadataPolicy = Remove
```

语义：

- 默认使用智能压缩。
- 默认不调整尺寸。
- 默认移除元数据，以符合图片压缩工具的常见预期。
- `Smart` 模式的具体质量参数由格式策略或图片引擎实现决定。

### 13.6 默认转换配置

第一阶段默认转换配置：

```text
DefaultConversionProfile:
  OutputFormat = WebP
  Quality = 80
  ResizePolicy = None
  MetadataPolicy = Remove
```

选择 WebP 作为默认输出格式的原因：

- AtomPix 的产品方向偏图片压缩和体积优化。
- WebP 对常见图片场景有较好的体积表现。
- 质量 `80` 是第一阶段的平衡默认值。

### 13.7 默认输出策略

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

  OverwritePolicy:
    AutoRename
```

默认输出策略优先保证安全：不污染原图目录、不默认覆盖已有文件、不打断批量处理。

### 13.8 不进入 AppSettings 的内容

第一阶段不放入 `AppSettings`：

```text
订阅状态
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

- 订阅状态属于 `SubscriptionState`，由订阅存储管理。
- 最近记录列表属于使用历史，由 Infrastructure 单独保存。
- 窗口位置、布局、上次选中页面等属于 Desktop UI 状态，后续可在 Desktop 或 Infrastructure 中单独设计。
- 图片处理内部实现参数属于 `Imaging.Magick` 或图片引擎配置，不进入 Core 的用户设置模型。

### 13.9 设计边界

- Core 定义 `AppSettings` 模型和默认值语义。
- Infrastructure 实现设置的读取、保存、迁移和容错。
- Workflows 通过设置存储端口读取和保存设置。
- Desktop 展示设置页面并把用户选择转换为 `AppSettings`。
- `AppSettings` 不直接依赖 Avalonia、AtomUI、Magick.NET 或具体存储格式。
## 14. 本地路径值对象基线

AtomPix 第一阶段在核心模型和跨模块契约中使用 `LocalPath` 表达本地文件或目录路径，不长期使用裸 `string` 传递路径。

### 14.1 LocalPath

建议模型：

```csharp
public readonly record struct LocalPath(string Value);
```

语义：

- `LocalPath` 表达用户本地文件系统中的路径。
- 它可以指向文件，也可以指向目录，具体语义由使用场景决定。
- `Value` 保存平台原生路径字符串。

### 14.2 使用约束

- Core、Workflows、Imaging.Abstractions 中涉及本地路径的公共模型优先使用 `LocalPath`。
- 不在系统中长期用裸 `string` 表达输入文件、输出文件、输出目录等路径。
- `LocalPath` 不直接访问文件系统，不判断路径是否存在。
- 路径存在性、权限、目录创建、自动重命名等外部 IO 由 Infrastructure 或图片处理实现处理。
- Desktop 从文件选择器拿到路径后，应尽早转换为 `LocalPath`。

### 14.3 设计边界

`LocalPath` 是值对象，不是 `FileInfo`、`DirectoryInfo` 或活动文件句柄。

Core 可以对 `LocalPath.Value` 做基础非空校验，但不做跨平台文件系统访问。
## 15. 基础设施端口基线

Core 定义 AtomPix 需要外部世界提供的稳定端口，Infrastructure 提供具体实现。

第一阶段基础设施端口包括：

```text
IAppSettingsStore
ISubscriptionStore
IRecentItemsStore
IFileSystemService
IAppPathProvider
```

### 15.1 设置与订阅端口

设置存储：

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

订阅状态存储：

```csharp
public interface ISubscriptionStore
{
    Task<OperationResult<SubscriptionState>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        SubscriptionState subscription,
        CancellationToken cancellationToken);
}
```

### 15.2 最近记录模型与端口

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

### 15.3 文件系统端口

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

    LocalPath Combine(LocalPath directory, string fileName);

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

### 15.4 应用路径端口

```csharp
public interface IAppPathProvider
{
    LocalPath AppDataDirectory { get; }

    LocalPath TempDirectory { get; }
}
```

`IAppPathProvider` 只表达 AtomPix 需要应用数据目录和临时目录。具体跨平台路径选择由 Infrastructure 实现。
## 16. Core 工业级硬化基线

Core 中的模型必须主动维护自身不变量，不能依赖调用方“按约定传对参数”。

本节对前文代码示例做实现级约束补充。若早期示例展示的是简化 record 写法，实际实现应以本节的不变量为准。

### 16.1 结果模型不变量

`OperationResult` 和 `OperationResult<T>` 只能通过静态工厂创建。

约束：

- 成功结果不能携带 `AtomPixError`。
- 失败结果必须携带 `AtomPixError`。
- `OperationResult<T>` 成功结果必须携带非空 `Value`。
- `AtomPixError.Message` 不能为空。

### 16.2 策略模型不变量

压缩策略：

- `CompressionMode.Custom` 必须显式提供 `ImageQuality`。
- `ImageQuality` 范围必须是 `1..100`。
- `ResizePolicy.None` 不能携带宽高或百分比。
- `ResizePolicy.FitWithinBounds` 至少携带一个正数宽高边界。
- `ResizePolicy.Percentage` 只能携带正数百分比，不能同时携带宽高。

输出策略：

- `OutputLocationMode.SameAsInput` 不能携带自定义目录或子目录名。
- `OutputLocationMode.Subfolder` 必须携带子目录名，不能携带自定义目录。
- `OutputLocationMode.CustomDirectory` 必须携带自定义目录，不能携带子目录名。
- `OutputNamingMode.KeepOriginalName` 不能携带后缀。
- `OutputNamingMode.AppendSuffix` 必须携带后缀。

设置模型：

- `AppSettings` 的默认压缩、转换、输出策略和最近记录设置不能为 null。
- `RecentItemsSettings.MaxCount` 必须大于 0。

### 16.3 订阅与功能访问不变量

`SubscriptionState` 必须满足：

```text
Free:
  BillingCycle = null
  ExpiresAt = null

Active:
  BillingCycle 有值
  ExpiresAt 有值

Expired:
  ExpiresAt 有值
```

`FeatureAccessDecision` 只能通过工厂方法创建：

- `Allow()` 不能携带阻止原因。
- `Deny(reason)` 必须携带阻止原因。

### 16.4 任务状态流转不变量

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
Pending / Running -> Succeeded
Pending / Running -> PartiallySucceeded
Pending / Running -> Failed
Pending / Running -> Canceled
```

约束：

- `BatchJob` 必须至少包含一个 `ImageJob`。
- `BatchJob.Complete` 只能设置终态。
- 终态批量任务不能再次完成。

### 16.5 结果快照不变量

`ImageJobResult`：

- 状态必须是终态，不能是 `Pending` 或 `Running`。
- 文件大小不能为负数。
- `Succeeded` 必须有输出路径和输出大小。
- `Failed` 必须有错误对象。

`BatchResult`：

- 状态必须是终态，不能是 `Pending` 或 `Running`。
- 必须至少包含一个 `ImageJobResult`。

### 16.6 测试要求

所有上述不变量必须有 Core 单元测试覆盖。

如果后续新增 Core 模型、策略、值对象或状态对象，必须同步补充：

```text
设计文档
对象自身校验
单元测试
```
### 16.7 设置版本不变量

`AppSettings` 必须携带 `SchemaVersion`：

- 当前版本为 `AppSettings.CurrentSchemaVersion = 1`。
- `SchemaVersion <= 0` 无效。
- 高于当前版本的设置文件不能静默当作当前版本读取。
- 旧 v1 文件缺少版本字段时，可由构造函数默认值按 v1 读取。

该字段用于后续设置迁移，不用于保存订阅状态、最近记录或窗口布局。
## 17. 最近记录与批量进度补充基线

### 17.1 RecentItemsPolicy

最近记录的纯业务规则放在 Core：

- 新记录加入后排在最前。
- 相同 `Path + Kind` 的旧记录必须被去重。
- 列表按 `OpenedAt` 倒序排列。
- 列表长度不得超过 `RecentItemsSettings.MaxCount`。

Infrastructure 只负责保存和读取最近记录，Workflows 负责在用户动作成功后调用最近记录流程。

### 17.2 BatchProgressSnapshot

批量进度快照用于无 UI 和未来 UI 共同消费：

- `TotalCount` 必须大于 0。
- `CompletedCount` 不能超过 `TotalCount`。
- 成功、失败、跳过、取消数量之和必须等于 `CompletedCount`。
- `CompletionRatio` 由 `CompletedCount / TotalCount` 得到。

该模型只表达进度快照，不定义线程、事件分发、暂停或恢复策略。
### 17.3 用户可解释错误语义补充

`InvalidImageFile` 用于表达输入文件存在，但不是有效图片或图片内容已损坏。

语义边界：

- `InputFileNotFound`：路径指向的文件不存在。
- `UnsupportedInputFormat`：文件可被识别为图片，但格式不在 AtomPix 当前能力范围内，或第一阶段拒绝多帧/动画处理。
- `InvalidImageFile`：文件存在，但图片库无法把它作为有效图片读取，例如文本文件改扩展名、截断的图片、损坏的图片。
- `ImageReadFailed`：有效性之外的读取失败，例如 IO、权限、路径或平台层读取异常。
- `ImageCompressFailed` / `ImageConvertFailed` / `ImagePreviewFailed`：图片处理动作本身失败。

Desktop 后续应优先根据 `AtomPixErrorCode` 做本地化展示，不直接展示底层异常消息。

### 17.4 LocalPath 路径边界补充

`LocalPath` 只表达本地路径文本，不访问文件系统，也不判断路径是否存在。

约束：

- `LocalPath` 拒绝空值和空白值。
- `LocalPath` 保留调用方传入的原始路径文本。
- 文件是否存在、目录是否存在、权限是否足够，由 Infrastructure 或 Imaging 实现判断。
- Core 不根据当前操作系统解释路径是否合法，避免把平台 IO 语义引入核心模型。

## 18. 取消与处理统计模型补充

### 18.1 取消不是失败

Core 中 `ImageJobStatus.Canceled` 与 `ImageJobStatus.Failed` 必须区分：

- `Failed` 表示处理动作发生业务或技术失败。
- `Canceled` 表示用户或系统主动中断流程。
- `Canceled` 任务结果必须携带 `AtomPixErrorCode.OperationCanceled` / `AtomPixErrorCategory.Cancellation` 错误。

Desktop 后续不应把取消展示为严重错误。

### 18.2 BatchResult 总数口径

`BatchResult.TotalCount` 表示批量任务原始计划数量。

`BatchResult.CompletedCount` 表示已经产生终态结果的数量，即 `Items.Count`。

取消场景下允许：

```text
TotalCount > CompletedCount
```

未开始的后续项不生成假任务结果。这样可以同时保留真实处理历史和未完成进度。

### 18.3 统计口径

`ImageJobResult` 提供：

- `SavedBytes = InputSizeBytes - OutputSizeBytes`
- `SavedRatio = SavedBytes / InputSizeBytes`

`BatchResult` 提供：

- `TotalInputSizeBytes`
- `TotalOutputSizeBytes`
- `TotalSavedBytes`
- `TotalSavedRatio`

统计只基于已经产生结果且有文件大小的项目。失败、跳过、取消项缺失的大小按 0 参与批量汇总。
