# AtomPix.Desktop 模块设计

> 文档状态：正式实现基线，ImageGallery 与新 Shell 布局待施工
>
> 基线时间：2026-08-23

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

当前浏览器呈现流程：

```text
OpenImageWorkflow 只执行 Probe 与业务能力检查
Desktop ImageGalleryItemAdapter 提供稳定 Key 与文件 Source
AtomUI.Labs ImageGallery 负责按视口解码、缩略图、缓存、缩放和平移
Crop 模式由 View 安全取得当前资源 Lease，CropCanvas 只借用 IImage
```

Avalonia/AtomUI.Labs 类型只停留在 Desktop。Core、Workflow 和处理请求不接触 Gallery 或 `IImage`；Browser ViewModel 不保存预览字节或 `Bitmap`，Crop 的 Lease 所有权只在 View 生命周期内存在。

## 7. 新 Shell 与四大功能的信息架构

Desktop 主工作区采用“独立标题栏 + 浏览/操作双状态 + 贴左图标轨”：

- 所有视觉 UI 组件执行 AtomUI 强制优先门禁：先使用公开稳定控件和公开组合方式，再考虑 Avalonia 原生能力。图片浏览器目标使用 AtomUI.Labs `ImageGallery`；只有 CropCanvas、Shell SafeArea 协调和薄平台/数据适配等尚无公共控件的领域能力可以建立 AtomPix 自定义实现。任何例外都必须在组件映射文档中记录原因和验收，禁止无记录地自绘 Button、表单、Drawer、Dialog、进度反馈或复制 AtomUI/AtomUI.Labs 私有模板。

- 默认背景是首页；打开来源后切换为图片浏览器。
- Home、Browser、Crop 和批量状态都使用独立标题栏：标题栏采用稳定浅色 Surface、主题默认前景与弱底部分隔线，页面背景和图片从标题栏下方开始。不得让图片延伸到标题栏下层，也不再维护 Browser 顶部深色渐变、亮色 Caption 或 `IsBrowserBackdropVisible` 页面切换状态。
- 原有宽文字侧栏取消。贴左图标轨宽约 `54 px`，固定悬浮在主背景上，只显示 Logo 与图标，不显示菜单文字。轨道使用约 `94%` 不透明白色浮层 Token、不绘制边框并使用统一的轻量投影；所有图标按钮保持无边框透明外观，鼠标悬停和按下不改变颜色，设置上方不绘制分割线。
- 图标从上到下固定为 Logo、压缩体积、转换格式、调整尺寸、剪裁尺寸、设置。Logo 返回首页；四项处理图标同级、同权重；设置切换到 Shell 普通内容页。轨道只在右上角和右下角使用圆角，高度包裹全部图标后在主内容区垂直居中；进入设置页时暂时隐藏，返回后恢复。图标通过 Tooltip 与自动化名称提供文字语义。
- 窗口运行时默认尺寸为 `1180 × 760 px`、最小尺寸为 `960 × 640 px`；浏览态与操作态切换都不改变窗口尺寸。设计 SVG 的 `1280 × 820 px` 只是高保真评审画布，不作为运行时初始窗口尺寸。
- 打开图片后默认进入浏览态：ImageGallery 占满标题栏下方内容区，`ActiveTool=None`，不存在也不预留右侧空白面板。选择四类工具后进入操作态：内容区使用普通 Grid 两列，左列为可伸缩 ImageGallery/Crop 工作区，右列为约 `380 px` 的处理面板。
- 右侧处理面板使用不透明 Surface 与左侧弱分隔线，从内容区顶部铺至底部并独立滚动。它不使用 Drawer、Popup、遮罩、悬浮圆角、投影或从最右边界向左滑入的 Motion。ImageGallery 按左列真实 Bounds 重新计算 Fit、工具栏与画廊响应式布局；窗口不扩宽，也不保留一份被面板覆盖的全宽主图。

图标轨中的四项处理能力为：

```text
压缩
转换
调整尺寸（Resize）
裁剪
```

压缩、转换和调整尺寸同时支持当前图片与走廊全部图片；裁剪只支持当前图片。四项参数继续使用彼此独立的 Request/Profile，不得因为共享 Shell 和右侧面板而合并成大量可空字段的通用请求。处理完成后的跨功能入口仍必须构造新的 Workflow 请求。

转换处理面板使用 Probe 的 `HasTransparency` 驱动透明度 UI，不把 `HasAlphaChannel` 当作真实透明。透明输入选择 JPEG 时显示共享于请求的背景色色块、`#RRGGBB` 输入和白/黑快捷项；选择 PNG/WebP 时隐藏该控件并保留草稿值。Desktop 可以用 Avalonia 绘制背景预览，但最终铺底由 Workflow 调用图片处理器完成，成功文案必须取 `TransparencyProcessingResult`，不能由界面自行推断。

