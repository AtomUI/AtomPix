# AtomPix Desktop 的 AtomUI 组件映射与实现基线

> 文档状态：正式目标设计，ImageGallery 与新 Shell 布局待实现
>
> 基线时间：2026-08-23

本文把 AtomPix 的 Desktop 页面能力、交互状态设计与 AtomUI/AtomUI.Labs 的公开组件对齐，作为 Desktop 页面实现时的组件选型基线。ImageGallery 的过渡期本地包治理与专项迁移细则见 [`atomui-labs-imagegallery-migration.md`](atomui-labs-imagegallery-migration.md)。

本文只冻结组件职责和组合边界，不冻结 AtomUI 私有模板部件、内部类型或某个页面的最终像素值。页面业务、状态与恢复动作仍分别以产品范围和 Desktop 交互状态设计为准。

## 1. 核对基线

本次核对的本地 AtomUI 源码快照声明：

| 项目 | 本地源码值 | AtomPix 约束 |
| --- | --- | --- |
| AtomUI | `6.0.8` | ImageGallery `6.0.8` 与 AtomUI `6.1.3` 存在二进制契约不兼容；当前所有 AtomUI Desktop/ColorPicker/Font/Icon 包统一固定为 `6.0.8`，禁止混装。 |
| 当前生产包基线 | `6.0.8` | 工程直接消费公开包和仓库内固定哈希的 Labs nupkg，不引用本地源码。后续必须让 AtomUI 与 Labs 同步升级并重新执行第 11 节。 |
| Avalonia | `12.1.1` | Desktop 与 Headless 测试统一固定为 `12.1.1`；它满足 ImageGallery 声明的 `>= 12.0.5`，不得产生重复或降级资产。 |
| .NET SDK | `10.0.300` | 与 AtomPix 当前 `net10.0` 目标一致。 |
| 主桌面组件包 | `AtomUI.Desktop.Controls` | MVP 必需。 |
| 颜色选择包 | `AtomUI.Desktop.Controls.ColorPicker` | MVP 必需，用于 JPEG 透明区域铺底色。 |
| 图片画廊目标包 | `AtomUI.Labs.Controls.ImageGallery 6.0.8` | 正式目标设计，待迁移；过渡期从已核对 nupkg 复制到 AtomPix 仓库内本地 NuGet 源，不引用外部源码项目。 |
| DataGrid 包 | `AtomUI.Desktop.Controls.DataGrid` | 明确禁止引用、注册或复制其实现；批量明细使用主桌面包中的 `ListView`。 |

`out-lib/AtomUI` 是不会提交的源码核对副本，不得成为 `AtomPix.Desktop.csproj` 的相对 `ProjectReference`，也不得从中复制内部控件源码或私有模板。AtomUI 正式组件继续通过公共包源消费；AtomUI.Labs ImageGallery 在正式发布前只允许使用仓库内、固定版本且校验哈希的本地 nupkg。两者都不得形成跨仓库 ProjectReference。

当前 `AtomPix.Desktop.csproj` 已固定引用上述 AtomUI `6.0.8` 包和 `AtomUI.Labs.Controls.ImageGallery 6.0.8`；ImageGallery 包由仓库内本地源、独立 restore cache 和 SHA-256 门禁管理。工程未引用 DataGrid 包，并通过 MSBuild 目标在发现该直接包引用时立即失败；Desktop 自动化测试另行检查生成程序集不引用 DataGrid 程序集。

## 2. 组件优先级

本节是 Desktop 视觉实现的强制选型门禁，不是组件建议清单。每个可见或可交互组件都必须先核对 AtomUI 的公开稳定能力；代码评审发现未记录例外的自绘通用控件时，视为不符合设计，不能合入。Desktop 实现按以下顺序选择视觉能力：

1. 直接使用 AtomUI 的 Stable 公开控件和公开 API。
2. 用多个 AtomUI 控件组合成 AtomPix 专用 `UserControl`，但不复制 AtomUI 模板。
3. 仅在 AtomUI 组件的产品语义不匹配时使用 Avalonia 原生布局、绘制、输入或平台 API。
4. 只有图片视口、裁剪选框等领域特有交互才新增 AtomPix 自定义 `Control`。

选择第 3 或第 4 项时，必须在本文对应组件映射中记录：AtomUI 缺失或不匹配的能力、不能直接组合的原因、最终使用的公开 Avalonia/AtomPix 边界和验收项。不得用“实现方便”“像素更好调”或“暂时先自绘”绕过 AtomUI。即便宿主是 AtomPix 自定义 Control，内部通用按钮、表单、弹窗、反馈、滚动和图标仍优先组合 AtomUI 组件。

“优先使用 AtomUI”不表示把所有能力强行解释为现有组件：

