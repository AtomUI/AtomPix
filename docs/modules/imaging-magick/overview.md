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
- 元数据保留或移除。
- 面向 DI 的服务注册扩展。

## 3. 禁止包含

- Avalonia、AtomUI、ViewModel 或 UI 状态。
- 授权、额度、订阅计划等商业规则。
- 输出命名策略和用户流程编排。
- 配置文件保存、订阅状态存储、订阅状态存储。
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
5. 判断 HasAlpha。
6. 判断 IsAnimated / FrameCount。
7. 判断 HasMetadata。
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
有透明通道 -> PNG
无透明通道 -> JPEG
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
4. 应用 ResizePolicy。
5. 应用 MetadataPolicy。
6. 根据原格式或输出路径扩展名决定输出格式。
7. 根据 CompressionMode / ImageQuality 设置质量。
8. 写入 OutputPath。
9. 返回输入和输出文件大小。
```

压缩质量映射：

| 模式 | 质量 |
| --- | --- |
| `HighQuality` | `90` |
| `Balanced` | `80` |
| `Maximum` | `65` |
| `Custom` | 用户指定 |
| `Smart` | 按格式策略决定 |

`Smart` 初始策略：

```text
JPEG -> 82
WebP -> 80
PNG -> 保持无损优化，不做激进颜色量化
```

PNG 策略：

- 第一阶段不做激进颜色量化，避免明显损伤。
- 可应用元数据移除和基础无损优化。
- 如果用户希望显著减小 PNG，推荐通过转换为 WebP 实现。

### 8.6 ConvertAsync 实现策略

`ConvertAsync` 实现单张图片格式转换。

流程：

```text
1. 读取图片。
2. 自动处理 EXIF 方向。
3. 多帧输入按第一阶段策略拒绝处理。
4. 应用 ResizePolicy。
5. 应用 MetadataPolicy。
6. 设置 Profile.OutputFormat 对应的目标格式。
7. 如果目标格式支持质量参数，应用 Quality。
8. 写入 OutputPath。
9. 返回输入格式、输出格式和前后文件大小。
```

输出格式映射：

```text
OutputImageFormat.Jpeg -> MagickFormat.Jpeg
OutputImageFormat.Png  -> MagickFormat.Png
OutputImageFormat.WebP -> MagickFormat.WebP
```

### 8.7 动画和多帧处理策略

第一阶段策略：

```text
Probe:
  识别多帧和动画。

Preview:
  只显示第一帧。

Compress / Convert:
  默认拒绝多帧输入。