Metadata 设置在 UI 上统一表述为“移除拍摄信息与位置数据”：勾选映射 `MetadataPolicy.Remove`，未勾选映射 `Preserve`。两者互斥且每个请求只能选择一个；辅助文案必须说明 ICC 色彩配置不受开关控制，AutoOrient 后旧方向信息不会继续保留。

压缩处理面板完整提供 Smart、HighQuality、Balanced、Maximum、Custom 五种模式。选择 Custom 时显示双向同步的 `1..100` 质量滑块与整数输入框；非法值使草稿无效。Smart 的内部质量候选不可编辑；批量 Custom 对所有有损项目共享一个质量值，设置页面可以把 `Custom + Quality` 保存为默认压缩配置。无损输出不使用质量，结果中的实际质量只能来自 Workflow 返回值，Desktop 不按模式猜测。

四类单张结果统一消费 Core 的 `SizeChangeKind / SizeDeltaBytes / SizeDeltaRatio`。Desktop 使用差值绝对值配合“减少”“未变化”“增加”文案，不直接展示带符号的技术值；输出变大仍展示成功及输出路径。输入或输出大小缺失时隐藏体积变化，不伪造 `0 B`。批量总体变化直接投影 `BatchResult` 的成功可比较项统计；没有可比较项时显示“暂无可比较结果”，不得从可见行或计划输入重新求和。

AtomPix 不提供原地覆盖输入图片。Workflow 返回 `StartRejected(OutputPathConflictsWithInput)` 时，Desktop 显示阻断弹窗“无法覆盖原始图片”，只提供“改为自动重命名”和“返回修改”。前者只修改当前草稿的 `OverwritePolicy`、重新计算输出摘要并关闭弹窗，不自动重新提交；批量冲突文案显示冲突数量并整体阻断，不提供“仍然覆盖”。

Resize 页面只编辑 `ResizePolicy` 与输出策略，不展示输出格式、质量或元数据控件。输出格式固定为输入格式；提交时 Desktop 把当前公共 `SameFormatEncodingPolicy` 作为不可变快照写入 `ResizeImageRequest`，任务接受后不再受设置变化影响。

批量 Resize 同样只维护一套共享 `ResizePolicy`。共享百分比、单边约束或双边最大边界时，每张图片根据自己的逻辑原始尺寸显示不同的预计结果；关闭保持比例后才表示所有项目强制输出为相同 Width / Height，并必须显示变形警告。第一阶段不提供逐项 Resize 参数覆盖。

压缩、转换、Resize、Crop 与批量草稿共用同一个输出策略编辑器。“文件命名”先用 AtomUI `Segmented` 明确选择“保留原文件名 / 添加后缀 / 自定义格式”，下方只呈现当前模式需要的上下文区域，并实时显示不含扩展名的名称示例。默认选择“添加后缀”，后缀为 `_atompix`；只有“自定义格式”展示 `{name}`、`{index}` 插入项。三种选择必须分别构造 Core 的 `KeepOriginalName`、`AppendSuffix`、`CustomPattern` 契约，不得全部降格为自定义模板。

批量任务基于上述基础命名策略派生稳定序号。实际格式缺少 `{index}` 且输入数量大于 1 时，Desktop 不阻止编辑，但必须提示系统会在末尾追加 `_{index}` 并显示实际生效格式；扩展名只读地来自任务输出格式。未知占位符、未闭合格式或非法文件名使草稿无效；用户提交时拒绝启动并由窗口顶部中央 Message 反馈，不能通过命令禁用静默拒绝。

Desktop 的名称预览只用于及时反馈。提交时 Workflow 根据冻结输入顺序生成权威 `BatchOutputPlan`；运行页以只读快照显示实际格式、序号和每项最终 OutputPath。序号从 `001` 开始，失败、跳过或取消不使后续行改号。

裁剪页面由 Desktop 负责维护选框的可视状态、八个缩放控制点、比例锁定，以及 `Width / Height / Position X / Position Y` 的双向同步。单张比例只约束 UI 选框；最终提交给 `CropImageWorkflow` 的是 Core `CropRectangle`，不得把比例重复作为执行事实，也不得把 Avalonia 坐标或控件类型传入内层。Crop 输出保持输入格式，页面不提供质量或元数据控件；提交时把公共 `SameFormatEncodingPolicy` 作为不可变快照写入请求。