- AtomUI 控件只负责视图交互和视觉状态；Workflow/Core 仍是业务状态真源。
- ViewModel 不持有 AtomUI/Avalonia 控件、`Window`、`Bitmap`、`Color`、StorageItem 或事件参数。
- 不使用 AtomUI internal 类型、Template Part 或伪类作为 AtomPix 业务契约。
- AtomUI 控件的 `SelectedItem`、`Value`、`IsOpen` 等状态只能绑定或投影 ViewModel 状态，不能形成第二份业务事实。
- 通用颜色、圆角、间距、阴影、字体和 Motion 先映射 AtomUI Token，再由 AtomPix Theme 提供稳定语义名称；页面 AXAML 不得复制同义常量或私有主题资源。

## 3. 工程包与启动注册

目标 Desktop 组合根使用 AtomUI 的公开启动链路：

```text
Program/AppBuilder
  -> UseAtomUIPlatformDetect
  -> WithAtomUIDefaultOptions

Application.Initialize
  -> UseAtomUI
       -> WithDefaultCultureInfo
       -> WithDefaultTheme / WithInitialTheme
       -> UseAlibabaSansFont
       -> UseDesktopControls
       -> UseDesktopColorPicker
       -> UseImageGallery
```

约束如下：

- `UseDesktopControls()` 注册公共和桌面控件主题；ColorPicker 与目标 ImageGallery 分别通过公开 `UseDesktopColorPicker()`、`UseImageGallery()` 入口追加。禁止调用 `UseDesktopDataGrid()`。
- 主题必须在首个窗口创建前完成注册。首帧主题使用 AtomUI 的初始主题配置，不在窗口显示后补刷一套颜色。
- AtomPix 不自行扫描 AtomUI/AtomUI.Labs 主题资源，不手工引用私有 AXAML，也不依赖任何 Gallery 示例/文档宿主项目。目标只消费 `AtomUI.Labs.Controls.ImageGallery` 包和公开主题注册入口。
- Ant Design 图标使用 AtomUI 图标包的公开 Provider。已核对可用于主导航的图标包括 `PictureOutlined`、`CompressOutlined`、`SwapOutlined`、`ColumnWidthOutlined`、`ScissorOutlined`、`FolderOpenOutlined`、`SettingOutlined`。
- 字体以 AtomUI 注册字体和平台中文 fallback 为准，不下载网络字体；最终字号、行高和对比度通过主题 Token 与视觉验收确定。

## 4. 全局组件映射

