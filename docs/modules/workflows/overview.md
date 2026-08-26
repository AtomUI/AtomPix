# AtomPix.Workflows 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Workflows` 是用户流程编排层。

它负责把 Desktop 层传入的用户动作转换为明确的应用流程，例如打开图片、生成预览、压缩图片、格式转换、批量处理和保存设置。

Workflows 不属于 UI 层，也不属于图片库实现层。它只编排 Core 规则、图片处理契约和外部能力端口。

## 2. 允许包含

- 用户流程服务，例如 `CompressImageWorkflow`、`BatchConvertWorkflow`。
- 用例输入和输出对象。
- 应用级流程编排。
- 输出策略与图片处理调用之间的组合逻辑。
- 面向 DI 的服务注册扩展。

## 3. 禁止包含

- Avalonia、AtomUI、View、ViewModel 等 UI 类型。
- Magick.NET、SkiaSharp、ImageSharp 等具体图片库类型。
- JSON 配置读写、SQLite、注册表、Keychain 等技术实现。
- 具体日志框架实现。
- 直接 `new MagickImageProcessor` 或依赖 `AtomPix.Imaging.Magick`。
- 用户界面提示、弹窗、Toast 或文件选择器。

## 4. 推荐目录

```text
src/AtomPix.Workflows/
  AtomPix.Workflows.csproj
  Browsing/
  Preview/
  Compression/
  Conversion/
  Batch/
  Settings/
  DependencyInjection/
```

## 5. 首批流程

- `OpenImageWorkflow`
- `OpenFolderWorkflow`
- `CreatePreviewWorkflow`
- `CompressImageWorkflow`
- `ConvertImageWorkflow`
- `ResizeImageWorkflow`
- `CropImageWorkflow`
- `AppendBatchInputsWorkflow`
- `BatchCompressWorkflow`
- `BatchConvertWorkflow`
- `BatchResizeWorkflow`
- `LoadSettingsWorkflow`
- `SaveSettingsWorkflow`

四类图片处理 Workflow 同级存在：压缩、转换、调整尺寸、裁剪。Desktop 不应通过“压缩面板的附属选项”代替独立 Resize 或 Crop Workflow。

## 6. 典型流程边界

压缩流程可以做：

```text
校验输入
根据 Core 策略计算输出路径
调用 IImageProcessor.CompressAsync
返回结构化结果
```

压缩流程不能做：

```text
弹出保存文件对话框
直接调用 Magick.NET
把结果写入 Avalonia Bitmap
决定 UI 如何展示错误
```

## 7. 依赖规则

```text
AtomPix.Workflows
  -> AtomPix.Core
  -> AtomPix.Imaging.Abstractions
```

Workflows 消费 Core 中定义的业务规则和存储端口，消费 Imaging Abstractions 中定义的图片处理契约。具体实现由 Desktop 组合根注入。

图片处理任务的状态所有权、Job 创建边界、单张与批量迁移顺序、取消、终态汇总和进度快照，以 [Workflow 任务状态机编排设计](job-state-orchestration.md) 为准。Workflows 只驱动 Core 状态机，不维护第二套与 `ImageJobStatus` / `BatchJobStatus` 重复的公开状态。

## 8. 第一阶段用户流程基线

`AtomPix.Workflows` 负责把 Desktop 层传入的用户动作转换为明确的应用流程。

本层不做 UI，不调用 Magick.NET，不直接读写配置文件。它负责组合：

```text
Core 业务规则
Imaging.Abstractions 图片处理契约
Core 中定义的外部能力端口
```

第一阶段定义以下 Workflow：

```text
OpenImageWorkflow
OpenFolderWorkflow
CreatePreviewWorkflow
CompressImageWorkflow
ConvertImageWorkflow
ResizeImageWorkflow
CropImageWorkflow
AppendBatchInputsWorkflow
BatchCompressWorkflow
BatchConvertWorkflow
BatchResizeWorkflow
LoadSettingsWorkflow
SaveSettingsWorkflow
```

### 8.1 返回值约定

所有 Workflow 统一返回：

```csharp
Task<OperationResult<T>>
```

示例：

```csharp
Task<OperationResult<CompressImageResult>> ExecuteAsync(
    CompressImageRequest request,
    CancellationToken cancellationToken);
```

约束：

- 用户可预期失败通过 `OperationResult<T>` 返回。
- 取消使用 Core 中定义的 `OperationCanceled` 错误码和 `Cancellation` 分类。
- Workflows 不把具体图片库异常、文件系统异常或 UI 类型穿透到调用方。

### 8.2 OpenImageWorkflow

用途：

```text
打开单张图片，读取图片基础信息。
```

输入：

```csharp
public sealed record OpenImageRequest(LocalPath InputPath);
```

输出：

```csharp
public sealed record OpenImageResult(
    ImageProbeResult ProbeResult);
```

流程：

```text
1. 校验 InputPath。
2. 调用 IImageProcessor.ProbeAsync。
3. 返回图片信息。
```

边界：

- 不生成预览图。
- 不更新 UI 状态。
- 不直接访问 Magick.NET。

### 8.3 CreatePreviewWorkflow

用途：

```text
为 UI 生成可显示的图片预览数据。
```

输入：

```csharp
public sealed record CreatePreviewRequest(
    LocalPath InputPath,
    int MaxPixelSize);
```

输出：

```csharp
public sealed record CreatePreviewResult(
    ImagePreviewResult Preview);
```

流程：

```text
1. 校验 MaxPixelSize。
2. 调用 IImageProcessor.CreatePreviewAsync。
3. 返回 EncodedBytes / MimeType / Width / Height。
```

边界：

- Desktop 负责把 `EncodedBytes` 转换为 Avalonia Bitmap。
- Workflows 不返回 Avalonia 类型。

### 8.4 CompressImageWorkflow

用途：

```text
执行单张图片压缩。
```

输入：

```csharp
public sealed record CompressImageRequest(
    LocalPath InputPath,
    CompressionProfile Profile,
    OutputPolicy OutputPolicy);
```

输出：

```csharp
public sealed record CompressImageResult(
    ImageJobResult JobResult,
    ImageQuality? AppliedQuality);
```

流程：

```text
1. 校验输入路径、压缩配置和输出策略。
2. 探测输入图片信息并确认格式为 JPEG / PNG / WebP；压缩的输出格式固定等于探测格式。
3. 根据 OutputPolicy、输入路径、同一格式扩展名和文件系统状态解析 OutputPath，并通过文件系统端口准备输出目录。
4. 前置检查通过后创建 Pending ImageJob。
5. 按 Skip、取消或真实执行分支驱动 Core 状态迁移。
6. 需要真实处理时 MarkRunning，再调用 IImageProcessor.CompressAsync。
7. 根据处理结果迁移到 Succeeded、Failed 或 Canceled。
8. 从 Core Job 终态组装 ImageJobResult。
```

边界：

- 单张压缩也返回 `ImageJobResult`，以保持单张和批量结果模型一致。
- 成功时把处理器返回的 `AppliedQuality` 原样投影给 Desktop；无损输出为 `null`，不得按 CompressionMode 自行推断。
- OutputPath 由 Workflows 解析后传给图片处理契约。
- Workflows 不直接调用 BCL 创建目录或写文件；它负责决定何时准备输出目录，并通过文件系统端口执行，具体能力由 Infrastructure 实现。

### 8.5 ConvertImageWorkflow

用途：

```text
执行单张图片格式转换。
```

输入：

```csharp
public sealed record ConvertImageRequest(
    LocalPath InputPath,
    ConversionProfile Profile,
    OutputPolicy OutputPolicy);
```

输出：

```csharp
public sealed record ConvertImageResult(
    ImageJobResult JobResult,
    TransparencyProcessingResult? Transparency);
```