## 8. 浏览集合与批量处理边界

图片走廊是唯一的浏览集合和批量处理输入集合，不再存在独立批量任务页：

- 首页“打开图片”调用支持多选的系统选择器；选择结果建立新集合。首页“打开文件夹”仍由 `OpenFolderWorkflow` 建立当前层级、自然排序的轻量候选集合。
- 走廊最左侧固定提供“追加图片”入口，调用多选文件选择器。它只追加图片，不追加文件夹；需要更换目录时点击 Logo 返回首页。
- 主图区域不显示上一张/下一张按钮，也不提供不可见的左右边缘点击热区；它只负责查看、缩放和平移。图片切换统一使用底部走廊的固定上一张/下一张按钮或直接点击缩略图。浏览态主图覆盖 ImageGallery 的完整 Bounds；Browser 不再保留右侧图片信息列，处理右列只在 `ActiveTool != None` 时参与布局。
- 走廊固定布局为“追加图片、上一张、可横向滚动缩略图、下一张”。三个控制项不参与滚动；所有导航入口共享 CurrentItem，并在切换后把当前缩略图滚入可见范围。缩略图保持直角，当前项淡白蒙版和选择指示也使用 `CornerRadius=0`，不得以圆角蒙版遗漏图片四角。
- 走廊外层使用半透明中性炭黑表面、`1 DIP` 半透明白色细边界和近白色操作图标。炭黑表面用于保证走廊在浅色主图上可辨认，浅色细边界用于保证走廊在深色主图上仍有轮廓；透明度只编码在背景 Brush 中，不通过父容器整体 `Opacity` 降低缩略图、文字和图标的不透明度。走廊颜色必须来自 AtomPix Theme Token，不在页面中散落硬编码颜色。
- 主图视口覆盖 ImageGallery 当前 Bounds，图片默认按视口完整适配并允许左右或上下留白；图片走廊作为距 ImageGallery 底部 `8 px` 的浮动画廊覆盖在主图视口上，不再占用固定底部布局行或绘制全宽底栏。画廊最大宽 `900 px`、总高 `68 DIP`，默认在左侧工作区内水平居中；四角使用统一浮层圆角，内部使用 `2 DIP` 等值内边距。追加、上一张和下一张共享无边框透明按钮外观，悬停不变色；缩略图保持直角，当前项使用淡白蒙版与浅色选择指示，滚动轨道透明。

- 用户点击压缩、转换、Resize 或 Crop 后，编辑器必须先同步进入 Loading 并立即提交 `ActiveTool` 与操作态两列布局，再等待设置或 Gallery 图片资源；不得把右列首帧放在图片解码之后。压缩、转换和 Resize 直接使用 Browser 已有 Gallery。Crop 切换为 `ResourceOnly` 并借用同一当前资源 Lease，不再调用 `CreatePreviewWorkflow` 或进行第二次完整解码。
- Browser 与 Crop 使用两套明确的呈现几何：普通浏览为 ImageGallery 当前 Bounds 内 `Fit / Contain / Uniform` 完整显示；Crop 在左列安全工作区内以 `Contain / Uniform` 显示完整原图编辑层。Crop 模式下 Gallery 的默认主图 Presenter 通过 `ResourceOnly` 停止绘制与命中，但 Gallery 根布局、选择、资源和走廊保留；前景 `AtomPixCropCanvas` 必须让自动方向校正后原图全部边缘可达。
- Shell/Desktop 只维护一份左列 Overlay/SafeArea 布局事实。贴左图标轨宽度、底部浮动画廊高度及其边缘间距来自共享 AtomPix Layout Token，统一推导 CropCanvas 的安全 Margin；标题栏和右侧面板由 Shell Grid 天然排除。Avalonia 每次 `Measure / Arrange` 都以当前左列尺寸和 DPI 重新分配 CropCanvas Bounds，CropCanvas 只负责在该 Bounds 内执行 `Contain` 和像素映射；禁止定时器、窗口绝对坐标、手写 Width/Height、读取 ImageGallery 私有模板部件或复制多套避让公式。
- Crop 模式同样只允许通过画廊固定上一张/下一张或缩略图切换 CurrentItem；主图区域不存在翻页按钮或透明热区，Pointer 全部交给 CropCanvas。切图保留比例意图并为新图重建合法选区。
- Crop 只需要前景编辑层的一份已解码预览资源，整个工作台固定使用 Home 浅灰 Surface（当前为 `#F5F7FA`），原图使用浅灰细边界，深色遮罩只存在于图片内部的非选区部分；不重复完整解码，也不引入背景模糊。Desktop 最终只把前景视口映射得到的原图像素 `CropRectangle` 提交给 Workflow；Core/Workflow 不感知 Fit、SafeArea、浮层或 Avalonia 坐标。
- Desktop 把当前集合与本次选择交给 Workflow 做规范化、支持性判断和去重；追加成功项保持选择器顺序并放在集合末尾，旧项目顺序不变。
- 追加过程是独立 `Appending` 状态；期间禁用重复追加和正式任务开始，但保留当前预览与已有集合。用户取消选择器时保持原集合且不显示错误。
- 重复、不支持和不可读的追加项目不进入集合；Desktop 用轻量通知展示 Workflow 返回的新增与跳过计数。文件夹候选后续探测失败时仍可作为 `Unavailable` 项保留并移出。
- CurrentItem 只决定主预览和单张处理目标；集合顺序是批量输出序号、进度 Index 和最终结果顺序的真源。
- 集合至少两张且当前工具为压缩、转换或调整尺寸时显示批量按钮；点击后把集合投影为冻结 `BatchInputPlan`。该提交直接使用当前可见工具面板的完整参数与 `OutputPolicy`，不得再次加载默认设置或以默认设置读取状态作为启动门槛。Crop 不显示批量入口。
- 提交后锁定追加、移除、重排、当前项切换和参数编辑。当前版本由右侧面板显示总体进度、当前处理项与结果；缩略图逐项状态和批量活动项自动跟随属于后期 TODO。

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