| AtomPix 语义 | 首选 AtomUI 组件 | 使用规则 |
| --- | --- | --- |
| 主窗口与标题栏 | `Window`、`WindowTitleBar` | 复用 AtomUI 窗口、主题和平台装饰能力，不按操作系统复制三套标题栏。标题栏是独立浅色 Surface，使用主题默认标题/Caption/Icon 前景与弱底部分隔线；Home、Browser、Crop 和批量内容都从其下方开始，不做客户区沉浸延伸，也不根据图片明暗动态改写 Caption 样式。 |
| 贴左图标轨 | `Button` / `IconButton`、`Tooltip`、Avalonia `Border`/`StackPanel` | 不使用宽 `NavMenu`。固定顺序为 Logo、Compress、Convert、Resize、Crop、Settings；对应中文语义为“压缩体积、转换格式、调整尺寸、剪裁尺寸”。容器高度按内容测量并在主内容区垂直居中，只设置右上/右下圆角。视觉只显示图标，命令、Tooltip 和自动化名称提供语义。 |
| Shell 主区与右侧处理面板 | Avalonia `Grid` / `ContentPresenter` / `Border`，正文使用 AtomUI `ScrollViewer` | `ActiveTool=None` 时只建立一列并让 ImageGallery 占满内容区；ActiveTool 非空时建立“`*` + 约 `380 px`”两列，右列承载当前工具内容。右列使用不透明 Surface 与左侧弱分隔线，从内容区顶部铺至底部；不使用 Drawer、Popup、遮罩、悬浮圆角或位移动画，也不改变顶层窗口尺寸。 |
| 页面滚动 | AtomUI `ScrollViewer` | 参数表单可以独立滚动；图片视口不因其他区域滚动而改变缩放。 |
| 水平/垂直间距 | `Space`，必要时 Avalonia Panel | 规则操作区优先使用 `Space`；复杂自适应网格使用 `Grid`。 |
| 分组容器 | `Card`、`Separator`、`GroupBox` | 参数组、结果摘要和警告摘要优先组合现有容器。 |
| 主次操作 | `Button`、`IconButton`、必要时 `DropdownButton` | `CanExecute` 驱动禁用；运行时不靠遮罩吞掉仍可点击的按钮。 |
| 少量互斥选项 | `Segmented` | 格式、批量任务类型、Pixel/百分比等 2–5 项切换；不是页面路由真源。 |
| 单选配置 | `RadioButton` / `OptionButtonGroup` | 输出冲突策略、裁剪比例等需要完整标签或换行的互斥配置。 |
| 布尔配置 | `CheckBox`，必要时 `Switch` | “保持比例”“移除拍摄信息”等表单值用 CheckBox；立即生效的全局视觉开关才考虑 Switch。 |
| 文本输入 | `LineEdit` | 文件名格式、HEX 颜色和只读路径；校验走绑定错误与 `Status`，不另存 `HasError`。 |
| 数值输入 | `NumericUpDown` | Width、Height、X、Y、百分比和质量，设置明确范围与整数步进。 |
| 连续质量值 | `Slider` + `NumericUpDown` | 两个控件双向绑定同一个质量字段，范围 `1..100`。 |
| 枚举/长选项 | `Select` | 输出目录策略、覆盖策略等不适合铺开的选项。 |
| 颜色输入 | `ColorPicker` + `LineEdit` + 快捷 `Button` | ColorPicker 禁用 Alpha；View 适配器在 Avalonia `Color`、HEX 与 Core `RgbColor` 间转换。 |
| 表单布局与错误 | `Form`、`FormItem` | 参数标签、必填、帮助文字和校验反馈由 Form 体系承载；业务草稿仍在 ViewModel。 |
| 简单集合 | `ListView` | 缩略图集合和错误明细；使用模板和容器回收，不让数据项引用视觉对象。 |
| 批量异常/恢复明细 | 虚拟化 `ListView` | 图片走廊承担主要逐项进度；右侧面板只在需要解释失败、跳过或恢复目标时使用紧凑明细，保持冻结输入顺序，不恢复旧五列表格或第二份输入集合。 |
| 任务与结果详情 | `Descriptions`、`Statistic` | 在当前工具的右侧处理面板或结果区展示提交参数、输出尺寸、格式和结果统计；浏览态不建立常驻右侧信息面板，也不从显示值反算业务结果。 |
| 状态标签 | `Tag`；计数提示使用 `CountBadge` / `DotBadge` | Pending/Running/Succeeded/Failed/Skipped/Canceled 使用统一语义颜色和文字，颜色不是唯一信息。 |
| 总进度 | `ProgressBar` | 只投影 Workflow 提供的可计算批量总进度；隐藏控件自带百分比文案，由相邻“已完成 N/M”承担精确数值表达。单张内部进度未知时不伪造百分比。 |
| 不确定等待 | `Spin` | 当前单张执行、当前批量行和短暂系统调用使用不确定反馈。 |
| 骨架加载 | `Skeleton` | 浏览器主图、缩略图和其他异步内容首次加载；旧代次晚返回不能替换新内容。 |
| 空内容 | `Empty` | 空文件夹、空批量草稿和其他无内容状态；提供与状态匹配的唯一下一步。 |
| 页面终态 | `Result` 或结果 `Card` | 仅在终态占据主要页面区域时使用 Result；编辑页内结果优先保留在同页 Card。 |
| 字段与草稿错误 | `WindowMessageManager` / `Message` | 当前内容 Ready 时允许用户发起提交尝试；参数、输出目录和命名草稿在提交边界统一校验，错误由 MainWindow 顶部中央 Message 明确反馈。处理面板底部不再重复渲染红色校验文本。 |
| 内联结果/提醒 | `Alert` | 成功、部分成功、资源限制和需要保留上下文的恢复说明；恢复按钮绑定现有命令。 |
| 设置与阻断确认 | 设置使用 `Grid` / `ScrollViewer` 普通页面；阻断确认使用 `Dialog` / `MessageBox` | 设置不再使用 Dialog；源文件覆盖、批量取消和恢复默认仍使用明确确认。设置返回时静默撤销草稿，不叠加确认。所有真正的 Dialog 必须指定 owner。 |
| 短暂通知 | `WindowMessageManager` / `Message` | 挂载 MainWindow，固定顶部中央；单张结果、添加输入统计、保存成功等非阻断反馈使用官方自动关闭。 |
| 系统级通知卡 | `WindowNotificationManager` / `Notification` | 挂载 MainWindow 右上角；批量终态使用标题、摘要、关闭按钮与点击回调，全部成功自动关闭，异常终态常驻。 |
| 辅助说明 | `Tooltip` | 完整路径、禁用原因、图标含义；关键错误不得只放 Tooltip。 |
| 结果大图查看 | `ImagePreviewer` | 用于成功结果或浏览图片的弹层查看、缩放和切换，不作为主编辑画布。 |