结果不变量：

- `Succeeded` 时 `Transparency` 必须存在，并等于 `IImageProcessor.ConvertAsync` 返回的实际结果。
- `Failed / Canceled / Skipped` 没有可靠的完成处理结果，`Transparency` 必须为 `null`。
- `Transparency` 不进入通用 Core `ImageJobResult`；它属于转换流程的专用结果，避免让所有任务类型承担无关字段。

流程：

```text
1. 校验输入路径、转换配置和输出策略。
2. 探测输入图片信息。
3. 使用 Probe.HasTransparency 与目标格式能力判定透明度预期；完整 ConversionProfile（含 TransparencyPolicy）保持为本次请求快照。
4. 根据 OutputPolicy、Profile.OutputFormat、输入路径和文件系统状态解析 OutputPath。
5. 前置检查通过后创建 Pending ImageJob。
6. 按 Skip、取消或真实执行分支驱动 Core 状态迁移。
7. 需要真实处理时 MarkRunning，再调用 IImageProcessor.ConvertAsync。
8. 根据处理结果迁移到 Succeeded、Failed 或 Canceled。
10. 从 Core Job 终态组装 ImageJobResult；透明度结果取处理器返回值，不由 Workflow 推断。
```

边界：

- 转换流程必须指定目标输出格式。
- 输入格式由图片探测结果决定。
- 背景色只在真实透明输入转换为不支持 Alpha 的目标格式时执行，但始终属于完整转换请求快照。
- Workflows 不直接调用具体图片库。

### 8.6 ResizeImageWorkflow

用途：

```text
按照独立 Pixel / Percentage 策略调整完整图片尺寸，不裁剪、不补边、不转换格式。
```

输入：

```csharp
public sealed record ResizeImageRequest(
    LocalPath InputPath,
    ResizePolicy ResizePolicy,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);
```

请求边界：

- `InputPath` 是 Desktop 在导航边界捕获的当前图片路径。
- `ResizePolicy` 只表达用户尺寸意图。
- `OutputPolicy` 只表达输出位置、命名和冲突策略。
- `EncodingPolicy` 是提交时已经解析完成的同格式编码快照；Resize 页面不直接展示质量控件。
- Request 不接收原图尺寸、`ResolvedResizeSize`、最终 `OutputPath`、JobId 或 Desktop 预计尺寸。

输出：

```csharp
public sealed record ResizeImageResult(
    ImageJobResult JobResult,
    ImageFormatKind Format,
    ImageSize InputSize,
    ResolvedResizeSize TargetSize,
    ImageSize? ActualOutputSize);
```

结果不变量：

- `InputSize` 使用自动校正 EXIF 方向后的逻辑尺寸。
- `TargetSize` 是 Core 对 `ResizePolicy + InputSize` 的解析结果，Job 创建前即已确定。
- `Succeeded` 时 `ActualOutputSize` 必须存在并严格等于 `TargetSize`。
- `Failed / Canceled / Skipped` 时 `ActualOutputSize` 可以为空，`TargetSize` 仍然保留用于解释原计划。
- 输入与输出 `Format` 相同；Resize 不承担格式转换。

流程：

```text
1. 校验 ResizeImageRequest。
2. Probe 输入并拒绝多帧/动画。
3. 建立逻辑 ImageSize。
4. Core 解析 ResizePolicy -> ResolvedResizeSize。
5. 检查同格式写出能力、目标尺寸及引擎限制。
6. 按原扩展名和 OutputPolicy 解析 OutputPath。
7. 创建 Pending ImageJob。
8. 处理 Skip 或执行前取消。
9. MarkRunning，调用 IImageProcessor.ResizeAsync。
11. 验证实际输出尺寸严格等于 TargetSize。
12. 迁移 Succeeded / Failed / Canceled。
13. 从 Core Job 终态和处理结果构造 ResizeImageResult。
```

默认设置便利入口可以加载 `DefaultSameFormatEncodingPolicy` 与 `DefaultOutputPolicy` 后委托本流程；显式参数流程自身不在执行中重新读取可变设置。

### 8.7 CropImageWorkflow

用途：

```text
从自动校正方向后的原图逻辑坐标系中提取一个确定矩形，不调整尺寸、不补边、不转换格式。
```

输入：

```csharp
public sealed record CropImageRequest(
    LocalPath InputPath,
    CropRectangle CropArea,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);
```

请求边界：

- `InputPath` 是 Desktop 在提交边界捕获的当前图片路径。
- `CropArea` 是 Desktop 完成选框、数值和比例联动后提交的最终 `X / Y / Width / Height`，坐标属于自动方向校正后的原图逻辑坐标系。
- Request 不接收 `CropAspectRatio`、画布坐标、缩放比例、控制点、最终 `OutputPath`、JobId 或 Desktop 临时选框状态。
- `OutputPolicy` 只表达输出位置、命名和冲突策略。
- `EncodingPolicy` 是提交时已经解析完成的同格式编码快照；Crop 页面不直接展示质量控件。

输出：

```csharp
public sealed record CropImageResult(
    ImageJobResult JobResult,
    ImageFormatKind Format,
    ImageSize InputSize,
    CropRectangle CropArea,
    ImageSize? ActualOutputSize);
```

结果不变量：

- `InputSize` 使用自动校正 EXIF 方向后的逻辑尺寸。
- `CropArea` 保留已经提交并校验通过的原计划。
- `Succeeded` 时 `ActualOutputSize` 必须存在，且其 Width / Height 严格等于 `CropArea.Width / Height`。
- `Failed / Canceled / Skipped` 时 `ActualOutputSize` 可以为空，`CropArea` 仍然保留用于解释原计划。
- 输入与输出 `Format` 相同；Crop 不承担格式转换。

流程：

```text
1. 校验 CropImageRequest。
2. Probe 输入图片，拒绝本轮不支持的格式和多帧输入。
3. 建立自动方向校正后的逻辑 ImageSize。
4. 使用 Core 校验 CropArea 完整位于 InputSize 内。
5. 检查同格式 Crop 能力及输入尺寸限制。
6. 按原扩展名和 OutputPolicy 解析 OutputPath。
7. 创建 Pending ImageJob。
8. 处理 Skip 或执行前取消。
9. MarkRunning，调用 IImageProcessor.CropAsync。
11. 验证实际输出格式未改变，且实际尺寸严格等于 CropArea.Width × CropArea.Height。
12. 迁移 Succeeded / Failed / Canceled。
13. 从 Core Job 终态和处理结果构造 CropImageResult。
```

`CropArea` 在 Probe 后不合法时返回 `StartRejected(InvalidCropOptions)`，不创建 Job。若任务已接受后源文件被替换、尺寸发生变化或实际执行无法应用原矩形，则任务进入 `Failed(ImageCropFailed)`，不能回退为 `StartRejected`。

默认设置便利入口可以加载 `DefaultSameFormatEncodingPolicy` 与 `DefaultOutputPolicy` 后委托本流程；显式参数流程自身不在执行中重新读取可变设置。

### 8.8 BatchCompressWorkflow

用途：

```text
执行批量图片压缩。
```

输入：

```csharp
public sealed record BatchCompressRequest(
    IReadOnlyList<LocalPath> InputPaths,
    CompressionProfile Profile,
    OutputPolicy OutputPolicy);
```

输出：

```csharp
public sealed record BatchCompressResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchCompressItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed record BatchCompressItemResult(
    ImageJobResult JobResult,
    ImageQuality? AppliedQuality);
```

流程：