## 9. 图片浏览器与当前图片操作

图片浏览器维护一个明确的 `CurrentImagePath`：

- 首页多选图片和打开文件夹都导航到图片浏览器；选择成功后建立同一类型的会话集合。
- 图片浏览器中的压缩、转换、调整尺寸、裁剪入口把 Shell 从全宽浏览态切换为左右操作态，不替换浏览会话。点击其他工具原位切换右侧内容；再次点击当前工具或面板关闭按钮移除右列并让 ImageGallery 恢复全宽。顶层窗口宽度始终不变。
- 没有图片时点击四项图标，先打开多选图片选择器；成功后进入浏览器并展开目标面板，取消则保持首页。
- 单张开始按钮捕获 `CurrentImagePath`；批量开始按钮捕获当前完整集合。后续缩略图切换不得改变已经提交的请求。
- 当前图片为空、已从磁盘删除或探测失败时，禁用四项入口并展示可恢复原因。
- 图片浏览器不提供搜索框。走廊只提供追加图片；更换文件夹来源应点击 Logo 返回首页。
- 文件夹来源由 `OpenFolderWorkflow` 建立当前层级、自然排序的轻量候选集合；Desktop 不直接枚举目录或过滤格式。
- Desktop 从首个候选开始按需调用 `OpenImageWorkflow`，失败项保留为 `Unavailable` 并继续寻找首个可用项；空目录进入浏览器空态。
- 当前生产 Browser 的显示解码、虚拟化调度、Lease、预取和内存缓存由 AtomUI.Labs `ImageGallery` 负责；Workflow 继续拥有文件夹发现、Probe、能力与处理规则。Desktop 不保留第二套 Browser Preview/Thumbnail LRU。
- 更换来源或离开页面时，业务 Probe 请求由 ViewModel 取消并使用 latest-wins 代次；Gallery 的迟到加载、容器回收和资源释放由组件自己的生命周期代次负责。
- 第一阶段只使用 ImageGallery 运行期内存缓存，不监听目录变化，也不建立持久化磁盘缩略图缓存。

Desktop 只维护一份会话集合，但仍区分“可变浏览集合”和“已提交批量快照”：前者拥有当前索引、`CurrentImagePath` 和追加/移除能力，后者在点击批量开始时冻结为 `BatchInputPlan`，运行期间不可被浏览状态回写。

## 10. Desktop 交互状态实现基线

Desktop 的逐页面状态、控件启用条件、加载与恢复行为，以及页面流转，以 [Desktop 交互状态设计](interaction-state-design.md) 为实现基线。

实现时遵守以下约束：