## 5. 页面能力与组件映射

| 页面/状态 | 主要 AtomUI 组件 | AtomPix 补位与边界 |
| --- | --- | --- |
| 首页 / 空态 | `Card`、`Button`、`Empty`、`Skeleton`、Ant Design 图标 | “打开图片”调用多选 Picker，“打开文件夹”调用单目录 Picker；首页不再重复展示四项快捷卡，图标轨工具在选择成功后进入 Browser 并展开对应面板。 |
| 图片浏览器 | AtomUI.Labs `ImageGallery`，恢复反馈组合 AtomUI `Spin`、`Empty`、`Alert` | `ImageGallery` 负责当前左侧工作区 Bounds 内的主图、缩放/平移、顶部工具栏、虚拟化底部画廊、追加入口、导航、解码和 Lease。浏览态占满内容区，操作态随左列收窄；AtomPix 通过 Desktop-only adapter 绑定业务集合与 CurrentItem，并通过公开 Appearance 对齐视觉。Crop 以 `ResourceOnly` + `TryAcquireCurrentImage` 复用主图；ActiveBatchIndex 跟随与缩略图六态插槽仍为后期 TODO。 |
| 压缩处理面板 | `Form`、`Segmented`、`Slider`、`NumericUpDown`、`CheckBox`、`Select`、`Button`、`Alert` | 五种模式绑定同一压缩草稿；一张时只有单张按钮，多张时增加批量全部按钮；主预览继续由 Browser 持有。 |
| 转换处理面板 | `Form`、`Segmented`、`Slider`、`NumericUpDown`、`ColorPicker`、`LineEdit`、`CheckBox`、`Select`、`Button` | 透明背景色规则不变；单张与批量共享当前面板草稿但构造不同 Request。Color 只在 View 层适配。 |
| Resize 处理面板 | `Form`、`Segmented`、`NumericUpDown`、`CheckBox`、快捷 `Button`、`Descriptions` | Pixel/百分比共享 `ResizePolicy` 草稿；单张显示当前预计尺寸，批量按每项原图投影预计值。 |
| Crop 处理面板 | `Form`、`NumericUpDown`、`OptionButtonGroup`/`Segmented`、`Button`、`Tooltip`、`Descriptions` | Crop 打开后左列切换为 `AtomPixCropCanvas` 编辑工作区，右列显示 Crop 参数。统一 SafeArea 在左列中把完整原图以 Contain 放入前景编辑层；主图左右边缘翻页热区从命中树失效，画廊仍可切换 CurrentItem；只显示单张执行按钮。 |
| 集成式批量状态 | `ProgressBar`、`Tag`、`Spin`、`Alert`、必要时 `ListView` | 当前版本的总进度、当前处理项、摘要与必要的结果明细位于工具面板，并由 BatchResult 校正；缩略图逐项状态与活动项自动跟随属于后期 TODO。 |
| 设置页面 | `Grid`、`ScrollViewer`、`Segmented`、`Slider`、`NumericUpDown`、`CheckBox`、`ColorPicker`、`Button`、`Message` | Dirty/Saving/SaveFailed 来自设置 ViewModel。页面平铺在 Shell 主工作区，左列是带独立背景与选中态的“图标 + 文案”分区导航，右列是包含全部分区的单一连续 ScrollViewer；点击导航执行短距离纵向滚动，手动滚动反向同步选中态，不替换内容树。Footer 固定为“恢复默认 / 返回 / 保存设置”。返回时静默撤销未保存草稿，保存反馈由主窗口顶部中央 Message 承担。设置快照在主窗口首帧后低优先级预加载；预加载与提前进入共享单次加载。 |
| 错误与边界 | `Alert`、`Result`、`Dialog`/`MessageBox`、`Button`、`Tag`、`Descriptions` | 错误码决定严重程度和恢复动作；`OutputPathConflictsWithInput` 使用阻断 Dialog，取消使用中性反馈。 |
| 公共页面状态 | `Skeleton`、`Spin`、`Empty`、`Alert`、`Result`、`Dialog`、`Tag`、`Message` | 反馈组件只投影公共 Desktop 状态；Processing 锁定参数，终态留在当前业务上下文，恢复动作创建新草稿而不是改写历史任务。 |

## 6. 必要的 AtomPix 自定义视图与适配能力

