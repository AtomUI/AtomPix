# AtomPix.Workflows 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Workflows` 是用户流程编排层。

它负责把 Desktop 层传入的用户动作转换为明确的应用流程，例如打开图片、生成预览、压缩图片、格式转换、批量处理、保存设置、检查授权和更新额度。

Workflows 不属于 UI 层，也不属于图片库实现层。它只编排 Core 规则、图片处理契约和外部能力端口。

## 2. 允许包含

- 用户流程服务，例如 `CompressImageWorkflow`、`BatchConvertWorkflow`。
- 用例输入和输出对象。
- 应用级流程编排。
- 授权、额度、输出策略、图片处理调用之间的组合逻辑。
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
  Licensing/
  DependencyInjection/
```

## 5. 首批流程

- `OpenImageWorkflow`
- `CreatePreviewWorkflow`
- `CompressImageWorkflow`
- `ConvertImageWorkflow`
- `BatchCompressWorkflow`
- `BatchConvertWorkflow`
- `LoadSettingsWorkflow`
- `SaveSettingsWorkflow`
- `RefreshSubscriptionWorkflow`
- `GetFeatureAccessWorkflow`

## 6. 典型流程边界

压缩流程可以做：

```text
校验输入
检查授权与额度
根据 Core 策略计算输出路径
调用 IImageProcessor.CompressAsync
检查功能访问策略
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
CreatePreviewWorkflow
CompressImageWorkflow
ConvertImageWorkflow
BatchCompressWorkflow
BatchConvertWorkflow
LoadSettingsWorkflow
SaveSettingsWorkflow
```

订阅、激活和支付流程第一阶段只保留功能访问检查点，不展开复杂商业流程。

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
    ImageJobResult JobResult);
```

流程：

```text
1. 检查 FeatureId.SingleCompress。
2. 校验输入路径、压缩配置和输出策略。
3. 探测输入图片信息。
4. 根据 OutputPolicy、输入路径、目标格式和文件系统状态解析 OutputPath。
5. 调用 IImageProcessor.CompressAsync。
6. 组装 ImageJobResult。
```

边界：

- 单张压缩也返回 `ImageJobResult`，以保持单张和批量结果模型一致。
- OutputPath 由 Workflows 解析后传给图片处理契约。
- Workflows 不直接创建目录或写文件；具体文件系统能力通过端口实现。

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
    ImageJobResult JobResult);
```

流程：

```text
1. 检查 FeatureId.SingleConvert。
2. 校验输入路径、转换配置和输出策略。
3. 探测输入图片信息。
4. 根据 OutputPolicy、Profile.OutputFormat、输入路径和文件系统状态解析 OutputPath。
5. 调用 IImageProcessor.ConvertAsync。
6. 组装 ImageJobResult。
```

边界：

- 转换流程必须指定目标输出格式。
- 输入格式由图片探测结果决定。
- Workflows 不直接调用具体图片库。

### 8.6 BatchCompressWorkflow

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
    BatchResult BatchResult);
```

流程：

```text
1. 检查 FeatureId.BatchCompress。
2. 校验输入列表非空。
3. 创建 BatchJob 和 ImageJob 列表。
4. 逐项解析 OutputPath。
5. 逐项调用 IImageProcessor.CompressAsync。
6. 单项失败记录到 ImageJobResult。
7. 汇总 BatchResult。
```

第一阶段执行策略：

- 批量处理顺序执行。
- 支持取消。
- 允许部分成功。
- 不支持暂停、恢复和关机后继续。
- 后续再评估并发处理。

### 8.7 BatchConvertWorkflow

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
    BatchResult BatchResult);
