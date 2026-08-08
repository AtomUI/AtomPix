# AtomPix.Desktop 模块设计

> 文档状态：正式实现基线，分阶段施工中
>
> 基线时间：2026-08-06

## 1. 模块定位

`AtomPix.Desktop` 是 Avalonia / AtomUI 桌面入口，也是生产组合根。

它负责窗口、页面、ViewModel、导航、资源、主题、文件选择器、拖拽、剪贴板、系统通知和依赖注入装配。

## 2. 允许包含

- Avalonia 启动入口和 App 配置。
- AtomUI 主题、资源和控件使用。
- Views、ViewModels、Commands。
- 页面导航、弹窗、消息提示。
- 文件选择器、拖拽、剪贴板、桌面交互。
- UI 资源，例如图标、图片、字体。
- DI 组合根，把接口映射到具体实现。

## 3. 禁止包含

- 直接调用 Magick.NET。
- 直接读写配置文件或最近记录文件。
- 在 ViewModel 中实现压缩、转换、批处理业务流程。
- 把 Avalonia `Bitmap`、控件类型或 UI 状态传入 Core、Workflows 或 Imaging Abstractions 的公共 API。
- 让内层模块反向依赖 Desktop。

## 4. 推荐目录

```text
src/AtomPix.Desktop/
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
```

## 5. 组合根职责

Desktop 可以引用所有生产实现模块，用于完成依赖注册：

```text
AtomPix.Desktop
  -> AtomPix.Workflows
  -> AtomPix.Core
  -> AtomPix.Imaging.Abstractions
  -> AtomPix.Imaging.Magick
  -> AtomPix.Infrastructure
```

典型注册关系：

```text
IImageProcessor -> MagickImageProcessor
IAppSettingsStore -> JsonAppSettingsStore
Logging abstractions -> Infrastructure local rolling provider
```

注册可以发生在 `Composition/` 或 `DependencyInjection/` 目录中。ViewModel 只依赖 Workflows 或抽象接口，不直接依赖具体实现。Desktop 启动时建立 SessionId、初始化本地日志和全局错误边界；日志初始化失败不得阻断应用启动。

## 6. UI 边界

Desktop 负责把用户输入转换为 Workflows 请求，也负责把 Workflows 返回结果转换成界面状态。

例如预览流程：

```text
ViewModel 调用 CreatePreviewWorkflow
Workflow 返回 ImagePreviewResult
Desktop 图片展示适配器将 EncodedBytes 转换为 Avalonia Bitmap
View 显示 Bitmap
```

Avalonia 类型只停留在 Desktop 的 View、框架适配器和组合根。可复用 ViewModel 状态保存预览负载或展示句柄抽象，不直接保存 Avalonia `Bitmap`。

## 7. 四大功能的信息架构目标

Desktop 主导航必须把以下四项作为同级、同权重入口：

```text
压缩
转换
调整尺寸（Resize）
裁剪
```

主导航保留压缩、转换、调整尺寸、裁剪四个同级单张功能；批量任务页只显示压缩、转换、调整尺寸三个标签，不显示批量裁剪。压缩和转换页面不得用内嵌参数替代独立的调整尺寸或裁剪流程；处理完成后可以提供跨功能快捷入口，但必须构造新的 Workflow 请求。

转换页使用 Probe 的 `HasTransparency` 驱动透明度 UI，不把 `HasAlphaChannel` 当作真实透明。透明输入选择 JPEG 时显示共享于请求的背景色色块、`#RRGGBB` 输入和白/黑快捷项；选择 PNG/WebP 时隐藏该控件并保留草稿值。Desktop 可以用 Avalonia 绘制背景预览，但最终铺底由 Workflow 调用图片处理器完成，成功文案必须取 `TransparencyProcessingResult`，不能由界面自行推断。

Metadata 设置在 UI 上统一表述为“移除拍摄信息与位置数据”：勾选映射 `MetadataPolicy.Remove`，未勾选映射 `Preserve`。两者互斥且每个请求只能选择一个；辅助文案必须说明 ICC 色彩配置不受开关控制，AutoOrient 后旧方向信息不会继续保留。

压缩页完整提供 Smart、HighQuality、Balanced、Maximum、Custom 五种模式。选择 Custom 时显示双向同步的 `1..100` 质量滑块与整数输入框；非法值使草稿无效。Smart 的内部质量候选不可编辑；批量 Custom 对所有有损项目共享一个质量值，设置页可以把 `Custom + Quality` 保存为默认压缩配置。无损输出不使用质量，结果中的实际质量只能来自 Workflow 返回值，Desktop 不按模式猜测。