```text
1. 完成批量级前置检查，冻结输入顺序、共享 CompressionProfile、OutputPolicy 和完整 BatchOutputPlan；Custom 质量对所有有损项目共享，不提供逐项覆盖。
2. 一次性创建 Pending BatchJob 和全部 Pending ImageJob。
3. BatchJob 进入 Running，顺序处理每个子任务。
4. 逐项使用 BatchOutputPlan 中已冻结的 OutputPath，并按计划 Skip、失败、取消或真实执行驱动 ImageJob。
5. 每个子任务终态后生成 ImageJobResult 和进度快照。
6. Core 根据自然完成、取消或批量级中止意图汇总 BatchJob 终态。
7. 从父子 Job 终态组装 BatchResult。
```

第一阶段执行策略：

- 批量处理顺序执行。
- 支持取消。
- 允许部分成功。
- 不支持暂停、恢复和关机后继续。
- 后续再评估并发处理。
- `ItemResults` 与已经产生终态的 `BatchResult.Items` 按顺序和 JobId 一一对应；成功的有损项目使用处理器返回的 `AppliedQuality`，成功的无损项目为 `null`，其他终态不得伪造实际质量。
- 四类单项 Workflow 只把处理器实际返回的输入、输出字节数写入成功 `ImageJobResult`；失败、取消或跳过没有实际输出时保持 `OutputSizeBytes = null`，不得补 `0`。
- `BatchResult` 的体积变化、比例和三类项目计数由 Core 按成功可比较项统一派生；Workflow 不从输入列表、进度行或缺失大小重新计算，也不因输出变大改变 Job 终态。

### 8.9 BatchConvertWorkflow

用途：

```text
执行批量图片格式转换。
```

输入：

```csharp
public sealed record BatchConvertRequest(
    IReadOnlyList<LocalPath> InputPaths,
    ConversionProfile Profile,
    OutputPolicy OutputPolicy);
```

输出：

```csharp
public sealed record BatchConvertResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchConvertItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed record BatchConvertItemResult(
    ImageJobResult JobResult,
    TransparencyProcessingResult? Transparency);
```

逐项结果不变量：

- `ItemResults` 与 `BatchResult.Items` 数量、顺序和 JobId 一一对应，只包含已经产生终态结果的项目。
- `Succeeded` 时 `Transparency` 必须存在并满足 Imaging.Abstractions 结果不变量。
- `Failed / Canceled / Skipped` 时 `Transparency` 必须为 `null`；不得根据 Probe 或请求参数伪造处理结果。
- 批量取消后尚未开始的 Pending 子任务不生成 `BatchConvertItemResult`。

流程：

```text
1. 完成批量级前置检查并冻结输入顺序、Profile、OutputPolicy 和完整 BatchOutputPlan。
2. 一次性创建 Pending BatchJob 和全部 Pending ImageJob。
3. BatchJob 进入 Running，顺序处理每个子任务。
4. 逐项使用 BatchOutputPlan 中已冻结的 OutputPath，并按计划 Skip、失败、取消或真实执行驱动 ImageJob。
5. 每个子任务终态后生成 BatchConvertItemResult 和进度快照。
6. Core 根据自然完成、取消或批量级中止意图汇总 BatchJob 终态。
7. 从父子 Job 终态组装 BatchResult。
```

第一阶段执行策略与批量压缩一致：顺序执行、支持取消、允许部分成功。

批量转换中的透明度规则：

- 整个批次共享一个 `ConversionProfile.TransparencyPolicy`，不提供逐项背景色覆盖。
- 每项根据自己的 `Probe.HasTransparency` 决定是否实际使用背景色；无真实透明像素的项目返回 `NotPresent`。
- 目标 JPEG 且存在透明项时，进度与最终明细使用处理器返回的 `Flattened + BackgroundColor`；目标 PNG / WebP 的透明项返回 `Preserved`。
- 恢复失败项或处理未完成项时复制旧任务提交的背景色快照，不重新加载当前默认设置。

### 8.10 BatchResizeWorkflow

用途：

```text
把一套共享 ResizePolicy 分别应用到每张图片自己的逻辑原始尺寸。
```

输入：

```csharp
public sealed record BatchResizeRequest(
    IReadOnlyList<LocalPath> InputPaths,
    ResizePolicy ResizePolicy,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);
```

请求边界：

- `InputPaths` 来自已经展开、过滤和去重的 `BatchInputPlan.InputPaths`；请求只接收不可变文件路径快照，不接收目录。
- 所有项目共享同一个 `ResizePolicy`，但每张图片使用自己的 `ImageSize` 独立解析 `ResolvedResizeSize`。
- Pixel 不保持比例时，所有成功输出使用相同的确定 Width / Height；保持比例或 Percentage 时，不同原图通常得到不同目标尺寸。
- 第一阶段不支持逐项 Resize 参数覆盖；需要不同规则的图片应进入单张任务或另一个批次。
- `OutputPolicy` 与 `EncodingPolicy` 是整个批次的共享提交快照，但每张图片仍解析自己的最终 `OutputPath`。

输出：

```csharp
public sealed record BatchResizeResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchResizeItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed record BatchResizeItemResult(
    ImageJobResult JobResult,
    ImageFormatKind? Format,
    ImageSize? InputSize,
    ResolvedResizeSize? TargetSize,
    ImageSize? ActualOutputSize);
```

不能直接复用单张 `ResizeImageResult`：批量任务先创建全部子 Job，再按顺序逐项 Probe，因此某个已接受的子任务可能在取得格式、原图尺寸或目标尺寸前失败。

逐项结果不变量：

- `ItemResults` 与 `BatchResult.Items` 数量、顺序和 JobId 一一对应，只包含已经产生终态结果的项目。
- Probe 失败时 `Format / InputSize / TargetSize / ActualOutputSize` 可以全部为空。
- Probe 成功但 ResizePolicy 解析失败时，`Format / InputSize` 有值，`TargetSize / ActualOutputSize` 可以为空。
- 已解析目标后发生能力、路径、处理或取消结果时保留 `TargetSize`；`ActualOutputSize` 可以为空，若有可靠实测值则允许保留。
- `Skipped` 必须已经解析出格式、输入尺寸、目标尺寸和目标路径，但不伪造实际输出尺寸。
- `Succeeded` 时 `JobResult` 与四个处理字段必须完整，且 `ActualOutputSize` 严格等于 `TargetSize`。
- `Format` 一旦有值就同时表示该项输入和输出格式；Batch Resize 不承担格式转换。
- 批量取消后尚未开始的 Pending 子任务不生成 `BatchResizeItemResult`。

流程：

```text
1. 校验非空 InputPaths、共享 ResizePolicy、OutputPolicy 和 EncodingPolicy。
2. 冻结输入顺序和共享请求快照，解析文件名格式并生成完整 BatchOutputPlan，完成批量级输出前置检查。
3. 一次性创建 Pending BatchJob 和全部 Pending ImageJob。
4. BatchJob 进入 Running。
5. 按输入顺序逐项执行：
   a. Probe，并校验 JPEG / PNG / BMP / 单帧 WebP。
   b. 建立逻辑 ImageSize，使用 Core 解析共享 ResizePolicy -> TargetSize。
   c. 校验 Resize Capabilities，读取该项已冻结的 OutputPath。
   d. 按 Skip、取消、失败或真实 ResizeAsync 驱动 ImageJob。
   e. 校验成功输出格式与尺寸，构造 BatchResizeItemResult。
   f. 发布新的不可变 BatchProgressSnapshot。
6. 单项失败继续处理其他项目；取消停止当前及后续调度。
7. Core 根据自然完成、取消或批量级中止汇总 BatchJob 终态。
8. 构造 BatchResult 与 BatchResizeResult。
```

批量级请求为空、共享策略非法或公共输出位置在启动前非法时返回 `StartRejected`，不创建 BatchJob。缺失文件、损坏图片、不支持格式、单项尺寸解析失败和单项输出路径失败属于子任务 `Failed`，不阻断其他项目。公共输出位置在运行中失效等不可归属到单项的错误使用批量级 `Abort`。