```

原因：

- 多帧 GIF / WebP / TIFF 的压缩和转换语义更复杂。
- 第一阶段优先保证单帧主流图片处理稳定。
- 后续如需支持动画，应单独设计多帧任务模型、预览和输出策略。

### 8.8 异常映射

Magick.NET 异常不得穿透到 Workflows 或 Desktop。

映射建议：

| 场景 | AtomPixErrorCode | Category |
| --- | --- | --- |
| 读取失败 | `ImageReadFailed` | `ImageProcessing` |
| 写入失败 | `ImageWriteFailed` | `ImageProcessing` 或 `FileSystem` |
| 压缩失败 | `ImageCompressFailed` | `ImageProcessing` |
| 转换失败 | `ImageConvertFailed` | `ImageProcessing` |
| 预览失败 | `ImagePreviewFailed` | `ImageProcessing` |
| 输入格式不支持 | `UnsupportedInputFormat` | `UnsupportedFormat` |
| 输出格式不支持 | `UnsupportedOutputFormat` | `UnsupportedFormat` |
| 用户取消 | `OperationCanceled` | `Cancellation` |
| 未预期异常 | `Unknown` | `Unexpected` |

实现层应保留必要诊断信息到 `AtomPixError.Details`，但不得暴露 Magick.NET 对象实例。

### 8.9 设计边界

- 本模块只实现 `AtomPix.Imaging.Abstractions`。
- 本模块不依赖 Desktop、Workflows 或 Infrastructure。
- 本模块不处理订阅、权益、输出命名、覆盖策略或批量任务状态。
- Workflows 传入的 `OutputPath` 已经是策略决策后的最终路径。
- 本模块负责把图片内容写入该路径。
## 9. Imaging.Magick 工业级硬化基线

`AtomPix.Imaging.Magick` 是图片契约的具体实现层。它必须严格服从 `AtomPix.Imaging.Abstractions` 的能力声明，不能因为 Magick.NET 能做更多事情就擅自扩大 AtomPix 第一阶段能力边界。

### 9.1 请求边界

公共方法必须满足：

- null 请求属于调用方编程错误，直接抛出 `ArgumentNullException`。
- 用户可预期失败必须返回 `OperationResult<T>.Failure`。
- 已取消的 `CancellationToken` 必须映射为 `OperationCanceled`，不能映射为图片读取、压缩或转换失败。

### 9.2 输入格式边界

读取成功后，必须把 Magick.NET 格式映射为 `ImageFormatKind`，并检查是否包含在 `Capabilities.SupportedInputFormats` 中。

- 无法识别或未声明支持的输入格式返回 `UnsupportedInputFormat`。
- 多帧图片第一阶段仍只允许 `Probe` 和第一帧 `Preview`，`Compress` / `Convert` 返回 `UnsupportedInputFormat`。

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

### 9.4 写入路径边界

Workflows 传入的 `OutputPath` 已经是最终路径。Magick 实现只负责：

- 当输出路径包含目录时创建目录。
- 当输出路径只是文件名时，不调用 `Directory.CreateDirectory` 创建空目录。
- 不处理覆盖、跳过、自动重命名或输出命名策略。

### 9.5 异常映射

Magick.NET、IO、权限、路径参数和平台不支持异常必须被捕获并转换为 AtomPix 错误模型，不得让 `ImageMagick` 异常穿透到 Workflows 或 Desktop。

### 9.6 测试要求

`AtomPix.Imaging.Magick.Tests` 至少覆盖：

- 主流输入格式探测。
- PNG alpha 预览与 JPEG 预览 MIME。
- JPEG 压缩、PNG/WebP/JPEG 转换。
- 多帧输入拒绝压缩和转换。
- 缺失文件、取消 token、非法输出格式和 null 请求。
## 10. 压缩效果验收补充

第一阶段不能只验证“能写出文件”，还必须验证最低限度的处理效果：

- JPEG 使用 `Maximum` 压缩时，复杂样本输出体积应小于输入体积。
- 压缩不应改变图片尺寸，除非请求中明确包含 `ResizePolicy`。
- PNG alpha 转 WebP 后应保持尺寸，并写出真实 WebP 格式。

这些测试不是最终图像质量评价体系，只是防止压缩/转换实现退化为无意义写出。
## 11. Resize 行为验收补充

Magick 实现必须证明 `ResizePolicy` 在压缩和转换中真实生效：

- `FitWithinBounds` 应保持比例，并保证输出宽高不超过边界。
- `Percentage` 应按比例缩放输出尺寸。
- 未指定 resize 时，压缩和转换不应改变尺寸。

当前测试覆盖：

- JPEG 压缩时应用 `FitWithinBounds(60, 60)`，输出为 60x40。
- PNG 转 WebP 时应用 `ScaleByPercentage(50)`，输出为 60x40。
## 12. 处理结果细节填充

Magick 实现必须为压缩和转换结果填充 `ImageProcessingDetails`：

- `InputWidth` / `InputHeight` 使用自动方向处理后的输入尺寸。
- `OutputWidth` / `OutputHeight` 使用写出前的最终尺寸。
- `ResizeApplied` 根据输入尺寸和输出尺寸是否变化判断。
- `MetadataRemoved` 根据请求中的 `MetadataPolicy.Remove` 判断。
- `LossyOutput` 对 JPEG / WebP 返回 true，对 PNG 返回 false。

当前不把这些细节塞进 `ImageJobResult`，避免任务模型过早膨胀。需要展示详细报告时，再设计报告模型。
## 13. 无效图片错误映射补充

Magick 实现必须把无效图片和损坏图片映射为用户可解释错误：

| 场景 | AtomPixErrorCode | Category |
| --- | --- | --- |
| 文件不存在 | `InputFileNotFound` | `FileSystem` |
| Probe/Preview 读取到 Magick.NET 图片解析异常 | `InvalidImageFile` | `ImageProcessing` |
| Probe 期间发生 IO、权限、路径或平台读取异常 | `ImageReadFailed` | `ImageProcessing` |
| Compress 直接处理损坏图片失败 | `ImageCompressFailed` | `ImageProcessing` |
| Convert 直接处理损坏图片失败 | `ImageConvertFailed` | `ImageProcessing` |

产品流程中，Workflows 会先调用 `ProbeAsync` 做预检，因此用户触发压缩/转换时遇到损坏图片，应优先表现为 `InvalidImageFile`，而不是笼统的压缩或转换失败。

## 14. 输出写入安全补充

Magick 实现写出压缩或转换结果时，不能直接把编码过程写入最终 `OutputPath`。

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
- 失败时返回 `ImageCompressFailed` 或 `ImageConvertFailed`，不得把半成品伪装为成功结果。

该策略不能替代真正的备份/恢复系统，但第一阶段必须避免“图片编码写到一半，最终输出路径留下半成品文件”。

## 15. 输出路径目录边界补充

Magick 实现接收的 `OutputPath` 已经是 Workflows 决策后的最终路径。

约束：

- 如果 `OutputPath` 包含目录部分，Magick 可以创建缺失目录。
- 如果 `OutputPath` 只有文件名，Magick 不创建空目录，直接按当前工作目录写入。
- 临时文件写入策略仍使用最终输出路径所在目录；只有文件名时，临时文件也位于当前工作目录。
- Magick 不解析 `OutputPolicy`，不参与 SameAsInput、Subfolder、CustomDirectory、Skip、Overwrite 或 AutoRename 决策。

## 16. Imaging.Magick DI 注册与取消边界补充

`AtomPix.Imaging.Magick.DependencyInjection` 提供 `AddAtomPixMagickImaging()`。

注册内容：

- `IImageProcessor -> MagickImageProcessor`

取消边界：

- Magick 实现会在入口检查 `CancellationToken`，并返回 `OperationCanceled`。
- 第一阶段不承诺中途强行终止 Magick.NET 同步读写或编码过程。
- Workflows 负责在批量项之间检查取消，并停止后续未开始项。
