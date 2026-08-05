# AtomPix 文档

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25
>
> 基线范围：当前文档定义的工程分层、模块边界、依赖方向、目录规划和第一阶段技术选型
>
> 变更规则：实现阶段如需调整模块职责、项目引用方向或关键技术选型，应先更新相关设计文档，再进入代码实现。

本目录维护 AtomPix 的架构设计、模块边界、工程约束和后续实现说明。

AtomPix 是一款基于 C#、Avalonia 和 AtomUI 的跨平台桌面图片工具，第一阶段目标是提供简易图片浏览、图片格式转换、图片压缩和批量处理能力，并为后续商业化能力预留清晰边界。

## 当前技术基线

| 领域 | 选择 |
| --- | --- |
| 桌面 UI | Avalonia + AtomUI |
| 图片处理 | Magick.NET |
| 架构组织 | 洋葱架构思想，依赖向核心收敛 |
| 用户流程层 | `AtomPix.Workflows` |
| 图片契约层 | `AtomPix.Imaging.Abstractions` |
| 发布策略 | 第一阶段优先 self-contained；NativeAOT 暂不作为强制标准 |

## 文档目录

| 目录 | 职责 |
| --- | --- |
| `product/` | 产品需求、版本范围、功能规划和验收口径。 |
| `architecture/` | 顶层架构、模块分层、依赖关系、项目目录规划和工程约束。 |
| `modules/` | 各模块职责、边界、允许内容、禁止内容和推荐目录。 |
| `implementation/` | 阶段性实现策略、测试策略、发布验证和工程执行说明。 |

## 阅读入口

推荐按以下顺序阅读：

```text
1. docs/product/mvp-scope.md
2. docs/product/feature-detailing-plan.md
3. docs/architecture/overview.md
4. docs/modules/overview.md
5. docs/modules/core/overview.md
6. docs/modules/workflows/overview.md
7. docs/modules/imaging-abstractions/overview.md
8. docs/modules/imaging-magick/overview.md
9. docs/modules/infrastructure/overview.md
10. docs/implementation/testing-and-release.md
11. docs/implementation/roadmap.md
12. docs/modules/desktop/overview.md
```

## 全局原则

- `Core` 是最内层业务核心，不依赖任何外层模块。
- `Workflows` 编排用户流程，不接触 UI 控件和具体图片库。
- `Imaging.Abstractions` 定义图片处理子系统契约，不依赖具体图片实现。
- `Imaging.Magick` 是 Magick.NET 的适配实现，不泄漏 `ImageMagick` 类型。
- `Infrastructure` 实现配置、授权、额度、文件系统、日志等外部能力。
- `Desktop` 是 Avalonia / AtomUI 启动项目和组合根，可以引用实现模块，但不能让 UI 类型向内层泄漏。