### 8.11 LoadSettingsWorkflow

用途：

```text
读取应用设置。
```

输入：

```csharp
public sealed record LoadSettingsRequest;
```

输出：

```csharp
public sealed record LoadSettingsResult(
    AppSettings Settings);
```

流程：

```text
1. 调用 IAppSettingsStore.LoadAsync。
2. 如果设置不存在，返回默认 AppSettings，算成功。
3. 如果设置文件损坏，返回失败，不偷偷覆盖用户文件。
```

边界：

- 默认设置语义定义在 Core。
- 具体读取、保存、迁移和容错由 Infrastructure 实现。

### 8.12 SaveSettingsWorkflow

用途：

```text
保存应用设置。
```

输入：

```csharp
public sealed record SaveSettingsRequest(
    AppSettings Settings);
```

输出：

```csharp
public sealed record SaveSettingsResult;
```

流程：

```text
1. 校验 AppSettings。
2. 调用 IAppSettingsStore.SaveAsync。
3. 返回成功或错误。
```

边界：

- Workflows 只表达保存流程，不决定设置文件格式。
- Desktop 负责把设置页面的用户选择转换为 `AppSettings`。

### 8.13 依赖边界

`AtomPix.Workflows` 可以依赖：

```text
AtomPix.Core
AtomPix.Imaging.Abstractions
```

`AtomPix.Workflows` 不依赖：

```text
AtomPix.Desktop
AtomPix.Infrastructure 具体实现
AtomPix.Imaging.Magick
Avalonia
AtomUI
Magick.NET
```

Workflows 消费 Core 中定义的端口和 Imaging.Abstractions 中定义的图片处理契约。具体实现由 Desktop 组合根注入。
## 9. Workflows 工业级硬化基线

`AtomPix.Workflows` 是策略编排层，必须把可在流程层判断的问题尽早收敛为明确结果。

### 9.1 依赖与请求边界

- Workflow 构造函数必须拒绝 null 依赖。
- `ExecuteAsync` 必须拒绝 null 请求。
- 请求中的 `Profile`、`OutputPolicy`、`InputPaths` 等关键对象不能为 null。

### 9.2 输出路径策略

- Workflows 负责解析 `OutputPolicy`。
- `Skip` 返回 `ImageJobStatus.Skipped`，不调用图片处理器。
- `Overwrite` 使用目标路径，不自动改名。
- `AutoRename` 使用 `_1`、`_2` 等候选路径，直到找到不存在的路径。
- Infrastructure 只提供文件系统原子能力，不参与策略决策。

### 9.3 失败语义

- 输入文件大小读取失败时，单张流程直接返回失败，不继续解析输出路径或调用图片处理。
- 输出目录准备失败时，不创建 Job；Workflow 以携带文件系统错误的 `StartRejected` 返回。
- 非法转换输出格式返回 `UnsupportedOutputFormat`，不能生成 `.img` 等模糊扩展名。
- 图片处理失败时，单张流程返回成功的 Workflow 结果，但其中 `ImageJobResult.Status = Failed`，用于批量汇总。

### 9.4 Headless 验收

Workflows 必须可以在无 UI 环境下串联真实 Infrastructure 和 Imaging.Magick，完成打开、预览、压缩、转换、批量、跳过、覆盖、自动重命名和部分失败等用户场景。
## 10. 最近记录与进度流程补充

### 10.1 最近记录 Workflows

最近记录使用四个显式 Workflow，Desktop 不直接读写 `IRecentItemsStore`：

- `LoadRecentItemsWorkflow` 加载、规范化、按最近访问时间排序并按设置上限截断；读取失败不返回伪造空列表。
- `AddRecentItemWorkflow` 把成功打开文件或目录产生的记录写入存储；同类规范路径去重后更新时间并返回新列表。
- `RemoveRecentItemWorkflow` 按 `Path + Kind` 移除一项并返回新列表；它只修改最近记录，不删除磁盘图片或目录。
- `ClearRecentItemsWorkflow` 把最近记录存储替换为空列表；Desktop 必须先确认，Workflow 不承担交互确认。

`AddRecentItemWorkflow` 流程：

流程：

```text
1. 加载现有最近记录。
2. 使用 RecentItemsPolicy 去重、排序、截断。
3. 保存更新后的最近记录。
4. 返回更新后的列表。
```

这些流程不隐式绑定到 `OpenImageWorkflow`。Desktop 在打开图片或文件夹成功后显式调用 Add；打开失败、Picker 取消或最近记录设置关闭时不写入。最近记录自身读取失败不阻止用户打开图片，但也不得在未形成可信列表时继续覆盖存储。

### 10.2 批量进度快照

`BatchResult` 是批量调用结束后的权威终态结果；它不能替代运行期间类似“上传进度”的连续通知。第一阶段在保留 `ExecuteAsync -> BatchResult` 的同时，为每次批量调用增加一个可选、方法级的 `IProgress<T>` 通道。

Workflow 公共进度契约：

```csharp
public sealed record BatchExecutionProgress<TItemResult>(
    long Sequence,
    BatchProgressSnapshot Summary,
    BatchItemProgress<TItemResult>? ChangedItem,
    BatchOutputPlan OutputPlan)
    where TItemResult : class;

public sealed record BatchItemProgress<TItemResult>(
    int Index,
    ImageJobId JobId,
    LocalPath InputPath,
    ImageJobStatus Status,
    TItemResult? Result)
    where TItemResult : class;
```

三个显式参数批量 Workflow 的目标签名：

```csharp
Task<OperationResult<BatchCompressResult>> ExecuteAsync(
    BatchCompressRequest request,
    IProgress<BatchExecutionProgress<BatchCompressItemResult>>? progress,
    CancellationToken cancellationToken);

Task<OperationResult<BatchConvertResult>> ExecuteAsync(
    BatchConvertRequest request,
    IProgress<BatchExecutionProgress<BatchConvertItemResult>>? progress,
    CancellationToken cancellationToken);

Task<OperationResult<BatchResizeResult>> ExecuteAsync(
    BatchResizeRequest request,
    IProgress<BatchExecutionProgress<BatchResizeItemResult>>? progress,
    CancellationToken cancellationToken);
```

保留不接收 `progress` 的便利重载，并委托上述方法且传入 null。Headless 调用方不消费进度时，执行顺序、取消和最终结果必须完全相同。默认设置批量 Workflow 接收同类型进度参数，在设置加载成功后原样转发给对应的显式参数 Workflow；设置加载失败属于 `StartRejected`，不发布进度。

类型边界：

- `BatchProgressSnapshot` 继续属于 Core，只表达批次汇总计数、比例和当前输入。
- `BatchExecutionProgress<TItemResult>` 与 `BatchItemProgress<TItemResult>` 属于 Workflows，因为序号、单项变化和通知方式是应用流程交付语义。
- 压缩终态单项结果使用 `ImageJobResult`；转换使用携带实际透明处理结果的 `BatchConvertItemResult`；调整尺寸使用携带目标/实际尺寸的 `BatchResizeItemResult`。
- `IProgress<T>` 是单次方法调用的可选观察通道，不在 Workflow 服务上暴露长期事件，也不形成可恢复任务队列。

进度不变量：

