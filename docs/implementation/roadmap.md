# AtomPix 第一阶段实现路线图

> 文档状态：实现路线讨论基线
>
> 基线时间：2026-06-26
>
> 基线范围：当前文档定义从空项目到 headless MVP 验证的实现顺序、阶段目标和验收口径
>
> 变更规则：调整实现顺序、阶段边界或阶段验收要求时，应先更新本文档。

## 1. 总原则

第一阶段实现遵循：

```text
底层先行
契约先行
测试同步
UI 最后
```

在 Desktop / UI 进入实现前，应先完成 Core、Imaging.Abstractions、Infrastructure、Imaging.Magick、Workflows 的 headless 实现和测试。

实现过程不以“页面能打开”为阶段目标，而以“业务能力能被 headless 测试验证”为阶段目标。

## 2. 阶段总览

```text
Phase 0: 工程骨架
Phase 1: Core + Core.Tests
Phase 2: Imaging.Abstractions
Phase 3: Infrastructure + Infrastructure.Tests
Phase 4: Imaging.Magick + Imaging.Magick.Tests
Phase 5: Workflows + Workflows.Tests
Phase 6: Headless 业务场景测试
Phase 7: 发布验证
Phase 8: Desktop / UI 规划与实现
```

`Phase 8` 明确放在最后，不在底层 headless 能力稳定前展开。

## 3. Phase 0: 工程骨架

目标：创建解决方案、项目结构和基础工程配置。

生产项目：

```text
src/AtomPix.Core/
src/AtomPix.Imaging.Abstractions/
src/AtomPix.Imaging.Magick/
src/AtomPix.Infrastructure/
src/AtomPix.Workflows/
```

测试项目：

```text
tests/AtomPix.Core.Tests/
tests/AtomPix.Infrastructure.Tests/
tests/AtomPix.Imaging.Magick.Tests/
tests/AtomPix.Workflows.Tests/
```

暂不创建或不实现：

```text
AtomPix.Desktop
AtomPix.Desktop.Tests
```

如工程模板需要，也可以先创建空的 `AtomPix.Desktop`，但不进入 UI 设计、页面实现或 UI 测试。

阶段验收：

```text
dotnet build
```

## 4. Phase 1: Core + Core.Tests

目标：实现业务核心模型、值对象、结果模型、策略和端口。

实现范围：

```text
OperationResult / AtomPixError
LocalPath
CompressionProfile
ConversionProfile
OutputPolicy
ImageJob / BatchJob
SubscriptionState / FeatureAccessPolicy
AppSettings
RecentItem
基础设施端口
```

测试类型：Round 1 单元测试。

测试重点：

```text
结果模型成功/失败约束
错误码和错误分类
压缩配置默认值和校验
转换配置默认值和校验
输出策略默认值和约束
任务状态和批量状态语义
FeatureAccessPolicy: Active 全开，Free 只允许免费功能
AppSettings 默认值
LocalPath 基础校验
```

阶段验收：

```text
dotnet test tests/AtomPix.Core.Tests/AtomPix.Core.Tests.csproj
```

## 5. Phase 2: Imaging.Abstractions

目标：实现图片处理契约项目。

实现范围：

```text
IImageProcessor
ImageFormatKind
ImageProbeRequest / ImageProbeResult
ImagePreviewRequest / ImagePreviewResult
ImageCompressRequest / ImageCompressResult
ImageConvertRequest / ImageConvertResult
ImageProcessorCapabilities
```

测试类型：编译期契约检查为主。

阶段重点：

- `AtomPix.Imaging.Abstractions` 可以依赖 `AtomPix.Core`。
- 不依赖 Magick.NET、Avalonia、AtomUI。
- 公共模型使用 `LocalPath`，不使用裸 `string` 表达本地路径。
- 返回 `OperationResult<T>`。

阶段验收：

```text
dotnet build src/AtomPix.Imaging.Abstractions/AtomPix.Imaging.Abstractions.csproj
```