| 自定义能力 | 基础 | 为什么不能直接替换为 AtomUI 现有控件 | 状态所有权 |
| --- | --- | --- | --- |
| `ImageGalleryItemAdapter` | 公开 `IImageGalleryItem` / `IImageGallerySource` | Labs 数据契约属于 Desktop；薄适配把 Browser item 映射为稳定规范化路径 Key、完整文件名和文件 Source，`ThumbnailImageSource=null` 让组件按 Purpose 复用主 Source。 | ImageGallery 拥有加载、解码、lease、缓存和组件代次；AtomPix ViewModel 拥有业务集合、CurrentItem、任务状态和恢复动作。 |
| Browser 左列 Overlay/SafeArea 协调能力 | Avalonia `Panel`/`Grid`、实际 Bounds 与布局失效通知 | ImageGallery 工具栏、图标轨和浮动画廊形成左列内部排除区，不能让各控件复制固定 Margin 或设计图坐标；独立标题栏与右侧面板由 Shell Grid 天然排除。 | Shell/View 是唯一布局事实源，按左列当前 Bounds 计算 Crop 工作区并传给 CropCanvas；ViewModel、Core 和 Workflow 不持有 Avalonia Rect。 |
| `AtomPixCropCanvas` | Avalonia `Control` 绘制 + ImageGallery 外部 Lease | AtomUI 没有图片内遮罩、八点缩放、边界约束和像素矩形同步控件。Gallery 以 `ResourceOnly` 继续加载当前资源；View 独占外部 Lease，Canvas 仅借用 `IImage`，不重复解码也不释放图片。 | ViewModel 拥有像素 CropRectangle/比例意图；View 拥有 Lease；Control 只投影完整原图、可视几何、逆变换和交互手势。 |
| `AtomPixInputDropZone` | Avalonia DragDrop + AtomUI Card/Icon/TextBlock 视觉 | AtomUI `Upload` 会拥有 `Files`、准入、目录展开和上传队列；这与 `AppendBatchInputsWorkflow` 的目录枚举、去重和 `BatchInputPlan` 真源冲突。 | View 只提取文件或目录路径并调用命令，不遍历目录、不判断格式、不去重。 |
| `OutputPolicyEditorView` | AtomUI `Segmented`、`TextBox`、`Button` 的共享组合 | 五类任务页必须编辑同一语义的输出位置、命名与冲突策略；复制五套控件会造成校验和文案漂移。 | 框架无关 `OutputPolicyEditorViewModel` 构造 Core `OutputPolicy`；位置、命名和选择器错误在所属编辑器内部就近呈现。 |
| `BrowserProcessingStatusPresenter`（TODO，后期迭代） | 未来通过 ImageGallery 公开缩略图扩展点组合 AtomUI `Spin`/`Tooltip` | 当前迁移不实现缩略图六态；批量总体和逐项异常先在右侧面板表达。未来实现仍不得访问 internal 缩略图类型或复制组件模板。 | ViewModel 的单一 `BatchItemVisualStatus?` 投影仍是未来状态真源；TODO 不得催生当前版本的第二套画廊。 |
| `AtomPixColorAdapter` | Converter/Behavior | AtomUI ColorPicker 使用 Avalonia `Color?`，Core 使用 `RgbColor`。 | ViewModel/Core 值是提交真源；ColorPicker 只是双向编辑投影。 |

这些类型都留在 `AtomPix.Desktop`。除纯 ViewModel 状态外，不为了“可复用”把 Avalonia 或 AtomUI 类型抽到 Core、Workflows 或 Imaging。

## 7. 明确不采用或受限采用的 AtomUI 组件

| 组件 | 结论 | 原因 |
| --- | --- | --- |
| `Upload` / `UploadTrigger` / `UploadDropZone` | MVP 批量输入不采用 | 它们会建立独立 `Files` 状态、执行准入和目录展开；AtomPix 要求目录枚举、支持格式判断、规范化去重由 Workflow 完成。普通 Button + Picker 及薄 DragDrop Bridge 更符合边界。 |
| `ImagePreviewer` | 只用于可选的结果/大图弹层查看 | 不承载裁剪矩形、编辑草稿、主页面的 CurrentImagePath 或内嵌浏览器布局；不得访问其 internal `ImageViewer`。 |
| `SearchEdit` | 图片浏览器不采用 | 产品已经明确浏览器没有搜索框。 |
| `NavMenu` | 不作为新 Shell 主导航 | 当前目标是只显示图标的贴左窄轨；用公开 Button/Icon/Tooltip 组合，不复制 NavMenu 模板或用隐藏文字强行压窄。 |
| `Drawer` / `Popup` / 自制窗口级浮层 | 不承载四类处理配置或设置 | 四类处理配置是 Shell 普通 Grid 的右侧布局列；设置是 Shell 普通内容页。不得用 Drawer、Popup、随机高 ZIndex、遮罩或复制私有模板恢复覆盖式面板。 |
| `AtomUI.Desktop.Controls.DataGrid` 整个包 | 禁止使用 | 不添加 PackageReference、不调用 `UseDesktopDataGrid()`、不使用其命名空间，也不复制其源码实现。 |
| `Notification` | 批量终态摘要 | 只承担窗口级、可关闭、可点击的批量结果，不承担运行进度、字段校验或模态确认。 |
| AtomUI Gallery 示例/文档宿主 | 不引用 | 示例项目不是生产组件依赖；这条不包括已明确采用的独立包 `AtomUI.Labs.Controls.ImageGallery`。 |