- `Sequence` 在一个 BatchId 内从 1 开始严格递增。Desktop 只接受属于当前调用且序号大于已消费序号的消息。
- 已接受任务发布的每条消息都携带同一个非空冻结 `OutputPlan`；其项目数量等于 `Summary.TotalCount`，Desktop 用它初始化并校正输出路径，不能根据当前草稿重新计算。
- 初始消息在 `BatchJob.MarkRunning` 后发布：`CompletedCount = 0`、`CurrentInputPath = null`、`ChangedItem = null`。
- `Index` 对应提交时冻结的 `InputPaths` 索引；`InputPath` 必须等于该索引的冻结路径，`JobId` 必须属于同一个 BatchJob。
- 单项真正进入 Core `Running` 后发布运行消息；此时 `Status = Running`、`Result = null`，且 `Summary.CurrentInputPath` 等于该项路径。
- `Pending` 不逐项发布。Desktop 根据冻结输入列表初始化全部等待行。
- 单项进入 `Succeeded / Failed / Skipped / Canceled` 后才发布终态消息；此时 `Result` 必须存在且状态一致，`CompletedCount` 已增加，`CurrentInputPath` 回到 null。
- `Pending -> Failed / Skipped / Canceled` 的直接终态分支不虚构 `Running` 消息。
- Workflow 必须先完成 Core 迁移和结果构造，再调用 `Report`；进度观察者不能获得活动 Core Job。
- 进度适配器的同步异常只能记录诊断，不能改变、取消或中止图片任务；Desktop 回调自身的异步展示异常同样不得反向写入 Workflow/Core。
- `StartRejected` 不发布任何进度，因为 Core Job 尚不存在。任务已接受但第一项开始前取消，可以只有初始消息，随后返回 `BatchResult.Status = Canceled`。
- `ExecuteAsync` 返回的最终 `BatchResult` 是权威事实。终态到达后，Desktop 使用完整结果校正所有行，并忽略 UI 队列中迟到的进度消息。

第一阶段不承诺单张图片内部的字节级或编码百分比。`CompletionRatio` 只按已产生终态的图片数量阶梯式增长；当前 `Running` 项使用不确定进度反馈。未来只有在 Imaging 契约正式提供可靠的单项进度后，才另行扩展，不能伪造平滑百分比。
## 11. 默认设置与前置能力校验补充

### 11.1 默认设置驱动流程

第一阶段新增默认设置驱动的图片流程：

- `CompressWithDefaultSettingsWorkflow`
- `ConvertWithDefaultSettingsWorkflow`
- `ResizeWithDefaultSettingsWorkflow`
- `CropWithDefaultSettingsWorkflow`

这四个流程只负责：

```text
1. 从 IAppSettingsStore 加载 AppSettings。
2. 按功能读取默认 CompressionProfile、ConversionProfile、SameFormatEncodingPolicy 与 OutputPolicy。
3. 委托给对应的显式参数单张 Workflow。
```

`DefaultConversionProfile` 包含默认 `TransparencyPolicy`。转换处理面板允许修改当前单张或批量草稿颜色；一旦委托给显式参数 Workflow，设置页面后续变化不能回写已提交请求。

`MetadataPolicy` 同样作为 `CompressionProfile`、`ConversionProfile` 或 `SameFormatEncodingPolicy` 的不可变请求快照传递。Workflows 只冻结并转发 `Preserve / Remove`，不自行枚举、删除或重写任何 Profile / Attribute，也不把 ICC 纳入该策略；ICC 保留、Orientation 规范化和目标格式能力降级由 Imaging 实现负责。

`LoadSettingsWorkflow` 必须保留 Core 的公共默认元数据不变量；`SaveSettingsWorkflow` 接收的 AppSettings 中，三个默认 Profile 的 `MetadataPolicy` 必须一致。单张/批量草稿对某次请求的覆盖不自动写回设置。

它们不复制图片处理或输出路径逻辑。显式参数流程仍是核心流程，默认设置流程只是应用层便利入口。

### 11.2 前置探测与能力校验

四类单张图片处理在执行文件大小读取、输出路径解析和真实处理前，必须先执行图片探测与能力校验：

- 调用 `IImageProcessor.ProbeAsync`。
- 校验输入格式是否在 `Capabilities.SupportedInputFormats` 中。
- 当图片为动画或多帧且处理器不支持动画处理时，提前返回 `UnsupportedInputFormat`。
- 转换时校验目标格式是否在 `Capabilities.SupportedOutputFormats` 中。
- 转换时使用 `Probe.HasTransparency` 判定真实透明区域；不得用 `HasAlphaChannel` 代替并产生无意义警告或铺底统计。
- 压缩时根据输入格式推导输出格式，若不在输出能力范围内则提前失败。
- Resize 时校验输入格式、帧数和目标尺寸是否满足 `Capabilities.Resize`。
- Crop 时校验输入格式、帧数、输入尺寸和最终矩形是否满足 `Capabilities.Crop` 及 Core 边界规则。

目标是让 Workflows 提供稳定、可解释的失败语义，而不是把所有错误推迟到具体图片库写出阶段。
## 12. 批量默认设置与最终进度补充

### 12.1 批量默认设置流程

第一阶段新增：

- `BatchCompressWithDefaultSettingsWorkflow`
- `BatchConvertWithDefaultSettingsWorkflow`
- `BatchResizeWithDefaultSettingsWorkflow`

流程职责与单张默认设置流程一致：

```text
1. 从 IAppSettingsStore 加载 AppSettings。
2. 压缩/转换读取对应默认 Profile；Batch Resize 保留调用方显式提交的共享 ResizePolicy。
3. Batch Resize 读取 DefaultSameFormatEncodingPolicy；三类流程都读取 DefaultOutputPolicy，并委托给对应的显式参数批量 Workflow。
```

默认设置流程不复制批量处理逻辑，不改变输出路径策略或单项失败语义。调用方提供实时进度观察器时，默认设置流程只负责转发；不得重建序号、缓存或合并进度消息。

### 12.2 批量 Workflow 结果的 FinalProgress

批量流程返回值携带最终进度快照：

```text
BatchCompressResult.BatchResult
BatchCompressResult.FinalProgress
BatchConvertResult.BatchResult
BatchConvertResult.FinalProgress
BatchResizeResult.BatchResult
BatchResizeResult.FinalProgress
```

`FinalProgress` 由 `BatchResult` 投影得到，可以作为结果的派生属性，用于 headless 验收和 UI 完成态展示，不需要成为第二份可写状态。当前代码仍只有最终快照；Desktop 实现前需要按 10.2 的目标契约增加运行中快照，同时保留 `FinalProgress` 作为终态一致性校验。

### 12.3 混合输入语义

批量流程遇到缺失文件、动画/多帧输入或不支持格式时：

- 单项记录为 `ImageJobStatus.Failed`。
- 不中断整个批次。
- 其他可处理图片继续处理。
- 批次最终状态按结果汇总为 `Succeeded`、`PartiallySucceeded`、`Failed` 或 `Canceled`。
## 13. 错误透传与批量失败语义补充

Workflows 的前置探测失败必须原样透传错误码，不重新包装成模糊错误。

约束：

- 四类单张 Workflow 在 `ProbeAsync` 返回失败时，直接返回该失败，不解析输出路径，不调用图片处理器。
- 三类批量 Workflow 在单项 `ProbeAsync` 失败时，将该项记录为 `ImageJobStatus.Failed`，保留原始 `AtomPixError`。
- 批量流程继续处理其他输入，最终按单项结果汇总为 `PartiallySucceeded` 或 `Failed`。
- Workflows 不把 `InvalidImageFile` 改写为具体的压缩、转换、调整尺寸或裁剪失败。

这保证 UI 层后续可以把“文件不存在”“图片损坏”“格式不支持”“动画暂不支持”等失败展示为不同用户提示。

## 14. 输出写入失败结果语义补充

Workflows 负责输出路径决策，但不负责图片文件编码写入。

当图片处理器返回压缩、转换、调整尺寸或裁剪失败时：