四类单张结果统一消费 Core 的 `SizeChangeKind / SizeDeltaBytes / SizeDeltaRatio`。Desktop 使用差值绝对值配合“减少”“未变化”“增加”文案，不直接展示带符号的技术值；输出变大仍展示成功及输出路径。输入或输出大小缺失时隐藏体积变化，不伪造 `0 B`。批量总体变化直接投影 `BatchResult` 的成功可比较项统计；没有可比较项时显示“暂无可比较结果”，不得从可见行或计划输入重新求和。

AtomPix 不提供原地覆盖输入图片。Workflow 返回 `StartRejected(OutputPathConflictsWithInput)` 时，Desktop 显示阻断弹窗“无法覆盖原始图片”，只提供“改为自动重命名”和“返回修改”。前者只修改当前草稿的 `OverwritePolicy`、重新计算输出摘要并关闭弹窗，不自动重新提交；批量冲突文案显示冲突数量并整体阻断，不提供“仍然覆盖”。

Resize 页面只编辑 `ResizePolicy` 与输出策略，不展示输出格式、质量或元数据控件。输出格式固定为输入格式；提交时 Desktop 把当前公共 `SameFormatEncodingPolicy` 作为不可变快照写入 `ResizeImageRequest`，任务接受后不再受设置变化影响。

批量 Resize 同样只维护一套共享 `ResizePolicy`。共享百分比、单边约束或双边最大边界时，每张图片根据自己的逻辑原始尺寸显示不同的预计结果；关闭保持比例后才表示所有项目强制输出为相同 Width / Height，并必须显示变形警告。第一阶段不提供逐项 Resize 参数覆盖。

批量草稿提供“文件名格式”编辑器。默认在多项批次显示 `{name}_atompix_{index}`，并提供“原文件名 `{name}`”“序号 `{index}`”插入项和至少两个实际输出示例。用户格式缺少 `{index}` 且输入数量大于 1 时，Desktop 不阻止编辑，但必须提示系统会在末尾追加 `_{index}` 并显示实际生效格式；扩展名只读地来自任务输出格式。未知占位符、未闭合格式或非法文件名使草稿无效并禁用开始。

Desktop 的名称预览只用于及时反馈。提交时 Workflow 根据冻结输入顺序生成权威 `BatchOutputPlan`；运行页以只读快照显示实际格式、序号和每项最终 OutputPath。序号从 `001` 开始，失败、跳过或取消不使后续行改号。

裁剪页面由 Desktop 负责维护选框的可视状态、八个缩放控制点、比例锁定，以及 `Width / Height / Position X / Position Y` 的双向同步。单张比例只约束 UI 选框；最终提交给 `CropImageWorkflow` 的是 Core `CropRectangle`，不得把比例重复作为执行事实，也不得把 Avalonia 坐标或控件类型传入内层。Crop 输出保持输入格式，页面不提供质量或元数据控件；提交时把公共 `SameFormatEncodingPolicy` 作为不可变快照写入请求。

## 8. 批量输入交互边界

批量任务页使用“添加文件”和“添加文件夹”，两个入口都把新结果追加到当前输入列表：

- “添加文件”调用支持多选的系统文件选择器。
- “添加文件夹”第一阶段每次选择一个文件夹；用户可以重复选择其他文件夹。
- 文件、文件夹可以交替添加，来源不要求位于同一目录或磁盘。
- Desktop 把选择器返回的文件路径、文件夹路径和当前列表提交给 `AppendBatchInputsWorkflow`，再使用返回的完整 `BatchInputPlan` 更新列表。
- Desktop 不直接遍历目录、不自行判断支持格式，也不自行实现路径规范化或去重。
- 添加完成后以轻量通知展示新增、重复、不支持和不可读取数量；有跳过项时允许查看明细。
- 列表项目提供“移除”操作和完整路径 Tooltip；移除不触碰源文件。
- 空列表时禁用开始处理按钮。
- 输入列表增加或移除时，按当前顺序刷新文件名示例；正式任务提交后停止重新编号。

批量任务执行时，Desktop 向 Workflow 传入本次调用专属的 `IProgress<BatchExecutionProgress<TItemResult>>`：

- 先根据冻结输入顺序建立全部 Pending 行，再使用进度消息的 `Index` 更新单项状态。
- 只消费当前调用、BatchId 一致且 `Sequence` 严格递增的消息，避免迟到或乱序回调覆盖新状态。
- 汇总进度直接投影 Core `BatchProgressSnapshot`，不从可见行重新计算业务统计。
- 当前 Running 项显示不确定进度；第一阶段不伪造单张图片内部百分比。
- `ExecuteAsync` 返回的完整 `BatchResult` 是最终权威结果，用于校正所有终态行；终态之后忽略迟到进度。
- UI 回调或调度错误不能反向修改、取消或终止 Workflow/Core Job。

批量终态恢复不复活旧任务：

