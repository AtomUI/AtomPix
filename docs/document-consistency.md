# AtomPix 文档一致性规则

> 文档状态：维护基线
>
> 基线时间：2026-08-24

本文用于区分现行设计、目标契约、当前代码事实和历史验证记录，避免不同时间形成的文档被误读为同一层级的规范。

## 1. 权威顺序

同一问题出现冲突时，按以下顺序判断：

1. 产品范围与用户可见行为：`product/mvp-scope.md`。
2. 分层、依赖和模块边界：`architecture/overview.md` 与 `modules/**/overview.md`。
3. Workflow/Core 状态编排：`modules/workflows/job-state-orchestration.md`。
4. Desktop 控件状态与页面流转：`modules/desktop/interaction-state-design.md`。
5. Desktop 当前视觉结构：`ui-design/README.md`；对应 SVG 只有在索引标记为“已按当前基线重绘”后才具备视觉验收效力。它们不得反向改变前四项业务与交互规则。
6. AtomUI 组件选型与 View 实现边界：`modules/desktop/atomui-component-mapping.md`；ImageGallery 专项包治理与迁移边界另见 `modules/desktop/atomui-labs-imagegallery-migration.md`。它们不得反向改变前五项业务、交互与视觉规则。
7. 实施与测试文档：记录实现顺序、当前代码差距和历史验证，不得反向改变产品或模块契约。

旧 Desktop 视觉基线已经退役。当前视觉基线由 `ui-design/README.md` 与 2026-08-24 重绘完成的十张 SVG 共同表达；更早的颜色、尺寸、位置和页面构图均不再具备规范效力。

同级正式文档之间仍应保持一致；该顺序只用于定位应修改哪一处，不能作为长期保留冲突的理由。

## 2. 三种时间语义

- “正式设计/目标契约”：已经协商冻结，但代码可能尚未实现。
- “当前代码/历史验证”：描述某个时间点仓库实际具备的能力，不构成目标设计。
- “后续备忘/历史参考”：不进入 MVP、公共 API、导航或验收范围。

文档描述尚未实现的正式契约时必须明确写出“目标设计，待实现”；描述旧代码行为时必须标明它是历史或迁移缺口，不能使用无时间限定的规范语气。

## 3. 当前范围摘要

- 四项同级单张功能：压缩、转换、调整尺寸、裁剪。
- 三项批量功能：压缩、转换、调整尺寸；没有批量裁剪。
- 首页打开图片支持多选，打开图片和打开文件夹都进入浏览器；图片走廊同时是浏览集合与批量输入集合。
- 浏览器走廊只追加多选图片；三项批量功能直接处理走廊全部图片，不再提供独立批量任务导航。
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
- `rg "AtomUI.*Upload|ImagePreviewer" docs/modules/desktop`
- `rg "AtomPixImageGalleryViewer|AtomPixImageViewport|AtomUI.Labs.Controls.ImageGallery|UseImageGallery" docs src tests`，必须区分“当前迁移期实现”和“正式目标”；目标接入不得依赖 Labs internal 类型、私有模板或开发机绝对路径。
- `rg "沉浸式|Drawer|抽屉|IsBrowserBackdropVisible|IsToolDrawerOpen|未遮挡区域" docs`，命中只能是明确的旧代码事实、历史验证或禁止性说明；当前目标必须是独立标题栏以及 Browse/Operate 普通两列工作区。
- `rg "AtomUI.Desktop.Controls.DataGrid|UseDesktopDataGrid|<[^>]*DataGrid" docs src tests`，命中只能是明确的禁止性说明，不能存在包引用、注册或控件用法。
- Markdown 相对链接以及 `ui-design/*.svg` 文件名是否存在。

命中并不一定是错误；历史参考和明确的代码缺口可以保留，但必须有清楚的时间与规范层级标识。

## 6. 当前施工一致性快照

截至 2026-08-26，生产组合根已经贯通四项单张 Workflow、三项批量 Workflow、OutputPolicy、源文件保护和 Desktop 状态边界，并完成 ImageGallery 与新 Shell 迁移：独立标题栏、图标轨、浏览走廊统一输入集合、无工具时全宽浏览、有工具时左右并列工作区、Crop 安全工作区和双列连续滚动设置页均已落地。迁移复用了按需缩略图、浏览切换与缩放、逐功能能力禁用、输出策略、批量自动序号、透明背景预览和裁剪能力；宽文字导航、覆盖式 Drawer、独立批量来源列表和首页最近记录 UI 不再属于生产界面。

当前本机证据为 Debug 全解决方案 `343 passed`（Core `43`、Imaging Abstractions `18`、Infrastructure `37`、Workflows `105`、Desktop 状态/交互 `65`、Desktop Headless UI `17`、Magick `55`、独立压力 `3`），构建 `0` 警告/`0` 错误。自动化覆盖首页轻量多选、ImageGallery Headless 组合与压力、有界浏览缓存、增量图片追加、稳定控件定位名称、统一单张/批量 View、窗口级反馈、批量草稿原子提交、单张完成后批量命令即时恢复、Resize 整数显示、宽高比例实时联动、Crop 五项比例与自定义单列整数矩形、画廊切换后单张目标与尺寸草稿同步、同格式质量 JSON 往返、禁止放大的 Core/Workflow/Desktop 投影与新 AtomUI 表单、设置普通页切换/连续分区/纵向定位滚动/返回恢复、外层批量命令准备并等待真实批量执行、Shell 从文件夹浏览到压缩面板再到批量执行的完整组合链路、自定义输出目录、WebP 转换和输出故障注入。Windows 发布进程 UIA 脚本继续定位 Logo、五个图标轨动作与设置页面，不使用旧“七个导航项”或 Settings Dialog 口径。该快照仍不把未在本机执行的 macOS/Linux 原生流水线、多 DPI、物理磁盘耗尽和真实用户超大图片集验收写成已完成。当前版本不要求屏幕阅读器或全页面纯键盘操作验收。

ImageGallery 与新 Shell 迁移已完成：AtomPix 通过仓库内受校验的 `AtomUI.Labs.Controls.ImageGallery` nupkg 使用正式公开能力，并采用独立标题栏、无工具时全宽浏览、有工具时左右并列的普通布局。单张和批量不再切换 View；窗口级反馈使用 AtomUI Message、Notification 与 Dialog 的公开能力。十张 `ui-design/*.svg` 已按该基线重绘并通过本地实际渲染检查。
