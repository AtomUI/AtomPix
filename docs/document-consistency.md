# AtomPix 文档一致性规则

> 文档状态：维护基线
>
> 基线时间：2026-08-06

本文用于区分现行设计、目标契约、当前代码事实和历史验证记录，避免不同时间形成的文档被误读为同一层级的规范。

## 1. 权威顺序

同一问题出现冲突时，按以下顺序判断：

1. 产品范围与用户可见行为：`product/mvp-scope.md`。
2. 分层、依赖和模块边界：`architecture/overview.md` 与 `modules/**/overview.md`。
3. Workflow/Core 状态编排：`modules/workflows/job-state-orchestration.md`。
4. Desktop 控件状态与页面流转：`modules/desktop/interaction-state-design.md`。
5. 页面布局和可见控件：`ui-prototype/README.md` 与对应 SVG；README 中明确标为历史的原型不属于 MVP。
6. AtomUI 组件选型与 View 实现边界：`modules/desktop/atomui-component-mapping.md`；它不得反向改变前五项业务与交互规则。
7. 实施与测试文档：记录实现顺序、当前代码差距和历史验证，不得反向改变产品或模块契约。

同级正式文档之间仍应保持一致；该顺序只用于定位应修改哪一处，不能作为长期保留冲突的理由。

## 2. 三种时间语义

- “正式设计/目标契约”：已经协商冻结，但代码可能尚未实现。
- “当前代码/历史验证”：描述某个时间点仓库实际具备的能力，不构成目标设计。
- “后续备忘/历史参考”：不进入 MVP、公共 API、导航或验收范围。

文档描述尚未实现的正式契约时必须明确写出“目标设计，待实现”；描述旧代码行为时必须标明它是历史或迁移缺口，不能使用无时间限定的规范语气。

## 3. 当前范围摘要

- 四项同级单张功能：压缩、转换、调整尺寸、裁剪。
- 三项批量功能：压缩、转换、调整尺寸；没有批量裁剪。
- 首页打开图片和打开文件夹都进入浏览器；浏览集合不是批量输入。
- 批量页只有一个前台批次，可反复追加多选文件和多个文件夹。
- 压缩保持输入格式和像素尺寸；转换只改格式；Resize 不裁剪；Crop 只提取矩形区域。
- 压缩/转换不提供处理后效果预览、正式处理前体积估算或用户命名预设。

## 4. 模块边界摘要

```text
Core <- Imaging.Abstractions <- Imaging.Magick
Core <- Infrastructure
Core + Imaging.Abstractions <- Workflows
上述模块 <- Desktop 组合根
```

- Core 拥有业务状态与纯规则，不依赖外层。
- Workflows 创建和驱动 Core Job，负责编排、输出计划和恢复语义。
- Infrastructure 实现存储、文件系统和本地日志端口，不决定输出业务策略。
- Imaging.Magick 编码并安全提交文件，不创建业务输出目录。
- Desktop 拥有 UI 状态和框架适配，不直接迁移 Core Job，也不让 Avalonia 类型进入可复用 ViewModel 或内层公共契约。

## 5. 维护检查

每次范围或契约变更至少检查：

- `rg "BatchCrop|批量裁剪|四类.*批量" docs`
- `rg "ResizeApplied|SavedBytes|SavedRatio" docs`
- `rg "创建缺失目录|创建输出目录" docs`
- `rg "AtomUI.*Upload|ImagePreviewer" docs/modules/desktop docs/ui-prototype/README.md`
- `rg "AtomUI.Desktop.Controls.DataGrid|UseDesktopDataGrid|<[^>]*DataGrid" docs src tests`，命中只能是明确的禁止性说明，不能存在包引用、注册或控件用法。
- Markdown 相对链接和 `ui-prototype/*.svg` 文件名是否存在。

命中并不一定是错误；历史参考和明确的代码缺口可以保留，但必须有清楚的时间与规范层级标识。

## 6. 当前施工一致性快照

截至 2026-08-07，产品文档规定的四项单张功能、三项批量功能、浏览集合/批量输入分离、OutputPolicy、源文件保护和 Desktop 状态边界均已在生产组合根形成纵向链路。Desktop 交互闭环包括按需缩略图、浏览切换与缩放、首页拖放、逐功能能力禁用、五页可见输出策略、批量自动序号、最近记录重新定位、导航同步、透明背景预览和裁剪键盘微调。

当前证据为 Release `304 passed`（Desktop 状态/交互 `40`、Desktop UI 自动化 `6`、独立压力 `3`），以及 Windows x64 自包含 publish、5 秒启动烟测和真实发布进程 UIA 导航烟测。自动化已覆盖 10000 项虚拟化、16MP 并发预览、有界浏览缓存、稳定控件定位名称和输出故障注入；该快照仍不把未在本机执行的 macOS/Linux 原生流水线、多 DPI、物理磁盘耗尽和真实用户超大图片集验收写成已完成。当前版本不要求屏幕阅读器、UIA 动作模式或全页面纯键盘操作验收。