## 6. Phase 3: Infrastructure + Infrastructure.Tests

目标：实现 Core 基础设施端口的本地实现。

实现范围：

```text
JsonAppSettingsStore
LocalSubscriptionStore
JsonRecentItemsStore
LocalFileSystemService
AppPathProvider
Infrastructure DI 注册扩展
```

测试类型：Round 2 契约测试。

测试重点：

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
不写用户真实 AppData
不在 Infrastructure 中执行 OverwritePolicy 决策
```

阶段验收：

```text
dotnet test tests/AtomPix.Infrastructure.Tests/AtomPix.Infrastructure.Tests.csproj
```

## 7. Phase 4: Imaging.Magick + Imaging.Magick.Tests

目标：实现 Magick.NET 图片处理引擎。

实现范围：

```text
MagickImageProcessor
MagickFormatMapper
MagickMetadataMapper
MagickExceptionMapper
MagickImageProcessorOptions
Imaging.Magick DI 注册扩展
```

测试类型：Round 2 契约测试。

测试样本：

```text
tests/TestAssets/Images/
  jpeg-basic.jpg
  png-alpha.png
  webp-basic.webp
  bmp-basic.bmp
  gif-animated.gif
  tiff-basic.tiff
```

测试重点：

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
Magick.NET 异常不穿透
```

阶段验收：

```text
dotnet test tests/AtomPix.Imaging.Magick.Tests/AtomPix.Imaging.Magick.Tests.csproj
```

## 8. Phase 5: Workflows + Workflows.Tests

目标：实现用户流程编排层。

实现范围：

```text
OpenImageWorkflow
CreatePreviewWorkflow
CompressImageWorkflow
ConvertImageWorkflow
BatchCompressWorkflow
BatchConvertWorkflow
LoadSettingsWorkflow
SaveSettingsWorkflow
Workflows DI 注册扩展
```

测试类型：Round 2 契约测试。

测试替身：

```text
FakeImageProcessor
FakeFileSystemService
FakeAppSettingsStore
FakeSubscriptionStore
FakeRecentItemsStore
```

测试重点：

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

阶段验收：

```text
dotnet test tests/AtomPix.Workflows.Tests/AtomPix.Workflows.Tests.csproj
```

## 9. Phase 6: Headless 业务场景测试

目标：在没有 UI 的情况下，用真实实现模拟用户业务动作。

组合：

```text
Real Workflows
Real Infrastructure
Real Imaging.Magick
TestAssets 图片样本
测试临时目录
```

典型场景：

```text
PNG -> WebP 转换
JPEG 平衡压缩
多图批量压缩
多图批量转换
AutoRename 输出
Skip 输出
免费用户批量压缩被拦截
订阅有效用户批量压缩通过
设置不存在时加载默认设置
设置损坏时返回失败
```

测试放置：

```text
第一阶段可放在 AtomPix.Workflows.Tests
后续规模扩大后再拆 AtomPix.Integration.Tests
```

阶段验收：

```text
dotnet test
```

## 10. Phase 7: 发布验证

目标：验证无 UI 或最小入口工程下的构建、测试和发布链路。

基础命令：

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

NativeAOT 暂作为实验项：

```text
dotnet publish -c Release -r win-x64 /p:PublishAot=true
```

NativeAOT 失败不阻塞第一阶段 MVP，但必须记录失败原因和阻塞点。

## 11. Phase 8: Desktop / UI

只有在底层 headless 能力稳定后，才进入 Desktop / UI。

进入条件：

```text
Core.Tests 通过
Infrastructure.Tests 通过
Imaging.Magick.Tests 通过
Workflows.Tests 通过
Headless 业务场景测试通过
```

UI 阶段另行设计：

```text
信息架构
页面布局
交互流程
AtomUI 控件选择
ViewModel 状态边界
UI 测试策略
```

UI 规划和原型图强依赖用户确认，因此不在底层实现阶段提前冻结。