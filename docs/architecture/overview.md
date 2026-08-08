# AtomPix 架构设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25
>
> 基线范围：当前文档定义的模块分层、依赖方向、目录规划和实现约束
>
> 变更规则：实现阶段如需调整本文档定义的设计边界，应先说明原因并同步更新模块文档。

## 1. 产品定位

AtomPix 是一款跨平台桌面图片工具，第一阶段面向以下场景：

- 本地图片浏览和预览。
- 图片格式转换。
- 图片压缩，尽量减小文件体积并保持清晰度。
- 图片尺寸调整与矩形裁剪。
- 压缩、转换、调整尺寸的批量处理；批量裁剪暂缓。

第一阶段具体功能范围以 `docs/product/mvp-scope.md` 为准。本文档只定义这些功能应落入怎样的工程分层和模块边界。

## 2. 核心设计原则

- 商业发布友好：第三方库必须明确许可证和商用边界。
- UI 与业务解耦：ViewModel 不直接调用图片库、文件存储或 Avalonia 系统交互实现。
- 图片引擎可替换：Magick.NET 是第一阶段实现，不是架构核心。
- 依赖向内收敛：Core 位于中心，外层模块依赖内层契约。
- 先保持边界清晰，再控制实现复杂度。

## 3. 当前技术选择

| 领域 | 当前选择 | 说明 |
| --- | --- | --- |
| 桌面框架 | Avalonia | 跨平台桌面 UI 基础。 |
| UI 控件 | AtomUI | 基于 Avalonia 的 Ant Design 风格控件库。 |
| 图片处理 | Magick.NET | 第一阶段承担图片读取、预览数据生成、转换、压缩、裁剪、缩放等能力。 |
| NativeAOT | 暂不强制 | 仍保持清晰依赖边界，后续可单独验证。 |
| 架构层 | Core / Workflows / Imaging / Infrastructure / Desktop | 按洋葱架构思想组织。 |

## 4. 运行流程

从用户操作到图片处理的典型运行流程如下：

```text
Desktop View / ViewModel
  -> Workflows
    -> Core 业务规则
    -> Imaging.Abstractions
      -> Imaging.Magick
        -> Magick.NET
    -> Infrastructure 端口实现
```

例如，用户触发单张图片压缩：

```text
CompressViewModel
  -> CompressImageWorkflow
    -> OutputNamingPolicy
    -> IImageProcessor.CompressAsync()
      -> MagickImageProcessor
        -> Magick.NET
```

压缩请求中的 `CompressionProfile` 是提交时快照：Custom 必须携带 `1..100` 的明确质量，批量任务对所有有损项目共享同一个值；Smart 的候选序列属于 Imaging.Magick 内置策略，不进入 Desktop 或设置项。处理器通过 `ImageCompressResult.AppliedQuality` 返回最终实际质量，Workflow 原样投影，Desktop 不自行推断；无损输出使用 `null`。

图片处理结果统一以 `SizeDeltaBytes = OutputSizeBytes - InputSizeBytes` 表达体积变化。负数、零和正数分别投影为减少、不变和增加；批量只聚合成功且输入/输出大小完整的项目，Workflow 和 Desktop 不把失败、跳过、取消或未开始项的缺失大小补成零参与业务统计。

图片处理资源采用“公共硬边界 + 引擎运行上限 + Workflow 恢复语义”三层保护。`Imaging.Abstractions` 声明输入、输出尺寸和文件体积能力，Workflow 在创建单张 Job 前预检，具体图片引擎仍需防御性复检。Magick 的 memory、map、disk 和 thread 配置只是进程级最大使用量：应用启动时设置上限，不提前分配内存、不预建指定大小的磁盘文件，也不常驻创建上限数量的工作线程。资源只在实际任务中按需申请并在任务结束后释放。

诊断采用跨外层模块的结构化日志作用域：一次用户命令共享 OperationId，Job 创建后追加 JobId/BatchId，只有未预期错误生成可复制 DiagnosticId。Core 与 Imaging.Abstractions 不依赖日志；Infrastructure 提供本地滚动 Provider 和统一隐私过滤，产品构建默认不记录完整路径、文件名、图片内容或元数据，也不上传遥测。完整规则见 [诊断与本地日志设计](../modules/infrastructure/diagnostics-and-logging.md)。

运行时调用关系不等同于编译期项目引用。项目引用必须围绕 Core 和契约层收敛，不能让 UI、Magick.NET 或存储实现向内层泄漏。

## 5. 模块分层

| 层 / 模块 | 物理项目 | 职责 |
| --- | --- | --- |
| Core | `AtomPix.Core` | 产品业务核心，定义任务、策略、配置模型、错误、结果和纯业务规则。 |
| Imaging Abstractions | `AtomPix.Imaging.Abstractions` | 图片处理子系统契约，定义 `IImageProcessor`、请求/结果模型、图片格式和预览数据结构。 |
| Workflows | `AtomPix.Workflows` | 用户流程编排，把 UI 动作转换为应用流程。 |
| Imaging Magick | `AtomPix.Imaging.Magick` | 基于 Magick.NET 实现图片处理契约。 |
| Infrastructure | `AtomPix.Infrastructure` | 配置、日志、文件系统、路径解析、本地存储等技术实现。 |
| Desktop | `AtomPix.Desktop` | Avalonia / AtomUI 桌面入口，承载 Views、ViewModels、资源、交互和 DI 组合根。 |