- Desktop 随已接受任务保存提交快照，包含原输入顺序、任务类型、处理参数、编码策略和输出策略。
- “重试失败项”只建立 Failed 输入的新草稿；“处理未完成项”建立 Failed、Canceled 与未开始输入的新草稿；成功和正常跳过项不进入默认恢复集合。
- 因目标存在而 Skipped 的行提供“使用自动重命名处理”，建立采用 AutoRename 的新草稿。`Skipped` 不是失败。
- 所有恢复动作先进入可编辑 Ready 草稿，不立即执行；再次点击开始才调用现有批量 Workflow。
- 新旧 JobId、进度和结果完全独立，不合并统计，也不增加 RetryWorkflow 或 Core Retrying 状态。
- 输入收集阶段的 `BatchInputPlan.SkippedItems` 没有创建 Job，不能显示为执行期 Skipped 行。

首页的“打开文件夹”入口只进入图片浏览页；它不调用批量输入 Workflow，也不自动跳转到批量任务。

## 9. 图片浏览器与当前图片操作

图片浏览器维护一个明确的 `CurrentImagePath`：

- 首页“打开图片”和“打开文件夹”都导航到图片浏览器。
- 打开文件夹时，缩略图列表是浏览集合，不投影为批量选择状态。
- 预览底部的压缩、转换、调整尺寸、裁剪入口只针对 `CurrentImagePath`。
- 点击入口时，ViewModel 立即复制当前路径并导航到相应单张页面；后续缩略图切换不得改变已经构造的请求。
- 当前图片为空、已从磁盘删除或探测失败时，禁用四项入口并展示可恢复原因。
- 图片浏览器不提供“添加图片”入口和文件夹内搜索框；更换浏览来源应返回首页重新打开图片或文件夹。
- 只有批量任务页维护批量输入列表，并调用 `AppendBatchInputsWorkflow`。
- 文件夹来源由 `OpenFolderWorkflow` 建立当前层级、自然排序的轻量候选集合；Desktop 不直接枚举目录或过滤格式。
- Desktop 从首个候选开始按需调用 `OpenImageWorkflow`，失败项保留为 `Unavailable` 并继续寻找首个可用项；空目录进入浏览器空态。
- 当前主预览优先于可见缩略图，二者复用 `CreatePreviewWorkflow`。缩略图有界并发、按可见区域延迟加载，当前项切换采用 latest-wins。
- 更换来源或离开页面时取消旧请求并切换集合代次；任何旧代次的晚返回结果都不得写回当前 ViewModel。
- 第一阶段只保留会话级预览缓存，不监听目录变化，也不建立持久化磁盘缩略图缓存。

这一区分必须反映在 ViewModel 类型中：浏览集合使用当前索引和 `CurrentImagePath`，批量输入使用 `BatchInputPlan`；二者不能复用同一个“已选择图片集合”状态。

## 10. Desktop 交互状态实现基线

Desktop 的逐页面状态、控件启用条件、加载与恢复行为，以及页面流转，以 [Desktop 交互状态设计](interaction-state-design.md) 为实现基线。

实现时遵守以下约束：

- 页面内容加载、参数草稿、预览生成和任务执行是四组正交状态，不能压缩为一个覆盖所有含义的 `Status` 枚举。
- `CanExecute`、`Visible`、`Loading` 和恢复动作由上述状态及 Workflow 投影结果派生，不在 ViewModel 中维护会相互矛盾的可写布尔值。
- 第一阶段一次只运行一个前台任务；运行期间锁定主导航、输入替换和参数编辑，只保留查看进度与取消。
- Workflow 通过“启动被拒绝、运行快照、任务终态”语义驱动 UI；Desktop 不直接根据 Core 状态写业务流转，也不直接修改 Core 模型。Job 创建与迁移顺序见 [Workflow 任务状态机编排设计](../workflows/job-state-orchestration.md)。
- 单张任务终态保留在当前页面；批量任务终态冻结任务快照；设置采用显式保存并保留未保存草稿。

## 11. 系统交互适配边界

文件选择、目录选择和调用系统文件管理器属于 Desktop 平台交互，不是图片处理业务。第一阶段使用 Avalonia 提供的跨平台系统 API，但 ViewModel 不直接持有 `Window` / `TopLevel`，也不直接调用 `StorageProvider` 或 `Launcher`。

Desktop 内部提供轻量接口并由组合根绑定 Avalonia 实现：

```text
IDesktopPickerService
  PickSingleImageAsync
  PickImagesAsync
  PickFolderAsync

IDesktopLauncherService
  OpenDirectoryAsync
```

接口和返回模型不得暴露 Avalonia 类型。选择成功返回用户选中的路径；用户取消是正常的无操作结果，不投影为 `Failure`，也不调用 Workflow；平台能力不可用或系统调用失败才显示 Desktop 轻量错误。