## 8. 状态到反馈组件的统一映射

| Desktop 状态 | 视觉组件 | 规则 |
| --- | --- | --- |
| `Empty` | `Empty` | 提供与页面语义匹配的打开/添加入口。 |
| `Loading` | `Skeleton` 或 `Spin` | 内容骨架用 Skeleton；短时动作和不可预知等待用 Spin。 |
| `Ready` | Form、ListView、Button 等常态组件 | 控件启用完全由派生 `CanExecute` 和编辑锁控制。 |
| `Processing` | `Spin`；批量另有 `ProgressBar` | 单张不显示伪百分比；批量总进度使用已完成项数量。 |
| `Success` | 同页结果 `Card`、`Tag`，必要时 `Result` | 即使输出体积增加也保持 Success，并显示实际结果。 |
| `Partial` | Warning `Alert` + ListView 终态行 | 已成功输出保留，恢复动作只建立新草稿。 |
| `Failure` | 提交校验与普通运行错误使用窗口级 Message/Notification；阻断条件用 Dialog | 草稿无效不得被命令 `CanExecute` 静默吞掉；用户点击后必须得到顶部中央反馈。重要或可操作错误不能只依赖自动消失消息。 |
| `Canceled` | 中性 `Alert`/`Tag` | 不使用异常红色，不删除已经成功提交的输出。 |

AtomUI `ProgressBar`、`Spin`、`Alert`、`Result` 只投影 Desktop 状态，不直接修改 Core Job。AtomUI 控件事件先进入 ViewModel Command；Command 再决定是否调用 Workflow。

## 9. 主题集成与界面质量约束

- 当前正式视觉结构以 `../../ui-design/README.md` 和 2026-08-24 重绘完成的 SVG 为准。宽文字侧栏退役，标题栏独立，Shell 使用贴左约 `50 px` 图标轨以及 Browse/Operate 两种工作区布局。
- 窗口运行时默认尺寸为 `1180 × 760 px`、最小尺寸为 `960 × 640 px`。浏览态不建立右列；操作态使用 ImageGallery/Crop 左列与约 `380 px` 右侧处理面板。右列从内容区顶部铺至底部、正文独立滚动，不覆盖图片、不使用抽屉动效，也不改变窗口尺寸。

- 工具编辑器先同步进入 Loading，再提交 ActiveTool 与两列布局并绑定右侧内容，最后异步等待设置或 Gallery 资源；右列首帧不等待资源。Compress、Convert、Resize 不创建面板私有预览；CropCanvas 通过 Gallery 当前资源 Lease 复用同一解码结果。此边界避免同一输入在点击工具时被重复解码/编码，并避免右侧面板延迟出现。
- 图标轨与其内部 AtomUI `Button` 均不绘制边框；轨道投影使用 AtomPix Theme 的语义化 BoxShadow 资源。导航轨、画廊共享 `AtomPixFloatingSurfaceBrush`，导航按钮与画廊追加/上一张/下一张共享 `AtomPixFloatingIconButton` 公共 Style：默认、悬停和按下状态保持透明背景、透明边框和同一前景色。当前工具状态由按钮后方独立的选中指示层表达，不覆盖 AtomUI 私有模板。Logo 与处理区之间保留一条分组线，设置上方不再增加分割线。
- 浏览态 ImageGallery 占满标题栏下方内容区；操作态只占可伸缩左列。浮动画廊不占用主图布局空间，最大宽 `900 px`、高 `68 px`、距 ImageGallery 底部 `8 px`，始终按控件自身 Bounds 居中并响应式收窄；浏览态不出现旧信息面板、底部白栏或空白预留列。
- 现有 `src/AtomPix.Desktop/Assets/Branding` 下 Logo、PNG 派生图和 ICO 全部继续使用；Ant Design 图标继续通过 AtomUI 公开 Provider 获取，不重绘、不复制为新的生产资产。
- 应用主题必须在首个窗口创建前通过 AtomUI/Avalonia 的公开主题注册入口加载；不得修改 AtomUI 源码，不得访问 internal Token、私有模板或 Gallery 资源。
- 新视觉中的共享颜色、圆角、字级和间距必须形成语义化的 AtomPix Theme/Token，页面只消费资源，不散落重复常量；主题实现仍应建立在 AtomUI 正式公开扩展能力上。
- 成功、警告、失败、信息和禁用态使用 AtomUI 语义 Token；所有状态同时提供文字或图标，不能只靠颜色。
- Light、Dark 和 FollowSystem 使用 AtomUI ThemeManager；首轮至少验证 Light 与 FollowSystem，Dark 不允许因硬编码画刷而出现不可读文本。
- 动效遵循 AtomUI `IsMotionEnabled` 和全局 motion 配置。批量行频繁更新不得触发无业务意义的重复动效。
- Tooltip、Dialog、Message、Notification 使用 AtomUI 既有 overlay/feedback layer，不手写随机高 ZIndex。
- 裁剪选框属于图片视口局部装饰，使用局部 Adorner/Canvas，不放进窗口级 `WindowFeedbackLayer`。
- 文本缩放、本地化长度和高 DPI 下不得用固定像素裁掉按钮文字；组件在新布局中必须保持可访问、可伸缩和可测试。