- 单张 Workflow 整体仍返回 `OperationResult.Success`，因为用户流程已被调度并产生了任务结果。
- `ImageJobResult.Status` 必须为 `Failed`。
- `ImageJobResult.InputSizeBytes` 应保留已读取到的输入大小。
- `ImageJobResult.OutputPath` 应保留 Workflows 决策出的目标路径。
- `ImageJobResult.OutputSizeBytes` 必须为空。
- `ImageJobResult.Error` 保留图片处理器返回的原始错误。

这样 Desktop 后续既能展示“目标路径原本打算写到哪里”，也能展示失败原因和输入文件信息。

## 15. 输出目录与文件名边界补充

Workflows 负责把 `OutputPolicy` 解析为最终 `OutputPath`。

当前冻结规则：

- `SameAsInput` 输出到输入文件所在目录。
- `Subfolder` 输出到输入文件所在目录下的指定子目录。
- `CustomDirectory` 输出到策略中指定的目录。
- `KeepOriginalName` 保留输入文件名主体，只改变目标扩展名。
- `AppendSuffix` 在输入文件名主体后追加后缀，再改变目标扩展名。
- `CustomPattern` 展开 `{name}` 和可选 `{index}`；扩展名仍由任务输出格式决定。
- 批量数量大于 1 时，基础格式缺少 `{index}` 会派生为 `BasePattern + _{index}`，而不是产生重复计划名称。
- `{index}` 从 1 开始，宽度为三位或批量总数位数中的较大者；编号以冻结输入顺序为准。
- 多点文件名只替换最后一个扩展名，例如 `archive.photo.png -> archive.photo_atompix.webp`。
- `AutoRename` 在完整目标文件名主体后追加索引，例如 `archive.photo_atompix.webp -> archive.photo_atompix_1.webp`。
- 压缩流程要求输入路径能够解析出扩展名，否则返回 `UnsupportedOutputFormat`，不继续解析输出路径或调用图片处理器。
- 压缩输出扩展名必须由 Probe 得到的 JPEG / PNG / WebP 输入格式决定，不能由用户文件名或 `CompressionProfile` 改成另一格式。
- Resize/Crop 保持输入格式，最终输出扩展名必须与探测得到的输入格式一致。

文件名和目录策略的最终决策必须在 Workflows 完成。通过全部公共校验和源文件冲突检查后、创建 Job 前，Workflow 对 `BatchOutputPlan` 中需要处理的不同输出目录调用 `IFileSystemService.CreateDirectoryAsync`；单张流程同样准备自己的输出目录。Workflows 不直接使用 BCL 创建目录，Infrastructure 只实现端口，Imaging.Magick 只在既有目录中按最终路径安全写入。

单张输出解析还必须返回实际 `OutputWriteDisposition`：无冲突新建为 `Created`，发生冲突后另取可用路径为 `AutoRenamed`，确实替换既有输出为 `Overwritten`，按策略未执行为 `SkippedExisting`。它是 Workflow 的执行决策结果，不等同于用户请求中的 `OverwritePolicy`；Desktop 只能消费该结果生成终态反馈，不能通过所选策略反推实际发生的文件行为。

目录准备失败属于 `StartRejected`，不创建 Job。任务接受后目录被外部删除、权限变化或空间耗尽，则由 Imaging 返回结构化失败并驱动已创建 Job 的终态；不能把运行期失败回写成启动拒绝。

## 16. 默认设置加载失败语义补充

默认设置驱动的图片流程必须把设置加载视为前置条件。

约束：

- `CompressWithDefaultSettingsWorkflow` 加载设置失败时直接返回该失败，不调用探测、压缩或输出路径解析。
- `ConvertWithDefaultSettingsWorkflow` 加载设置失败时直接返回该失败，不调用探测、转换或输出路径解析。
- `ResizeWithDefaultSettingsWorkflow` 与 `CropWithDefaultSettingsWorkflow` 加载设置失败时同样不得探测、解析输出路径或创建 Job。
- 批量默认设置流程同理，设置失败时不启动批量任务。
- 该规则适用于 settings.json 损坏、高版本 schema、权限失败和取消等场景。

这样可以避免在用户设置不可解释或版本不兼容时继续生成处理结果。

## 17. 取消、中断与批量进度语义补充

第一阶段取消语义：

- 取消统一使用 `OperationCanceled` + `Cancellation`。
- 取消不是失败，不能被包装为 `ImageCompressFailed`、`ImageConvertFailed`、`ImageResizeFailed` 或 `ImageCropFailed`。
- 四类单张 Workflow 如果图片处理器返回取消，任务结果均为 `ImageJobStatus.Canceled`。
- Workflow 前置检查开始前 token 已取消时返回 `StartRejected(OperationCanceled)`，不创建 Job 或任务结果。
- 批量 Job 已接受但第一项开始前取消时，返回 `BatchJobStatus.Canceled`，允许 `BatchResult.Items` 为空。
- 批量流程处理中途取消时：
  - 已完成项保留原始结果。
  - 当前检测到取消的项记录为 `Canceled`。
  - 后续未开始项不生成 `ImageJobResult`。
  - `BatchResult.Status = Canceled`。
  - `BatchResult.TotalCount` 保留原始输入数量。
  - `BatchResult.CompletedCount` 只统计已经产生结果的项。
  - `FinalProgress.IsCompleted` 可以为 false。

第一阶段不做并发批量处理，也不承诺强行中断 Magick.NET 正在执行的同步编码写入。当前保证 Workflow 边界和图片处理入口的取消语义稳定。

完整的创建边界、父子状态和取消顺序见 [Workflow 任务状态机编排设计](job-state-orchestration.md)。

## 18. Workflows DI 注册补充

`AtomPix.Workflows.DependencyInjection` 提供 `AddAtomPixWorkflows()`。

注册内容：

- `ImageWorkflowServices`
- 打开、预览、四类单张处理、三类批量处理、默认设置、设置和最近记录 workflows；不注册 BatchCropWorkflow

Workflows DI 注册只绑定流程服务，不绑定 Infrastructure 或 Imaging 实现。Desktop 或 headless host 需要显式组合：

```text
AddAtomPixInfrastructure(...)
AddAtomPixMagickImaging()
AddAtomPixWorkflows()
```

## 19. 批量输入收集与追加契约

批量图片处理 Workflow 只接收已经展开、过滤并去重后的文件路径。`AppendBatchInputsWorkflow` 继续负责文件候选的稳定追加、过滤与去重，不能由各个批量处理 Workflow 重复实现。当前 Desktop 目标中，首页文件夹由 `OpenFolderWorkflow` 建立浏览集合；进入浏览器后只把多选文件传给追加流程，不再暴露追加目录入口。

目标请求与结果：

```csharp
public sealed record AppendBatchInputsRequest(
    IReadOnlyList<LocalPath> ExistingInputs,
    IReadOnlyList<LocalPath> SelectedFiles,
    IReadOnlyList<LocalPath> SelectedDirectories,
    bool IncludeSubdirectories = false);

public sealed record BatchInputPlan(
    IReadOnlyList<LocalPath> InputPaths,
    int AddedCount,
    int DuplicateCount,
    int UnsupportedCount,
    int UnreadableCount,
    IReadOnlyList<BatchInputSkip> SkippedItems);

public sealed record BatchInputSkip(
    LocalPath Path,
    BatchInputSkipReason Reason);

public enum BatchInputSkipReason
{
    Duplicate,
    UnsupportedFormat,
    Missing,
    Unreadable
}
```

执行顺序：

```text
接收当前 InputPaths、用户新选文件和目录
  -> 通过文件系统端口枚举目录候选文件
  -> 规范化为绝对 LocalPath
  -> 按图片引擎输入能力过滤扩展名
  -> 对当前列表和本次新增内容统一去重
  -> 保留当前列表顺序，把有效新增项追加到末尾
  -> 返回完整 BatchInputPlan 与跳过明细
```

