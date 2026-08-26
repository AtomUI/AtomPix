# AtomPix 模块文档索引

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25
>
> 基线范围：当前文档定义的模块职责、边界、依赖方向和目录规划
>
> 变更规则：实现阶段如需调整模块职责，应同步更新本文件和对应模块文档。

本文件是 AtomPix 模块文档的导航页，也是模块边界速查表。

## 1. 模块总览

| 模块 | 文档 | 物理项目 | 核心职责 |
| --- | --- | --- | --- |
| Core | [core/overview.md](core/overview.md) | `AtomPix.Core` | 产品业务核心，定义模型、值对象、策略、错误、结果和纯业务规则。 |
| Workflows | [workflows/overview.md](workflows/overview.md) | `AtomPix.Workflows` | 用户流程编排，把 UI 动作转换为应用流程。 |
| Imaging Abstractions | [imaging-abstractions/overview.md](imaging-abstractions/overview.md) | `AtomPix.Imaging.Abstractions` | 图片处理契约，定义图片引擎对外能力和请求/结果模型。 |
| Imaging Magick | [imaging-magick/overview.md](imaging-magick/overview.md) | `AtomPix.Imaging.Magick` | 基于 Magick.NET 实现图片处理契约。 |
| Infrastructure | [infrastructure/overview.md](infrastructure/overview.md) | `AtomPix.Infrastructure` | 配置、日志、文件系统、本地存储等技术实现。 |
| Desktop | [desktop/overview.md](desktop/overview.md) | `AtomPix.Desktop` | Avalonia / AtomUI UI、ViewModel、桌面交互和组合根。 |

Workflows 如何驱动 Core `ImageJob` / `BatchJob`，见 [Workflow 任务状态机编排设计](workflows/job-state-orchestration.md)。

Desktop 如何把产品能力落实为 AtomUI 组件、独立包和必要自定义控件，见 [AtomUI 组件映射与实现基线](desktop/atomui-component-mapping.md)。

Desktop 当前视觉结构和人肉验收原型见 [UI 设计入口](../ui-design/README.md)。

跨 Desktop、Workflows、Imaging.Magick 与 Infrastructure 的诊断关联、日志落盘和隐私边界，见 [诊断与本地日志设计](infrastructure/diagnostics-and-logging.md)。

## 2. 推荐阅读顺序

```text
1. docs/document-consistency.md
2. docs/product/mvp-scope.md
3. docs/architecture/overview.md
4. docs/modules/overview.md
5. docs/modules/core/overview.md
6. docs/modules/imaging-abstractions/overview.md
7. docs/modules/workflows/overview.md
8. docs/modules/workflows/job-state-orchestration.md
9. docs/modules/imaging-magick/overview.md
10. docs/modules/infrastructure/overview.md
11. docs/modules/infrastructure/diagnostics-and-logging.md
12. docs/modules/desktop/overview.md
13. docs/modules/desktop/interaction-state-design.md
14. docs/modules/desktop/atomui-component-mapping.md
15. docs/ui-design/README.md
```

## 3. 模块依赖摘要

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

AtomPix.Imaging.Abstractions
  -> AtomPix.Core

AtomPix.Infrastructure
  -> AtomPix.Core
```

## 4. 常见任务路由

| 任务 | 应修改模块 |
| --- | --- |
| 新增压缩策略、转换策略、任务状态和错误规则 | Core |
| 新增打开图片、生成预览、压缩、转换、批处理、保存设置等用户流程 | Workflows |
| 新增图片处理接口、请求/结果 DTO、格式枚举、预览数据结构 | Imaging Abstractions |
| 修改 Magick.NET 调用、格式映射、图片库异常转换 | Imaging Magick |
| 调整图片文件/像素硬边界或 Memory/Map/Disk/Thread 上限 | 先更新 Imaging Abstractions 能力与 Product 范围，再更新 Imaging Magick；Core 只增加稳定错误语义 |
| 修改配置文件保存、最近记录存储、路径解析、本地日志 Provider 或隐私过滤 | Infrastructure |
| 新增窗口、页面、ViewModel、AtomUI 控件、主题资源、拖拽交互 | Desktop |
| 调整模块引用关系或项目拆分 | Architecture 文档和对应模块文档 |
| 调整第一阶段功能范围、暂缓功能或验收口径 | Product 文档，优先更新 `docs/product/mvp-scope.md` |

## 5. 全局禁止事项

- 不得在 `Core` 中引用 Avalonia、AtomUI、Magick.NET、数据库、日志框架或具体配置文件实现。
- 不得在 ViewModel 中直接调用 Magick.NET 或 Infrastructure 具体存储类。
- 不得把 `ImageMagick` 类型作为 Workflows、Desktop 或 Abstractions 的公共 API。
- 不得在 Infrastructure 中编写压缩、转换、批处理等用户流程。
- 不得让 Workflows 直接依赖 `AtomPix.Imaging.Magick`。
- 不得为了方便把所有模块都塞进 `AtomPix.Desktop`。

## 6. 文档维护规则

- 新增模块时，必须新增对应模块文档，并更新本文件。
- 调整依赖关系时，必须同步更新 `docs/architecture/overview.md` 和本文件。
- 新增重要业务对象时，先判断是否属于 Core；如果属于 Core，先更新 Core 文档。
- 新增图片引擎能力时，先更新 Imaging Abstractions 文档，再实现具体引擎。
- 新增 UI 工作区或页面时，先更新 Desktop 文档或后续 UI 文档，再进入实现。