## 10. 推荐实现顺序

```text
1. Desktop 工程、AtomUI 包、主题和 Window/Shell
2. 独立标题栏、贴左图标轨与 Browse/Operate 两种工作区状态
3. 反馈组件、Form 适配和 Picker/Launcher 桥接
4. 按专项迁移设计接入 AtomUI.Labs ImageGallery、本地包门禁与 Desktop adapter
5. 首页多选与图片浏览器追加入口
6. 压缩、转换、Resize 的右侧处理面板
7. Browser 内 AtomPixCropCanvas 与 Crop 面板
8. 图片走廊批量状态和面板实时进度投影
9. 设置普通内容页
10. 状态与 UI 自动化验收
```

截至 2026-08-11，生产代码已经完成上一轮视觉迁移：宽 NavMenu、独立单张页面和独立批量输入页已退出 Shell；贴左图标轨、沉浸式 Home/Browser、AtomUI 官方右侧 Drawer、浏览集合批量语义、缩略图任务状态、活动项自动跟随和 AtomUI 设置 Dialog 已进入生产组合根。旧 ViewModel/Workflow 能力被保留并重新组合，生产 UI 不再包含五列表格或第二份批量来源列表。

2026-08-24 已完成该技术重构：图片浏览器生产实现改用 AtomUI.Labs `ImageGallery`，Shell 改为独立标题栏与 Browse/Operate 普通两列布局；本地包门禁、公开 API 接入、Crop Lease Bridge 和旧控件清理均已进入代码。第 52 节以前的数字仍只是历史快照，不能代表本次迁移后的最新验证结果。

右侧处理面板不再依赖 AtomUI `Drawer.Title` 或 Drawer 模板。Shell 使用公开 Grid/ContentPresenter 承载“工具名称 + 集合数量 + 关闭按钮”，其中通用按钮、表单、滚动和反馈仍优先组合 AtomUI 公开组件；不得为了沿用旧实现复制 Drawer 私有 Header 或 Frame。

共享编辑器壳体只复用布局、预览、输出策略和结果区域，不能把四类 Request 合并成一个包含大量可空字段的通用请求。

## 11. 验收与升级检查

AtomUI 组件集成至少覆盖：