约束：

- 第一阶段 `IncludeSubdirectories` 固定为 `false`；字段只用于明确边界，不在 UI 提供递归开关。
- `SelectedFiles` 保持调用方提供的顺序；每个目录中的候选文件按相对路径稳定排序。
- 去重使用文件系统端口提供的规范化路径和平台路径比较规则；Windows 路径比较不区分大小写。
- 当前列表与本次新增内容之间、本次不同来源之间都必须去重。
- 不支持、缺失或不可读取的文件作为跳过明细返回，不使整次追加失败。
- 目录自身无法访问或枚举被取消时，返回结构化失败；不得用一个空计划静默覆盖 `ExistingInputs`。
- 返回的 `InputPaths` 是不可变快照。后续 `BatchCompressRequest`、`BatchConvertRequest` 和 `BatchResizeRequest` 只接收该快照中的文件路径，不再接收目录。
- `AppendBatchInputsWorkflow` 不创建 `BatchJob`，也不开始图片处理；用户确认参数并点击开始后才创建批量任务。
- `SelectedDirectories` 保留为 Workflow 的通用/迁移期输入能力，但当前 Desktop 只有首页“打开文件夹”入口；浏览器走廊调用本流程时该字段必须为空。后续若删除该字段，应作为独立契约迁移处理，不能让 Desktop 自行枚举目录替代它。

目录枚举和路径规范化通过 Infrastructure 文件系统端口实现；Workflows 负责能力过滤、追加规则、顺序、去重和结果组织。Imaging.Magick 不枚举目录。

## 20. OpenFolderWorkflow 与浏览集合契约

`OpenFolderWorkflow` 把一个本地目录转换为轻量、不可变的图片浏览集合。它是首页“打开文件夹”的应用流程，本身不创建批量计划或任务；但返回的浏览集合就是用户随后在 Compress、Convert 或 Resize 面板发起批量处理时的可见输入范围。

请求与结果：

```csharp
public sealed record OpenFolderRequest(
    LocalPath DirectoryPath);

public sealed record OpenFolderResult(
    LocalPath DirectoryPath,
    IReadOnlyList<BrowserImageCandidate> Items,
    int UnsupportedFileCount);

public sealed record BrowserImageCandidate(
    LocalPath Path,
    string DisplayName);
```

结果不变量：

- `DirectoryPath` 和每个候选 `Path` 都是规范化后的绝对 `LocalPath`。
- `Items` 永远不为 null，是按确定顺序排列且不含重复路径的不可变快照。
- `DisplayName` 是面向 Desktop 展示的文件名，不携带 Avalonia 类型、Bitmap 或图片字节。
- `UnsupportedFileCount` 只统计因输入格式扩展名不受支持而被过滤的当前层级文件，不能把损坏图片计入其中。
- 目录中没有候选图片时返回成功的空 `Items`，由 Desktop 投影为浏览器空态。

执行顺序：

```text
校验 DirectoryPath
  -> 通过 IFileSystemService 枚举当前层级文件
  -> 根据 IImageProcessor.Capabilities.SupportedInputFormats 过滤扩展名
  -> 规范化路径并按平台路径规则去重
  -> 按文件名自然顺序排序，规范化完整路径作为稳定决胜条件
  -> 返回轻量 BrowserImageCandidate 快照
```

边界：

- 第一阶段固定不递归子目录，Request 不提供 `Recursive` 或 `IncludeSubdirectories` 字段。
- 扩展名过滤只用于候选发现，不证明文件内容有效；损坏、伪装格式或枚举后被删除的文件在后续 `OpenImageWorkflow` 中形成结构化失败。
- 本流程不逐项 Probe、不生成缩略图、不创建预览、不监听目录变化，也不创建 `ImageJob` 或 `BatchJob`。
- 本流程不调用 `AppendBatchInputsWorkflow`，也不直接返回或复用 `BatchInputPlan`；Desktop 只在用户点击批量开始时，从当前走廊的冻结路径建立正式批量请求。
- 目录不存在返回 `InputDirectoryNotFound`；无访问权限或枚举失败返回对应 `Permission` / `FileSystem` 错误；取消返回 `OperationCanceled`。
- 目录可访问但没有候选图片属于成功，不把空目录伪装成失败。

浏览集合建立后，Desktop 按需组合现有流程：

```text
当前候选项
  -> OpenImageWorkflow 探测格式、尺寸、帧数和可用性
  -> Desktop adapter 提供稳定路径身份与文件 Source
  -> AtomUI.Labs ImageGallery 按当前视口加载主图

当前可见/预取的候选项
  -> ImageGallery 在自己的资源上限、调度与缓存内加载缩略图
```

`CreatePreviewWorkflow` 仍作为框架无关的显式预览契约保留并独立测试，但生产 Browser 和 Crop 不消费它。浏览显示不新增 `CreateThumbnailWorkflow`；Gallery/Avalonia 类型也不进入 Workflow。该边界避免 Workflow 与 ImageGallery 同时维护一套主图/缩略图解码和缓存。

## 21. 浏览器单张快捷操作语义

首页打开图片或文件夹后进入图片浏览器。文件夹浏览结果只用于建立缩略图列表和当前图片，不生成 `BatchInputPlan`。

Desktop 从浏览器发起快捷操作时必须先捕获当前 `LocalPath`，再构造对应的单张请求：

```text
压缩       -> CompressImageRequest(CurrentImagePath, ...)
转换       -> ConvertImageRequest(CurrentImagePath, ...)
调整尺寸   -> ResizeImageRequest(CurrentImagePath, ...)
裁剪       -> CropImageRequest(CurrentImagePath, ...)
```

约束：

- 单张请求只能携带触发时的一个输入路径，不接收浏览文件夹、缩略图列表或当前目录。
- 浏览器当前图片在请求创建后发生切换，不改变该请求的输入。
- 打开文件夹成功不能隐式创建 `BatchJob`；它只建立可浏览、可继续追加的会话集合。
- 批量 Workflow 只能由 Compress、Convert 或 Resize 右侧面板，在用户点击“批量处理”后，使用当前走廊冻结出的明确 `BatchInputPlan.InputPaths` 启动；Crop 永远不启动批量 Workflow。
- Workflows 不读取 Desktop 的当前选择状态；当前路径由 Desktop 在调用边界显式传入。

## 22. 批量恢复动作与普通新任务复用

第一阶段不新增 `RetryBatchWorkflow`，也不向 Core 增加 `Retrying` 状态。重试失败项、处理未完成项和使用自动重命名处理，都是 Desktop 根据旧任务的提交快照与结果构造一个新的普通批量草稿；用户确认后仍调用现有三类批量 Workflow。

Desktop 必须随已接受的批量任务保留不可变 `SubmittedBatchSnapshot`：

```text
TaskType
InputPaths
CompressionProfile / ConversionProfile / ResizePolicy
SameFormatEncodingPolicy
OutputPolicy
```

恢复选择：

- 重试失败项：从 `BatchResult.Items` 提取 `ImageJobStatus.Failed`，保持原提交顺序。
- 处理未完成项：从原 `InputPaths` 中排除已经 `Succeeded` 或 `Skipped` 的输入，因此包含 Failed、Canceled 和没有结果的未开始项。
- 使用自动重命名处理：只选择 `Skipped + OutputFileAlreadyExists` 项，并将新草稿的 `OverwritePolicy` 改为 `AutoRename`。
- 成功项不进入任何默认恢复输入，避免生成重复输出。

新草稿默认复制旧任务实际提交的参数，不通过默认设置 Workflow 重新读取可能已经变化的设置。用户可以先修改参数、输出目录、覆盖策略，或者移除/重新定位输入；点击恢复动作本身不调用图片处理 Workflow，也不创建 Job。

