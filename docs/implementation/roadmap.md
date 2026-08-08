# AtomPix 第一阶段实现路线图

> 文档状态：首轮 Headless 实施记录 + 当前目标实施路线
>
> 基线时间：2026-06-26
>
> 基线范围：当前文档定义从空项目到 headless MVP 验证的实现顺序、阶段目标和验收口径
>
> 变更规则：调整实现顺序、阶段边界或阶段验收要求时，应先更新本文档。

> 时间语义：Phase 0–7 保留 2026-06-26 首轮 Headless 建设顺序与验收事实；其中出现的内嵌 Resize、旧结果字段或旧目录行为只描述当时代码。现行目标契约以 Product、Modules 与本文 Phase 8 为准，不得用历史实现反向覆盖正式设计。

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
tests/AtomPix.Imaging.Abstractions.Tests/
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
RgbColor / TransparencyPolicy
OutputPolicy
ImageJob / BatchJob
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
透明处理策略与结果组合不变量
输出策略默认值和约束
任务状态和批量状态语义
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
ImageResizeRequest / ImageResizeResult
ImageCropRequest / ImageCropResult
ImageProcessorCapabilities
ImageResourceCapabilities
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
JsonRecentItemsStore
LocalFileSystemService
AppPathProvider
LocalRollingLogProvider
LogPrivacyFilter
Infrastructure DI 注册扩展
```

测试类型：Round 2 契约测试。

测试重点：

```text
settings.json 不存在 -> 默认设置
settings.json 损坏 -> Failure
recent-items.json 不存在 -> 空列表成功
recent-items.json 损坏 -> 空列表成功
CreateDirectoryAsync
GetFileSizeAsync
BuildIndexedPath
不写用户真实 AppData
不在 Infrastructure 中执行 OverwritePolicy 决策
JSON Lines 日志滚动、7 天 / 50 MiB 保留上限
日志路径、文件名、异常消息与 Details 默认脱敏
日志写入或清理失败不改变业务结果
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
PNG alpha channel -> HasAlphaChannel = true
opaque RGBA -> HasTransparency = false
transparent PNG -> HasTransparency = true
ICC sample -> HasColorProfile = true independently of HasMetadata
GIF animated -> IsAnimated = true
Preview JPEG -> JPEG bytes
Preview PNG alpha -> PNG bytes
Compress JPEG Balanced
Compress WebP Balanced
Compress PNG Smart
Convert PNG -> WebP
Convert WebP -> JPEG
Resize JPEG / PNG / BMP / 单帧 WebP 并保持格式
Crop JPEG / PNG / BMP / 单帧 WebP 并保持格式
Transparent PNG -> JPEG explicit background flatten
Metadata Remove/Preserve keeps ICC and normalizes Orientation
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
Workflows DI 注册扩展
```

测试类型：Round 2 契约测试。

测试替身：

```text
FakeImageProcessor
FakeFileSystemService
FakeAppSettingsStore
FakeRecentItemsStore
```

测试重点：

```text
OpenImageWorkflow 调用 Probe
CreatePreviewWorkflow 调用 CreatePreview
输出路径策略：Subfolder / suffix / AutoRename
文件名格式：CustomPattern + {name} / {index}
BatchOutputPlan：多项默认三位序号、计划路径冻结且唯一
BatchExecutionProgress：序号、单项变化与最终快照一致
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
多图批量调整尺寸
单张矩形裁剪
打开文件夹进入浏览集合而非批量任务
AutoRename 输出
Skip 输出
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

## 11. Phase 8: 目标契约补齐与 Desktop / UI

Desktop 信息架构、原型和逐控件状态设计已经完成，不再“另行设计”。进入页面代码实现前，必须先补齐该页面依赖的正式目标契约，或提供语义等价的适配实现。

进入条件：

```text
Core.Tests 通过
Infrastructure.Tests 通过
Imaging.Magick.Tests 通过
Workflows.Tests 通过
Headless 业务场景测试通过
```

已冻结的设计入口：

```text
docs/ui-prototype/README.md
docs/modules/desktop/overview.md
docs/modules/desktop/interaction-state-design.md
docs/modules/desktop/atomui-component-mapping.md
docs/modules/workflows/job-state-orchestration.md
docs/implementation/testing-and-release.md（第 32–46 节）
```

目标契约补齐的优先范围包括：文件夹浏览、批量输入、实时批量进度、独立 Resize/Crop、Batch Resize、冻结输出计划、透明度和元数据策略、源文件保护、资源限制、中性体积统计与本地诊断。Desktop 实现不得复用历史内嵌 Resize 或旧 `Saved*` 口径绕过这些正式设计。

Desktop 首次落地时固定经过验证的 AtomUI/Avalonia 包版本，只注册主桌面控件和 ColorPicker 主题，并按组件映射文档的顺序先完成 Shell 与公共反馈，再实现图片视口、页面和虚拟化批量 ListView。禁止引用或注册 AtomUI DataGrid 包；`out-lib/AtomUI` 只是本地源码核对副本，不进入解决方案项目引用。

截至 2026-08-07，本阶段的第一阶段功能施工、Desktop 交互闭环和工程发布门禁已经贯通：正式 Shell、首页、浏览器、四类单张页面、三类批量页面、设置、最近记录、诊断边界和关闭确认均已接入生产组合根，通用空页已移除。浏览器有界缓存与离场释放、结果文件存活检查、批量终态只读和精确恢复也已完成。Avalonia/AtomUI 真实无头渲染与输入自动化、2000 项批处理、10000 行虚拟化、16MP 并发预览和日志压力测试均已成为可执行门禁；CI 对 Windows/Linux/macOS 进行构建与 UI 自动化，并在原生 Runner 生成自包含单文件归档、SHA-256 和启动烟测。具体终态以 `testing-and-release.md` 第 51 节为准。多 DPI 和发行主体的平台签名/公证仍属于发布验收与外部凭据工作，不得把密钥写入源码。屏幕阅读器、UIA 动作模式和全页面纯键盘巡检不属于当前版本需求。