- 应用启动时 AtomUI 主包、ColorPicker 与目标 ImageGallery 主题均在首窗前成功注册；首帧无缺失资源，且工程没有 DataGrid 包引用或注册。
- 图标轨顺序、ActiveTool 和运行期导航锁一致；同一工具再次点击收起面板，其他工具原位切换内容，重复操作不创建第二份草稿或页面实例。
- Slider 与 NumericUpDown 绑定同一质量值，不产生循环或短暂非法值。
- ColorPicker、HEX 文本、黑白快捷项与 Core `RgbColor` 往返一致，Alpha 永远不会进入请求。
- ImageGallery 本地 nupkg 的文件名、版本、源提交和 SHA-256 与专项迁移设计一致；干净缓存和 CI 三平台 restore 不读取开发机 `D:\work\c#\AtomUI.Labs`，AtomUI 全家桶为 `6.0.8` 且不存在重复 AtomUI/Avalonia 资产。
- 图片走廊使用 ImageGallery 的虚拟化能力；较大集合滚动和容器回收后，复用容器不得串用选择、命令或缩略图。批量逐项状态与活动项跟随的容器回收要求随对应后期 TODO 一并验收。
- ImageGallery 不设置推动 Window 的固有尺寸；浏览态横纵拉伸到标题栏下方内容区，操作态横纵拉伸到 Shell 左列。Fit/ActualSize/Custom 只改变控件内部视口，任何图片 Extent 都绝不能扩大顶层窗口、右侧列或阻止窗口缩回 MinWidth/MinHeight 以上尺寸。
- **TODO（后期迭代）**：ImageGallery 通过正式公开 API 接收与 `CurrentItem` 分离的只读 `ActiveBatchIndex`/跟随请求。合法 Running 项进入可视区时，以最短距离动画真实横向滚动；用户滚动暂停和 Reduced Motion 规则保持不变。当前迁移不要求此 API，且不得依赖 Labs internal 成员、Template Part 或复制私有模板补做。
- **TODO（后期迭代）**：`BrowserProcessingStatusPresenter` 的外层固定为 `20 × 20 DIP` 圆形，缩略图右上内缩 `2 DIP`，带 `2 DIP` 白色 keyline，内部图形包围盒 `12 DIP`。Pending/Running/Succeeded/Failed/Skipped/Canceled 分别使用时钟/旋转圆弧/对勾/叉号/感叹号/横线；默认语义色为 `#AEB7C2/#1677FF/#52C41A/#FF4D4F/#FAAD14/#8C8C8C`，但生产模板必须引用 AtomPix Token，不能写页面局部常量。
- **TODO（后期迭代）**：Running 内部圆弧使用 `800 ms` 线性顺时针循环，状态图形使用 `120 ms` Ease-out 交叉淡化；Reduced Motion 立即替换。Presenter 位于选中蒙版与底部品牌条之上，随缩略图滚动，`IsTabStop=false` 且不执行命令。容器 Detach/Recycle 必须停止动画并清除旧伪类/Tooltip，Attach 后从绑定状态恢复；不得访问 AtomUI internal 成员或为每个 Pending 项创建持续动画。
- Crop 模式派生自 `ActiveTool=Crop`：Gallery 切到 `ResourceOnly`，默认主图视觉与命中退出但逻辑资源、选择和走廊保留；下层 CropCanvas 在统一 SafeArea 中借用 expected item 对应 Lease 的 `IImage` 并 Contain 完整原图。不得重复解码、使用平台相关背景模糊或把设计稿固定坐标写入控件。
- ImageGallery 的主图翻页按钮在所有模式下均关闭，也不叠加透明翻页热区；只有走廊固定按钮和缩略图可以切换 CurrentItem。Crop 模式下主图 Pointer 完整交给 CropCanvas。
- Shell 在浏览态/操作态/设置态切换时始终保持 `1180 × 760 / 960 × 640 px` 默认/最小尺寸；操作态约 `380 px` 右列参与普通布局，ImageGallery 按剩余左列 Bounds 重新 Fit 与响应式排版。右列不覆盖主图、不保留上下浮动空隙、不播放抽屉式 Motion；设置页不改变窗口尺寸和 ActiveTool。
- 批量逐项状态以虚拟化图片走廊为主，右侧处理面板只显示共享参数、总体进度、终态摘要和恢复动作；不得恢复旧五列表格或第二份批量输入列表。
- `ImagePreviewer` 的 Sources 替换或页面 detach 后释放过期图片资源；仅关闭弹层时允许按控件契约保留仍需显示的封面。主视口切换代次后释放旧 Bitmap，晚返回结果不能覆盖当前图片。
- CropCanvas 在窗口缩放、DPI、缩放和平移后仍提交正确的整数像素矩形，控制点不越过图像边界。
- Alert/Dialog/Message 的选型符合第 8 节，Dialog 始终具备 owner、键盘关闭策略和焦点恢复。
- Light/Dark/FollowSystem、125%/150%/200% DPI、键盘导航和简体中文长文案通过页面级快照或集成验收。
- AtomUI 升级后重新核对公开 API、主题注册、ListView 容器回收、ColorPicker Value 绑定和 ImagePreviewer 生命周期；不得通过引用 internal 成员规避破坏性变化。

## 12. 与其他设计文档的关系

- 产品范围与可见行为：`../../product/mvp-scope.md`。
- Desktop 职责与系统适配：`overview.md`。
- 控件启用、Loading 和恢复动作：`interaction-state-design.md`。
- 当前视觉结构与 SVG：`../../ui-design/README.md`。
- AtomUI.Labs ImageGallery 过渡包、适配与迁移门禁：`atomui-labs-imagegallery-migration.md`。
- Workflow/Core 状态编排：`../workflows/job-state-orchestration.md`。

当前 SVG 与文字设计共同冻结目标结构和首轮尺寸。若后续统一调整 `50/380/1180 px` 等数值，应先同步修改 UI 设计入口、本文和交互状态设计，再修改设计图与 AXAML。

若 AtomUI 组件的默认行为与这些正式业务规则冲突，应在 Desktop View/Adapter 中限制组件行为或改用更薄的 Avalonia 实现，不能反向修改业务规则来迁就组件。