用户再次点击开始后，Workflow 把它当作普通新请求：重新执行输入、能力和输出前置校验，创建新的 BatchJobId / ImageJobId，使用新的实时进度 Sequence，并返回独立 BatchResult。Workflow 不接收旧 BatchJobId，不修改旧任务，也不合并两次统计。

MVP 中执行期 `ImageJobStatus.Skipped` 只来自“目标存在 + OverwritePolicy.Skip”，必须保留目标路径并以 `OutputFileAlreadyExists` 解释原因。`AppendBatchInputsWorkflow` 返回的 `BatchInputPlan.SkippedItems` 是未进入任务的输入候选，不是 ImageJob 终态。

## 23. 禁止输出覆盖任务输入

源文件保护属于 Workflow 的权威前置校验，不由 Desktop 或 Imaging.Magick 猜测。

单张流程：

```text
1. 规范化 InputPath，并根据 OutputPolicy、任务类型和目标格式纯计算计划 OutputPath。
2. Overwrite 且 PathsEqual(InputPath, OutputPath) 时返回 StartRejected(OutputPathConflictsWithInput)。
3. 不创建 ImageJob、不创建目录、不调用图片处理器。
4. AutoRename 把输入路径视为已存在并选择新名称；Skip 创建正常 Skipped 任务结果。
```

批量流程：

```text
1. 冻结并规范化完整 InputPaths 集合。
2. 在创建 BatchJob / ImageJob 前纯计算每项计划 OutputPath。
3. Overwrite 下，将每个计划输出与整个输入集合比较，而不只与自己的输入比较。
4. 任一命中即整体 StartRejected(OutputPathConflictsWithInput)，Details 携带 ConflictCount 和首个冲突示例。
5. 没有命中后才准备输出目录、创建父子 Job 并顺序执行。
```

Desktop 可以根据当前草稿提前显示预计冲突，但正式结论必须来自 Workflow。`OutputPathConflictsWithInput` 是非法请求组合，不是 `ImageJobStatus.Failed` 或 `Skipped`；`Overwrite` 不能被 Workflow 静默改成 AutoRename。

第一阶段不为外部程序、多个 AtomPix 进程、符号链接或硬链接造成的路径竞争设计预占或提交重试。单进程、单前台任务和批量顺序执行仍是当前并发边界。

## 24. 批量文件名格式与 BatchOutputPlan

批量 Workflow 不在每项即将执行时临时决定文件名，而是在创建 `BatchJob` 前冻结完整输出计划。类型属于 Workflows，因为它组合 Core 命名规则、任务输出格式与文件系统存在性：

```csharp
public sealed record BatchOutputPlan(
    IReadOnlyList<BatchOutputPlanItem> Items,
    string EffectivePattern);

public sealed record BatchOutputPlanItem(
    int ItemIndex,
    int SequenceNumber,
    LocalPath InputPath,
    LocalPath OutputPath,
    BatchOutputDecision Decision,
    AtomPixError? Reason);

public enum BatchOutputDecision
{
    Process,
    Skip
}
```

计划流程：

```text
1. 冻结并规范化 InputPaths；TotalCount 即冻结项目数。
2. 将 KeepOriginalName / AppendSuffix / CustomPattern 转为 BasePattern。
3. TotalCount > 1 且缺少 {index} 时，派生 EffectivePattern = BasePattern + _{index}。
4. 按输入顺序展开 {name}/{index} 并附加任务决定的扩展名。
5. 按源文件保护规则检查每个计划输出是否命中任意输入路径。
6. 按输入顺序应用磁盘已有文件的 Skip / Overwrite / AutoRename，并把最终路径加入本批次保留集合。
7. 验证所有 Process 项的 OutputPath 在当前平台路径规则下唯一。
8. 创建 BatchJob 和全部 ImageJob，并把计划作为不可变执行上下文保存。
```

不变量：

- `Items` 与冻结输入数量和顺序一一对应；`ItemIndex` 为零基执行索引，`SequenceNumber = ItemIndex + 1` 是一基文件名序号。
- `EffectivePattern` 在 `TotalCount > 1` 时一定包含且只包含一个 `{index}`。
- `OutputPath` 已包含最终扩展名和 AutoRename 结果；任务运行期间不因前项成功、失败、跳过或取消而重新分配。
- `Decision = Skip` 时 `Reason` 必须是 `OutputFileAlreadyExists`；`Process` 时 `Reason` 必须为空。
- 计划构造失败属于 `StartRejected`，不创建父子 Job、不创建输出目录或临时文件。
- 未知占位符、非法格式或展开后重复路径返回 `InvalidOutputNamingPattern / Validation`。
- 序号与 `AutoRename` 冲突编号是两套语义：`holiday_001.webp` 已存在时，AutoRename 可以得到 `holiday_001_1.webp`。

输入列表在草稿期增加、移除时可以重新生成预览；正式提交后计划冻结。终态恢复建立普通新批量草稿，按新草稿顺序重新编号，不复写旧任务的 `BatchOutputPlan`。

## 25. 图片处理资源预检

四类单张 Workflow 和三类批量 Workflow 统一读取 `IImageProcessor.Capabilities.Resources`，不能各自硬编码最大文件、宽高或像素数。第一阶段默认能力为输入文件 `512 MiB`、输入/输出单边 `32768 px`、输入/输出总像素 `128000000 px`。

单张调用顺序：

```text
文件存在性与字节数
  -> 轻量 Probe / 图片头信息
  -> checked long 输入像素校验
  -> Core 参数解析
  -> checked long 计划输出像素校验
  -> 输出计划与其他前置检查
  -> 创建 ImageJob
  -> IImageProcessor 正式处理
```

文件或尺寸超限在 Job 创建前分别返回 `StartRejected(InputFileTooLarge)` 或 `StartRejected(ImageDimensionsExceedLimit)`。Magick 实际运行时仍可能返回 `ImageResourceLimitExceeded / InsufficientDiskSpace`；前置检查通过不代表 Workflow 可以忽略这些结果。

批量按照 [Job 状态编排设计](job-state-orchestration.md) 的资源影响范围执行：单项文件、尺寸、内存或像素缓存限制只使当前项 Failed 并继续；输出卷或公共私有缓存位置真实磁盘不足使当前项 Failed 后中止批次。批量输入收集阶段不把资源超限项目归类成 `BatchInputPlan.SkippedItems`。

资源数值属于图片处理能力，不读取 `AppSettings`，运行期间也不因设置页面变化而改变。Workflow 不分配 Memory/Map/Disk 配额；这些上限由组合根在图片引擎初始化时配置，实际资源由引擎在任务中按需申请。

## 26. 诊断作用域与记录边界

每个公开 Workflow 调用必须处于一个 OperationId 诊断作用域中。Desktop 已建立作用域时直接继承；Headless 调用没有外层作用域时，由 Workflow 入口创建。Job 创建后把现有 JobId 或 BatchId 加入作用域，批量单项再加入 ItemIndex 和子 JobId。

Workflow 只记录用例开始、正式终态、耗时、稳定错误码和必要的恢复分类，不把日志当作业务结果，也不因日志写入失败改变 Core 状态。用户取消使用 Information；正常 Skip 和创建前校验不使用 Error；未预期异常才生成 DiagnosticId。

捕获并转换原始异常的最内侧边界负责记录一次完整的脱敏异常，Workflow 外层只记录终态，不重复附加相同调用栈。Workflow 不整体序列化 `AtomPixError.Details`，也不记录完整输入/输出路径或文件名。具体字段、级别、隐私和存储规则见 [诊断与本地日志设计](../infrastructure/diagnostics-and-logging.md)。