- 页面内容加载、参数草稿、预览生成和任务执行是四组正交状态，不能压缩为一个覆盖所有含义的 `Status` 枚举。
- `CanExecute`、`Visible`、`Loading` 和恢复动作由上述状态及 Workflow 投影结果派生，不在 ViewModel 中维护会相互矛盾的可写布尔值。
- 第一阶段一次只运行一个前台任务；运行期间锁定图标轨、图片追加/移除、当前项切换和参数编辑，只保留查看进度与取消。
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
窗口级结果反馈    -> Message / Notification（不进入 Workflow）
拖放文件/目录     -> View 提取路径 -> 复用对应 ViewModel 命令
```

Desktop 适配器只隔离窗口生命周期、框架类型和系统调用，不重新实现 Windows、macOS、Linux 三套选择器。保持该边界后，更换 UI 框架时主要重写 View、样式和平台适配器；Core、Workflows、Imaging 以及不依赖 Avalonia 类型的 ViewModel 状态逻辑不应被迫修改。当前不为此额外拆分工程项目。

## 12. AtomUI 组件实现基线

页面实现必须以 [AtomUI 组件映射与实现基线](atomui-component-mapping.md) 为组件选型依据。图片浏览器迁移另外遵守 [AtomUI.Labs ImageGallery 接入与迁移设计](atomui-labs-imagegallery-migration.md)。两份文档共同明确正式 AtomUI 包、过渡期 Labs 本地 nupkg、组件公开 API 和 AtomPix 自定义适配边界；它们不反向改变产品行为。

实现时遵守以下总则：

- 优先直接使用 AtomUI/AtomUI.Labs 公开控件，其次组合公开控件；只有裁剪画布、SafeArea 协调和薄平台/数据桥接使用 Avalonia/AtomPix 自定义实现。
- `out-lib/AtomUI` 只用于源码核对，不作为生产 `ProjectReference`。AtomUI.Labs ImageGallery 正式发布前使用仓库内固定版本、固定哈希的 nupkg；两者都不得形成跨仓库源码引用，也不复制 private/internal 实现或模板。
- ViewModel 和内层模块不暴露 AtomUI/Avalonia 类型；组件事件先进入 Command，再由 ViewModel 决定是否调用 Workflow。
- 禁止引用或注册 `AtomUI.Desktop.Controls.DataGrid` 包。批量逐项状态主要投影在虚拟化图片走廊；右侧面板需要明细时使用主桌面包中的 `ListView`，不得恢复 DataGrid。
- AtomUI Upload 不作为 AtomPix 批量输入真源；文件夹枚举、格式判断、规范化和去重继续由 Workflow 负责。
- AtomUI ImagePreviewer 只用于结果或大图弹层查看；主图片工作区目标使用 AtomUI.Labs ImageGallery，裁剪画布继续使用 `AtomPixCropCanvas`。

## 13. 当前实现快照

截至 2026-08-11，`AtomPix.Desktop` 已完成上一轮 Shell 的生产重构：宽文字导航、独立单张页面和独立批量输入页面已经退出组合根；Home/Browser 沉浸背景、贴左图标轨、右侧处理 Drawer、浏览集合批量语义、Crop 安全工作区、缩略图进度和设置 Dialog 已接入原有 Workflow 能力。2026-08-23 冻结的新正式目标改为独立标题栏与 Browse/Operate 两列工作区，该视觉布局尚待实现。

当前落地能力包括 Picker / Launcher / Clipboard / Dialog 适配、预览字节投影、按需缩略图、latest-wins、四类参数草稿、CropCanvas、OutputPolicy、批量进度与终态恢复、设置显式保存/关闭撤销和诊断边界。图标轨工具只组合这些能力，不重新实现底层业务；生产代码禁止重新引入 NavMenu 宽侧栏和独立批量来源页。

2026-08-11 本地 Release 门禁为 Desktop 状态/交互 `50 passed`、Desktop UI 自动化 `12 passed`、整套解决方案 `320 passed`，构建 `0` 警告/`0` 错误。首页轻量多选、浏览器三类有界 LRU 缓存、结果文件激活复核、10000 项 ListView 虚拟化、生产控件稳定自动化名称、图标轨/Drawer/Dialog 渲染和 Windows 发布进程 UIA 定位均有自动门禁。仍需作为外部发布验收而非页面功能缺口处理的项目是：CI 上 macOS/Linux 原生运行结果、真实多 DPI、物理磁盘耗尽和真实用户超大图片集验收。当前版本明确不要求屏幕阅读器或全页面纯键盘巡检。Magick.NET 已升级到 `14.16.0`，后续依赖变更仍必须重新执行漏洞审计。

2026-08-24 已完成 Browser 的 AtomUI.Labs `ImageGallery 6.0.8` 生产迁移：供应链哈希门禁、普通 Browse/Operate 布局、Desktop adapter、Crop `ResourceOnly`/Lease Bridge 和旧控件/重复缓存清理均已进入代码。上述 2026-08-11 数字仅是历史快照；本次迁移后的精确验证数字以 `testing-and-release.md` 最新记录为准。