```

流程：

```text
1. 检查 FeatureId.BatchConvert。
2. 校验输入列表非空。
3. 创建 BatchJob 和 ImageJob 列表。
4. 逐项解析 OutputPath。
5. 逐项调用 IImageProcessor.ConvertAsync。
6. 单项失败记录到 ImageJobResult。
7. 汇总 BatchResult。
```

第一阶段执行策略与批量压缩一致：顺序执行、支持取消、允许部分成功。

### 8.8 LoadSettingsWorkflow

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

### 8.9 SaveSettingsWorkflow

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

### 8.10 依赖边界

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
- 输出目录创建失败时，返回文件系统错误。
- 非法转换输出格式返回 `UnsupportedOutputFormat`，不能生成 `.img` 等模糊扩展名。
- 图片处理失败时，单张流程返回成功的 Workflow 结果，但其中 `ImageJobResult.Status = Failed`，用于批量汇总。

### 9.4 商业化入口

- Workflows 在入口检查对应 `FeatureId`。
- 免费/订阅归属由 `FeatureAccessPolicy` 决定。
- Desktop 只能展示状态和限制原因，不能作为最终裁判。
- Imaging.Magick 完全不知道订阅、收费或功能访问。

### 9.5 Headless 验收

Workflows 必须可以在无 UI 环境下串联真实 Infrastructure 和 Imaging.Magick，完成打开、预览、压缩、转换、批量、跳过、覆盖、自动重命名和部分失败等用户场景。
## 10. 最近记录与进度流程补充

### 10.1 AddRecentItemWorkflow

`AddRecentItemWorkflow` 负责把用户动作产生的最近记录写入 `IRecentItemsStore`。

流程：

```text
1. 加载现有最近记录。
2. 使用 RecentItemsPolicy 去重、排序、截断。
3. 保存更新后的最近记录。
4. 返回更新后的列表。
```

该流程不隐式绑定到 `OpenImageWorkflow`。Desktop 或更高层应用流程可以在打开图片成功、打开文件夹成功或处理输出成功后显式调用。

### 10.2 批量进度快照

当前第一阶段不引入异步事件流和并发队列，但 `BatchResult` 可以投影为 `BatchProgressSnapshot`，用于 headless 验收和未来 UI 进度展示。

后续若增加实时进度事件，应复用 `BatchProgressSnapshot` 或兼容它的字段语义。
## 11. 默认设置与前置能力校验补充

### 11.1 默认设置驱动流程

第一阶段新增默认设置驱动的图片流程：

- `CompressWithDefaultSettingsWorkflow`
- `ConvertWithDefaultSettingsWorkflow`

这两个流程只负责：

```text
1. 从 IAppSettingsStore 加载 AppSettings。
2. 读取默认 CompressionProfile / ConversionProfile / OutputPolicy。
3. 委托给 CompressImageWorkflow 或 ConvertImageWorkflow。
```

它们不复制压缩、转换、输出路径或功能访问逻辑。显式参数流程仍是核心流程，默认设置流程只是应用层便利入口。

### 11.2 前置探测与能力校验

单张压缩和转换在执行文件大小读取、输出路径解析和图片处理前，必须先执行图片探测与能力校验：

- 调用 `IImageProcessor.ProbeAsync`。
- 校验输入格式是否在 `Capabilities.SupportedInputFormats` 中。
- 当图片为动画或多帧且处理器不支持动画处理时，提前返回 `UnsupportedInputFormat`。
- 转换时校验目标格式是否在 `Capabilities.SupportedOutputFormats` 中。
- 压缩时根据输入格式推导输出格式，若不在输出能力范围内则提前失败。

目标是让 Workflows 提供稳定、可解释的失败语义，而不是把所有错误推迟到具体图片库写出阶段。
## 12. 批量默认设置与最终进度补充

### 12.1 批量默认设置流程

第一阶段新增：

- `BatchCompressWithDefaultSettingsWorkflow`
- `BatchConvertWithDefaultSettingsWorkflow`

流程职责与单张默认设置流程一致：

```text
1. 从 IAppSettingsStore 加载 AppSettings。
2. 读取默认 CompressionProfile / ConversionProfile / OutputPolicy。
3. 委托给 BatchCompressWorkflow 或 BatchConvertWorkflow。
```

默认设置流程不复制批量处理逻辑，不改变功能访问判断、输出路径策略或单项失败语义。

### 12.2 BatchResult.FinalProgress

批量流程返回值携带最终进度快照：

```text
BatchCompressResult.BatchResult
BatchCompressResult.FinalProgress
BatchConvertResult.BatchResult
BatchConvertResult.FinalProgress
```

`FinalProgress` 由 `BatchResult` 投影得到，用于 headless 验收和未来 UI 的完成态展示。第一阶段仍不实现实时进度事件流。

### 12.3 混合输入语义

批量流程遇到缺失文件、动画/多帧输入或不支持格式时：

- 单项记录为 `ImageJobStatus.Failed`。
- 不中断整个批次。
- 其他可处理图片继续处理。
- 批次最终状态按结果汇总为 `Succeeded`、`PartiallySucceeded`、`Failed` 或 `Canceled`。
## 13. 错误透传与批量失败语义补充

Workflows 的前置探测失败必须原样透传错误码，不重新包装成模糊错误。

约束：

- 单张压缩/转换在 `ProbeAsync` 返回失败时，直接返回该失败，不解析输出路径，不调用压缩或转换。
- 批量压缩/转换在单项 `ProbeAsync` 失败时，将该项记录为 `ImageJobStatus.Failed`，保留原始 `AtomPixError`。
- 批量流程继续处理其他输入，最终按单项结果汇总为 `PartiallySucceeded` 或 `Failed`。
- Workflows 不把 `InvalidImageFile` 改写为 `ImageCompressFailed` 或 `ImageConvertFailed`。

这保证 UI 层后续可以把“文件不存在”“图片损坏”“格式不支持”“动画暂不支持”等失败展示为不同用户提示。

## 14. 输出写入失败结果语义补充

Workflows 负责输出路径决策，但不负责图片文件编码写入。

当图片处理器返回压缩或转换失败时：

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
- 多点文件名只替换最后一个扩展名，例如 `archive.photo.png -> archive.photo_atompix.webp`。
- `AutoRename` 在完整目标文件名主体后追加索引，例如 `archive.photo_atompix.webp -> archive.photo_atompix_1.webp`。
- 压缩流程要求输入路径能够解析出扩展名，否则返回 `UnsupportedOutputFormat`，不继续解析输出路径或调用图片处理器。

文件名和目录策略的最终决策必须在 Workflows 完成；Infrastructure 只提供路径辅助，Imaging.Magick 只按最终路径写文件。

## 16. 默认设置加载失败语义补充

默认设置驱动的图片流程必须把设置加载视为前置条件。

约束：

- `CompressWithDefaultSettingsWorkflow` 加载设置失败时直接返回该失败，不调用探测、压缩或输出路径解析。
- `ConvertWithDefaultSettingsWorkflow` 加载设置失败时直接返回该失败，不调用探测、转换或输出路径解析。
- 批量默认设置流程同理，设置失败时不启动批量任务。
- 该规则适用于 settings.json 损坏、高版本 schema、权限失败和取消等场景。

这样可以避免在用户设置不可解释或版本不兼容时继续生成处理结果。

## 17. 取消、中断与批量进度语义补充

第一阶段取消语义：

- 取消统一使用 `OperationCanceled` + `Cancellation`。
- 取消不是失败，不能被包装为 `ImageCompressFailed` 或 `ImageConvertFailed`。
- 单张压缩/转换如果图片处理器返回取消，任务结果为 `ImageJobStatus.Canceled`。
- 批量流程开始前 token 已取消时，整体返回取消失败，不创建批量结果。
- 批量流程处理中途取消时：
  - 已完成项保留原始结果。
  - 当前检测到取消的项记录为 `Canceled`。
  - 后续未开始项不生成 `ImageJobResult`。
  - `BatchResult.Status = Canceled`。
  - `BatchResult.TotalCount` 保留原始输入数量。
  - `BatchResult.CompletedCount` 只统计已经产生结果的项。
  - `FinalProgress.IsCompleted` 可以为 false。

第一阶段不做并发批量处理，也不承诺强行中断 Magick.NET 正在执行的同步编码写入。当前保证 Workflow 边界和图片处理入口的取消语义稳定。

## 18. Workflows DI 注册补充

`AtomPix.Workflows.DependencyInjection` 提供 `AddAtomPixWorkflows()`。

注册内容：

- `IFeatureAccessPolicy -> DefaultFeatureAccessPolicy`
- `ImageWorkflowServices`
- 打开、预览、压缩、转换、批量、默认设置、设置和最近记录 workflows

Workflows DI 注册只绑定流程服务，不绑定 Infrastructure 或 Imaging 实现。Desktop 或 headless host 需要显式组合：

```text
AddAtomPixInfrastructure(...)
AddAtomPixMagickImaging()
AddAtomPixWorkflows()
```