## 6. 项目依赖关系

推荐项目引用方向：

```text
AtomPix.Desktop
  -> AtomPix.Workflows
  -> AtomPix.Core
  -> AtomPix.Imaging.Abstractions
  -> AtomPix.Imaging.Magick
  -> AtomPix.Infrastructure

AtomPix.Workflows
  -> AtomPix.Core
  -> AtomPix.Imaging.Abstractions

AtomPix.Imaging.Magick
  -> AtomPix.Imaging.Abstractions
  -> AtomPix.Core

AtomPix.Infrastructure
  -> AtomPix.Core

AtomPix.Core
  -> 无项目依赖

AtomPix.Imaging.Abstractions
  -> AtomPix.Core
```

依赖收敛视角：

```text
AtomPix.Core
  <- AtomPix.Infrastructure
  <- AtomPix.Imaging.Abstractions
  <- AtomPix.Imaging.Magick
  <- AtomPix.Workflows
  <- AtomPix.Desktop

AtomPix.Imaging.Abstractions
  <- AtomPix.Imaging.Magick
  <- AtomPix.Workflows
  <- AtomPix.Desktop
```

`AtomPix.Core` 是最内层项目。`AtomPix.Imaging.Abstractions` 可以单向依赖 Core，以复用稳定的结果、路径、策略和错误类型；Core 绝不反向依赖图片契约。图片引擎专有概念仍不得进入 Core。

## 7. 强约束

- `AtomPix.Core` 不依赖 Avalonia、AtomUI、Magick.NET、数据库、配置文件实现、日志框架或网络 API 实现。
- `AtomPix.Imaging.Abstractions` 不依赖 Magick.NET、SkiaSharp、ImageSharp、Avalonia 或 AtomUI。
- `AtomPix.Imaging.Magick` 不依赖 Desktop，不泄漏 `ImageMagick` 类型到契约层。
- `AtomPix.Infrastructure` 不编写用户用例流程，只提供外部能力实现。
- `AtomPix.Workflows` 不依赖 Avalonia、AtomUI、Magick.NET 具体实现或 Infrastructure 具体实现。
- `AtomPix.Desktop` 可以引用实现模块，但 ViewModel 不直接调用 `MagickImageProcessor`、`JsonAppSettingsStore` 等具体实现。
- ViewModel 不持有 Avalonia `Window` / `TopLevel`，也不直接调用 `StorageProvider`、`Launcher` 等框架系统 API；文件选择、目录选择和打开目录通过 Desktop 内部的轻量适配接口完成。
- Avalonia 类型只允许存在于 View、框架适配器和组合根。更换 UI 框架时允许重写这些外层实现，但不能要求 Core、Workflows 或 Imaging 修改业务契约。
- 组合根位于 `AtomPix.Desktop`，由它完成具体实现到接口的注册。

## 8. 工程目录规划

推荐目录结构：

```text
AtomPix/
  AtomPix.slnx
  Directory.Build.props
  Directory.Packages.props

  docs/
    overview.md
    product/
      mvp-scope.md
    architecture/
      overview.md
    modules/
      overview.md
      core/
        overview.md
      workflows/
        overview.md
        job-state-orchestration.md
      imaging-abstractions/
        overview.md
      imaging-magick/
        overview.md
      infrastructure/
        overview.md
        diagnostics-and-logging.md
      desktop/
        overview.md
        interaction-state-design.md
        atomui-component-mapping.md

  src/
    AtomPix.Core/
      AtomPix.Core.csproj
      Compression/
      Conversion/
      Resize/
      Crop/
      Jobs/
      Output/
      Results/
      Settings/
      ValueObjects/

    AtomPix.Imaging.Abstractions/
      AtomPix.Imaging.Abstractions.csproj
      Formats/
      Metadata/
      Capabilities/
      Processing/
      Preview/
      Requests/
      Results/

    AtomPix.Imaging.Magick/
      AtomPix.Imaging.Magick.csproj
      Processing/
      Mapping/
      Resources/
      DependencyInjection/

    AtomPix.Infrastructure/
      AtomPix.Infrastructure.csproj
      Configuration/
      
      FileSystem/
      Logging/
      Paths/
      Storage/
      DependencyInjection/

    AtomPix.Workflows/
      AtomPix.Workflows.csproj
      Browsing/
      Preview/
      Compression/
      Conversion/
      Resize/
      Crop/
      Batch/
      Output/
      Settings/
      DependencyInjection/

    AtomPix.Desktop/
      AtomPix.Desktop.csproj
      App.axaml
      App.axaml.cs
      Program.cs
      Assets/
      Composition/
      Shell/
      Navigation/
      Platform/
      Views/
      ViewModels/
      Dialogs/
      Resources/

  tests/
    AtomPix.Core.Tests/
    AtomPix.Imaging.Abstractions.Tests/
    AtomPix.Workflows.Tests/
    AtomPix.Imaging.Magick.Tests/
    AtomPix.Infrastructure.Tests/
    AtomPix.Desktop.Tests/
```

## 9. 后续扩展

图片引擎扩展应通过新增实现模块完成，例如：

```text
AtomPix.Imaging.Skia
AtomPix.Imaging.ImageSharp
```

新增实现必须实现 `AtomPix.Imaging.Abstractions` 中的契约，不能要求 Desktop 或 Workflows 改成依赖具体图片库。