调用边界如下：

```text
首页打开图片      -> Picker -> OpenImageWorkflow
首页打开文件夹    -> Picker -> OpenFolderWorkflow
批量添加文件/目录 -> Picker -> AppendBatchInputsWorkflow
打开输出目录      -> Launcher（不进入 Workflow）
拖放文件/目录     -> View 提取路径 -> 复用对应 ViewModel 命令
```

Desktop 适配器只隔离窗口生命周期、框架类型和系统调用，不重新实现 Windows、macOS、Linux 三套选择器。保持该边界后，更换 UI 框架时主要重写 View、样式和平台适配器；Core、Workflows、Imaging 以及不依赖 Avalonia 类型的 ViewModel 状态逻辑不应被迫修改。当前不为此额外拆分工程项目。

## 12. AtomUI 组件实现基线

页面实现必须以 [AtomUI 组件映射与实现基线](atomui-component-mapping.md) 为组件选型依据。该文档已经把 01–13 原型逐页映射到本地 AtomUI 源码中存在的公开组件，并明确 ListView、ColorPicker、ImagePreviewer、Upload 及自定义图片控件的适用边界。

实现时遵守以下总则：

- 优先直接使用 AtomUI Stable 公开控件，其次组合 AtomUI 控件；只有图片视口、裁剪画布和薄平台桥接使用 Avalonia/AtomPix 自定义实现。
- `out-lib/AtomUI` 只用于源码核对，不作为生产 `ProjectReference`，不复制 private/internal 控件实现或模板。
- ViewModel 和内层模块不暴露 AtomUI/Avalonia 类型；组件事件先进入 Command，再由 ViewModel 决定是否调用 Workflow。
- 禁止引用或注册 `AtomUI.Desktop.Controls.DataGrid` 包。批量列表使用主桌面包中的虚拟化 `ListView`，以静态表头和五列行模板投影冻结输出计划。
- AtomUI Upload 不作为 AtomPix 批量输入真源；文件夹枚举、格式判断、规范化和去重继续由 Workflow 负责。
- AtomUI ImagePreviewer 只用于结果或大图查看；主图片工作区和裁剪画布使用 Desktop 自定义控件。

## 13. 当前实现快照

截至 2026-08-07，`AtomPix.Desktop` 的第一阶段纵向施工已经贯通。生产组合根、AtomUI 主题、单窗口 Shell、同级导航、首页、图片浏览器、压缩、转换、调整尺寸、单张裁剪、三类批量任务和设置页均使用正式 ViewModel 与 Workflow 契约，不再保留通用空页或模拟处理入口。

已落地的关键交互包括：框架无关 Picker / Launcher / Clipboard / Dialog 适配；首页单文件/单目录拖放薄桥接；Preview 字节到 Avalonia Bitmap 的薄投影；浏览器按容器延迟请求缩略图、前后切换、缩放、适应/1:1、不可用项移除和逐功能能力禁用；四类单张任务的草稿校验、取消、导航锁、终态和恢复动作；五类任务页面可见且可校验的 OutputPolicy 编辑与冻结提交；转换透明背景色及对应主预览；CropCanvas 像素矩形和键盘微调；批量多来源追加、冻结输出计划、实时进度防乱序、结果校正及失败/跳过/未完成项恢复；显式保存设置、离开时 Save / Discard / Stay；最近文件/目录 Drawer 和失效记录重新定位；Shell 路由与 AtomUI NavMenu 选中同步；应用关闭时前台任务确认取消；本地诊断编号与 Desktop 全局错误边界。批量任务只包含压缩、转换和调整尺寸，不包含裁剪；工程和测试均禁止 AtomUI DataGrid。

Desktop 无窗口状态测试当前为 `40 passed`，Desktop UI 自动化为 `6 passed`（含 1 项压力测试），整套解决方案 Release 基线为 `304 passed`。浏览器三类有界 LRU 缓存、结果文件激活复核、10000 项 ListView 虚拟化、生产控件稳定自动化名称和 Windows 发布进程 UIA 导航均已有自动门禁。仍需作为外部发布验收而非页面功能缺口处理的项目是：CI 上 macOS/Linux 原生运行结果、真实多 DPI、物理磁盘耗尽和真实用户超大图片集验收。当前版本明确不要求屏幕阅读器、UIA `InvokePattern/SelectionItemPattern` 或全页面纯键盘巡检，AtomUI `NavMenuNode` 不提供动作模式不再列为 AtomPix 缺口。Magick.NET 已升级到 `14.16.0`，当前整套解决方案的 NuGet 直接及传递依赖审计未发现已知漏洞；后续依赖变更仍必须重新审计。
