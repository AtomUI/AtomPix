# AtomPix Desktop 的 AtomUI 组件映射与实现基线

> 文档状态：正式目标设计，分阶段实现中
>
> 基线时间：2026-08-06

本文把 AtomPix 的 01–13 UI 原型、Desktop 交互状态设计与本地 `out-lib/AtomUI` 源码中的公开组件对齐，作为 Desktop 页面实现时的组件选型基线。

本文只冻结组件职责和组合边界，不冻结 AtomUI 私有模板部件、内部类型或某个页面的最终像素值。页面业务、状态与恢复动作仍分别以产品范围和 Desktop 交互状态设计为准。

## 1. 核对基线

本次核对的本地 AtomUI 源码快照声明：

| 项目 | 本地源码值 | AtomPix 约束 |
| --- | --- | --- |
| AtomUI | `6.1.3` | 实现时在中央包版本文件中固定经过验证的明确版本，不使用浮动版本。 |
| 当前生产包基线 | `6.1.2` | NuGet 正式源在本轮施工时尚未提供 `6.1.3`；工程固定使用可恢复的 `6.1.2`，并以本地 `6.1.3` 源码复核公开 API。发布版升级到 `6.1.3+` 前必须重新执行第 11 节。 |
| Avalonia | `12.1.0` | Desktop 必须使用与 AtomUI 兼容的 Avalonia 版本。 |
| .NET SDK | `10.0.300` | 与 AtomPix 当前 `net10.0` 目标一致。 |
| 主桌面组件包 | `AtomUI.Desktop.Controls` | MVP 必需。 |
| 颜色选择包 | `AtomUI.Desktop.Controls.ColorPicker` | MVP 必需，用于 JPEG 透明区域铺底色。 |
| DataGrid 包 | `AtomUI.Desktop.Controls.DataGrid` | 明确禁止引用、注册或复制其实现；批量明细使用主桌面包中的 `ListView`。 |

`out-lib/AtomUI` 是不会提交的源码核对副本，不得成为 `AtomPix.Desktop.csproj` 的相对 `ProjectReference`，也不得从中复制内部控件源码或私有模板。正式工程通过受版本管理的包依赖 AtomUI；升级版本时重新执行本文第 11 节的组件验收。

当前 `AtomPix.Desktop.csproj` 已固定引用 `AtomUI.Desktop.Controls`、`AtomUI.Desktop.Controls.ColorPicker`、`AtomUI.Fonts.AlibabaSans` 与 `AtomUI.Icons.AntDesign` 的 `6.1.2` 包。工程未引用 DataGrid 包，并通过 MSBuild 目标在发现该直接包引用时立即失败；Desktop 自动化测试另行检查生成程序集不引用 DataGrid 程序集。

## 2. 组件优先级

Desktop 实现按以下顺序选择视觉能力：

1. 直接使用 AtomUI 的 Stable 公开控件和公开 API。
2. 用多个 AtomUI 控件组合成 AtomPix 专用 `UserControl`，但不复制 AtomUI 模板。
3. 仅在 AtomUI 组件的产品语义不匹配时使用 Avalonia 原生布局、绘制、输入或平台 API。
4. 只有图片视口、裁剪选框等领域特有交互才新增 AtomPix 自定义 `Control`。

“优先使用 AtomUI”不表示把所有能力强行解释为现有组件：

- AtomUI 控件只负责视图交互和视觉状态；Workflow/Core 仍是业务状态真源。
- ViewModel 不持有 AtomUI/Avalonia 控件、`Window`、`Bitmap`、`Color`、StorageItem 或事件参数。
- 不使用 AtomUI internal 类型、Template Part 或伪类作为 AtomPix 业务契约。
- AtomUI 控件的 `SelectedItem`、`Value`、`IsOpen` 等状态只能绑定或投影 ViewModel 状态，不能形成第二份业务事实。

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
```

约束如下：

- `UseDesktopControls()` 注册公共和桌面控件主题；ColorPicker 再通过独立注册入口追加。禁止调用 `UseDesktopDataGrid()`。
- 主题必须在首个窗口创建前完成注册。首帧主题使用 AtomUI 的初始主题配置，不在窗口显示后补刷一套颜色。
- AtomPix 不自行扫描 AtomUI 主题资源，不手工引用 AtomUI 私有 AXAML，也不依赖 Gallery 项目。
- Ant Design 图标使用 AtomUI 图标包的公开 Provider。已核对可用于主导航的图标包括 `PictureOutlined`、`CompressOutlined`、`SwapOutlined`、`ColumnWidthOutlined`、`ScissorOutlined`、`FolderOpenOutlined`、`SettingOutlined`。
- 字体以 AtomUI 注册字体和平台中文 fallback 为准，不下载网络字体；最终字号、行高和对比度通过主题 Token 与视觉验收确定。

## 4. 全局组件映射

| AtomPix 语义 | 首选 AtomUI 组件 | 使用规则 |
| --- | --- | --- |
| 主窗口与标题栏 | `Window`、`WindowTitleBar` | 复用 AtomUI 窗口、主题和平台装饰能力，不按操作系统复制三套标题栏。 |
| 左侧主导航 | `NavMenu`，`Mode=Inline` | 节点映射浏览、四项单张功能、批量任务和设置；`SelectedItem` 只投影当前 Route，路由生命周期仍由 Shell ViewModel 管理。 |
| 页面分栏 | `Splitter`、Avalonia `Grid` | 浏览器、编辑器和批量任务允许用户调整主区与侧栏；最小宽度由页面布局约束。 |
| 页面滚动 | AtomUI `ScrollViewer` | 参数面板独立滚动；图片视口不因右侧表单滚动而改变缩放。 |
| 水平/垂直间距 | `Space`，必要时 Avalonia Panel | 规则操作区优先使用 `Space`；复杂自适应网格使用 `Grid`。 |
| 分组容器 | `Card`、`Separator`、`GroupBox` | 参数组、结果摘要、最近记录和警告摘要优先组合现有容器。 |
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
| 简单集合 | `ListView` | 最近记录、横向缩略图和错误明细；使用模板和容器回收，不让数据项引用视觉对象。 |
| 批量明细 | 静态表头 `Grid` + 虚拟化 `ListView` | `ItemTemplate` 使用与表头一致的五列 Grid 展示缩略图、输出路径、体积、状态和操作；ItemsSource 保持冻结输入顺序。 |
| 图片详情 | `Descriptions`、`Statistic` | 展示尺寸、格式、Metadata、ICC 和结果统计；不从显示值反算业务结果。 |
| 状态标签 | `Tag`；计数提示使用 `CountBadge` / `DotBadge` | Pending/Running/Success/Failed/Skipped/Canceled 使用统一语义颜色和文字，颜色不是唯一信息。 |
| 总进度 | `ProgressBar` | 只显示 Workflow 提供的可计算批量总进度；单张内部进度未知时不伪造百分比。 |
| 不确定等待 | `Spin` | 当前单张执行、当前批量行和短暂系统调用使用不确定反馈。 |
| 骨架加载 | `Skeleton` | 首页最近记录、浏览器主图和缩略图首次加载；旧代次晚返回不能替换新内容。 |
| 空内容 | `Empty` | 无最近记录、空文件夹、空批量草稿；提供与状态匹配的唯一下一步。 |
| 页面终态 | `Result` 或结果 `Card` | 仅在终态占据主要页面区域时使用 Result；编辑页内结果优先保留在同页 Card。 |
| 内联错误/提醒 | `Alert` | 校验、部分成功、资源限制和可恢复错误；恢复按钮绑定现有命令。 |
| 阻断确认 | `Dialog` / `MessageBox` | 源文件覆盖阻断、离开运行页确认等真正模态场景；必须指定 owner。 |
| 短暂通知 | `Message` | 添加输入统计、保存成功等非阻断反馈；重要错误不能只显示短暂 Message。 |
| 系统级通知卡 | `Notification` | MVP 默认不用；只有需要在当前窗口内长期可见且不阻断的后台结果时再启用。 |
| 辅助说明 | `Tooltip` | 完整路径、禁用原因、图标含义；关键错误不得只放 Tooltip。 |
| 结果大图查看 | `ImagePreviewer` | 用于成功结果或浏览图片的弹层查看、缩放和切换，不作为主编辑画布。 |

## 5. 01–13 原型逐页映射

| 原型 | 页面/状态 | 主要 AtomUI 组件 | AtomPix 补位与边界 |
| --- | --- | --- | --- |
| 01 | 首页 / 空态 | `Card`、`Button`、`ListView`、`Empty`、`Skeleton`、Ant Design 图标 | “打开图片/打开文件夹”使用普通 Button 调用 `IDesktopPickerService`；首页拖放只提取路径并进入对应 ViewModel 命令。 |
| 02 | 图片浏览器 | `Splitter`、`ListView`、`Descriptions`、`Button`、`Tooltip`、`Tag`、`Skeleton` | 主图使用 `AtomPixImageViewport`；缩略图集合使用 ListView，已实现容器通过 `BrowserThumbnailView` 触发延迟请求。`ImagePreviewer` 只用于可选的大图弹层，不替代当前图片状态。 |
| 03 | 单张压缩 | `Form`、`Segmented`、`Slider`、`NumericUpDown`、`CheckBox`、`Select`、`Button`、`Alert`、`Card` | 五种模式绑定同一压缩草稿；Custom 才显示质量编辑，Smart 只读；预览使用共享图片视口。 |
| 04 | 单张转换 | `Form`、`Segmented`、`Slider`、`NumericUpDown`、`ColorPicker`、`LineEdit`、`CheckBox`、`Select`、`Button` | 只有 JPEG 且源图真实透明时显示不透明背景色编辑器；Color 只在 View 层转换，铺底仍由 Workflow/Imaging 执行。 |
| 05 | 批量任务 | `Segmented`、`Button`、`ProgressBar`、`ListView`、`Tag`、`Spin`、`Form`、`LineEdit`、`Select`、`Alert` | ListView 行索引来自冻结计划；当前 Running 行使用 Spin。输入按钮仍通过 Picker/Workflow，不把列表或 Upload 当作输入真源。 |
| 06 | 设置 | `Form`、`Card`、`Segmented`、`Slider`、`NumericUpDown`、`Select`、`CheckBox`、`ColorPicker`、`Button`、`Alert`、`Message` | Dirty/Saving/SaveFailed 来自设置 ViewModel；公共 Metadata 开关同步写入三份默认 Profile 的动作由 ViewModel 构造，不由控件分别保存。 |
| 08 | 错误与边界 | `Alert`、`Result`、`Dialog`/`MessageBox`、`Button`、`Tag`、`Descriptions` | 错误码决定严重程度和恢复动作；`OutputPathConflictsWithInput` 使用阻断 Dialog，取消使用中性反馈。 |
| 09 | 单张调整尺寸 | `Form`、`Segmented`、`NumericUpDown`、`CheckBox`、快捷 `Button`、`Descriptions`、`Button` | Pixel/百分比共享 `ResizePolicy` 草稿；预计尺寸由确定性本地规则投影，图片视口只展示原图。 |
| 10 | 单张裁剪 | `Form`、`NumericUpDown`、`OptionButtonGroup`/`Segmented`、`Button`、`Tooltip`、`Descriptions` | 使用 `AtomPixCropCanvas` 维护画布坐标换算、遮罩和八个控制点；ViewModel 持有最终像素 `CropRectangle`。 |
| 11 | 首页与浏览器状态板 | `Skeleton`、`Spin`、`Empty`、`Alert`、`Tooltip`、`Button` | 复用 01/02 组件，不另建“状态板控件”；状态由内容加载代次和当前图片有效性驱动。 |
| 12 | 四类单张编辑器状态板 | `Spin`、`Alert`、`Result`/`Card`、`Dialog`、`Button`、`Tag` | Processing 锁定参数；成功、失败、取消留在同页；输出冲突形成新提交，不在 Dialog 内直接重跑。 |
| 13 | 批量与设置状态板 | `ProgressBar`、`ListView`、`Spin`、`Alert`、`Result`、`Form`、`Message` | 批量终态以 Workflow 最终结果校正；设置保存失败保留 Dirty 草稿；状态板不形成第二套状态枚举。 |

## 6. 必要的 AtomPix 自定义视图能力

| 自定义能力 | 基础 | 为什么不能直接替换为 AtomUI 现有控件 | 状态所有权 |
| --- | --- | --- | --- |
| `AtomPixImageViewport` | Avalonia `Control`/`Image`、变换与输入事件；外围使用 AtomUI Button/Tooltip/Spin | `ImagePreviewer` 的定位是封面加弹层查看器，不是一直嵌入页面的编辑工作区。 | ViewModel 拥有来源、加载代次和缩放意图；View 持有 Bitmap、像素到视口变换及 Pointer capture。 |
| `BrowserThumbnailView` | `AtomPixImageViewport` + AtomUI Spin/Text | 缩略图必须由已实现的 ListView 容器触发，不能在建立浏览集合时一次性解码全部图片。 | ViewModel 持有会话级字节缓存、独立取消和并发上限；View 只在容器附着时请求。 |
| `AtomPixCropCanvas` | 上述视口 + 局部 `AdornerLayer`/Canvas 绘制 | AtomUI 没有图片裁剪选框、八点缩放、边界约束和像素矩形同步控件。 | ViewModel 拥有像素 CropRectangle/比例意图；Control 只投影可视几何和交互手势。 |
| `AtomPixInputDropZone` | Avalonia DragDrop + AtomUI Card/Icon/TextBlock 视觉 | AtomUI `Upload` 会拥有 `Files`、准入、目录展开和上传队列；这与 `AppendBatchInputsWorkflow` 的目录枚举、去重和 `BatchInputPlan` 真源冲突。 | View 只提取文件或目录路径并调用命令，不遍历目录、不判断格式、不去重。 |
| `OutputPolicyEditorView` | AtomUI `Segmented`、`TextBox`、`Button`、`Alert` 的共享组合 | 五类任务页必须编辑同一语义的输出位置、命名与冲突策略；复制五套控件会造成校验和文案漂移。 | 框架无关 `OutputPolicyEditorViewModel` 构造 Core `OutputPolicy`；UserControl 只负责组合布局。 |
| `BatchTaskListView` | 静态表头 Grid + AtomUI `ListView` + 五列 `ItemTemplate` | 批量明细需要稳定列对齐，但明确禁止 DataGrid 包。该类型只是页面专用组合视图，不扩展成通用表格控件。 | ViewModel 的冻结行集合、Index 和命令是唯一状态；UserControl 只负责表头、列宽、文本省略、Tooltip 和容器模板。 |
| `AtomPixPreviewPresentationAdapter` | Avalonia Bitmap/Dispatcher 生命周期 | Workflow 返回编码字节，AtomUI 图片控件不应迫使内层返回 Bitmap。 | 适配器创建、替换和释放 Bitmap；ViewModel 只持有框架无关负载或展示句柄。 |
| `AtomPixColorAdapter` | Converter/Behavior | AtomUI ColorPicker 使用 Avalonia `Color?`，Core 使用 `RgbColor`。 | ViewModel/Core 值是提交真源；ColorPicker 只是双向编辑投影。 |

这些类型都留在 `AtomPix.Desktop`。除纯 ViewModel 状态外，不为了“可复用”把 Avalonia 或 AtomUI 类型抽到 Core、Workflows 或 Imaging。

## 7. 明确不采用或受限采用的 AtomUI 组件

| 组件 | 结论 | 原因 |
| --- | --- | --- |
| `Upload` / `UploadTrigger` / `UploadDropZone` | MVP 批量输入不采用 | 它们会建立独立 `Files` 状态、执行准入和目录展开；AtomPix 要求目录枚举、支持格式判断、规范化去重由 Workflow 完成。普通 Button + Picker 及薄 DragDrop Bridge 更符合边界。 |
| `ImagePreviewer` | 只用于结果/大图查看 | 不承载裁剪矩形、编辑草稿或主页面的 CurrentImagePath。 |
| `SearchEdit` | 图片浏览器不采用 | 产品已经明确浏览器没有搜索框。 |
| `TabControl` | 不作为三类批量任务真源 | 批量任务类型只是同一草稿中的少量互斥选项，使用 Segmented；页面与草稿生命周期由 ViewModel 控制。 |
| `AtomUI.Desktop.Controls.DataGrid` 整个包 | 禁止使用 | 不添加 PackageReference、不调用 `UseDesktopDataGrid()`、不使用其命名空间，也不复制其源码实现。 |
| `Notification` | 默认不使用 | 当前是单前台任务，重要结果应留在页面；短暂反馈用 Message，阻断错误用 Alert/Dialog。 |
| AtomUI Gallery 控件 | 不引用 | Gallery 是示例与文档宿主，不是 AtomPix 生产组件依赖。 |

## 8. 状态到反馈组件的统一映射

| Desktop 状态 | 视觉组件 | 规则 |
| --- | --- | --- |
| `Empty` | `Empty` | 提供与页面语义匹配的打开/添加入口。 |
| `Loading` | `Skeleton` 或 `Spin` | 内容骨架用 Skeleton；短时动作和不可预知等待用 Spin。 |
| `Ready` | Form、ListView、Button 等常态组件 | 控件启用完全由派生 `CanExecute` 和编辑锁控制。 |
| `Processing` | `Spin`；批量另有 `ProgressBar` | 单张不显示伪百分比；批量总进度使用已完成项数量。 |
| `Success` | 同页结果 `Card`、`Tag`，必要时 `Result` | 即使输出体积增加也保持 Success，并显示实际结果。 |
| `Partial` | Warning `Alert` + ListView 终态行 | 已成功输出保留，恢复动作只建立新草稿。 |
| `Failure` | Error `Alert`；阻断条件用 Dialog | 必须提供错误码对应的下一步，不能只弹短暂 Message。 |
| `Canceled` | 中性 `Alert`/`Tag` | 不使用异常红色，不删除已经成功提交的输出。 |

AtomUI `ProgressBar`、`Spin`、`Alert`、`Result` 只投影 Desktop 状态，不直接修改 Core Job。AtomUI 控件事件先进入 ViewModel Command；Command 再决定是否调用 Workflow。

## 9. 主题、Token 与界面质量

- 原型中的 `#4F6BED`、圆角和间距是视觉意图，不应逐控件硬编码。优先通过 AtomUI Global/Control Token 或 AtomPix 应用资源统一覆盖。
- 成功、警告、失败、信息和禁用态使用 AtomUI 语义 Token；所有状态同时提供文字或图标，不能只靠颜色。
- Light、Dark 和 FollowSystem 使用 AtomUI ThemeManager；首轮至少验证 Light 与 FollowSystem，Dark 不允许因硬编码画刷而出现不可读文本。
- 动效遵循 AtomUI `IsMotionEnabled` 和全局 motion 配置。批量行频繁更新不得触发无业务意义的重复动效。
- Tooltip、Dialog、Message、Notification 使用 AtomUI 既有 overlay/feedback layer，不手写随机高 ZIndex。
- 裁剪选框属于图片视口局部装饰，使用局部 Adorner/Canvas，不放进窗口级 `WindowFeedbackLayer`。
- 文本缩放、本地化长度和高 DPI 下不得用固定像素裁掉按钮文字；原型尺寸只是初始布局参考。

## 10. 推荐实现顺序

```text
1. Desktop 工程、AtomUI 包、主题和 Window/Shell
2. NavMenu 路由与公共页面布局
3. 反馈组件、Form 适配和 Picker/Launcher 桥接
4. AtomPixImageViewport 与预览 Bitmap 生命周期
5. 首页和图片浏览器
6. 单张压缩、转换、Resize 的共享编辑器壳体
7. AtomPixCropCanvas 与裁剪页面
8. ListView 批量任务页和实时进度投影
9. 设置页
10. 01–13 状态与 UI 自动化验收
```

截至 2026-08-07，步骤 1–9 已完成第一阶段纵向实现：Shell、公共反馈、首页、浏览器、四类单张内容页、CropCanvas、三类批量 ListView 和设置持久化页均接通正式 Workflow。当前使用的 AtomUI 控件包括 Window、NavMenu、Card、Button、Alert、Empty、Spin、ListView、Segmented、NumericUpDown、Slider、Select、CheckBox、Drawer、Dialog、ProgressBar、ScrollViewer、ColorPicker 和 Ant Design 图标；图片预览由 `AtomPixImageViewport` 管理 Bitmap 创建、替换和释放，裁剪由 `AtomPixCropCanvas` 负责画布坐标与像素矩形转换。步骤 10 的自动化状态测试已经建立，10000 项 ListView 虚拟化、稳定控件定位名称和 Windows 发布进程 UIA 导航已纳入门禁；真实多 DPI 仍属于发布验收。屏幕阅读器、UIA 动作模式和全页面纯键盘巡检不属于当前版本需求。

共享编辑器壳体只复用布局、预览、输出策略和结果区域，不能把四类 Request 合并成一个包含大量可空字段的通用请求。

## 11. 验收与升级检查

AtomUI 组件集成至少覆盖：

- 应用启动时 AtomUI 主包与 ColorPicker 主题均成功注册；首帧无缺失资源，且工程没有 DataGrid 包引用或注册。
- NavMenu 选中项、Shell Route 和运行期导航锁一致，重复点击当前项不创建第二页面实例。
- Slider 与 NumericUpDown 绑定同一质量值，不产生循环或短暂非法值。
- ColorPicker、HEX 文本、黑白快捷项与 Core `RgbColor` 往返一致，Alpha 永远不会进入请求。
- 批量 ListView 使用默认虚拟化面板；较大输入集合滚动和容器回收后，进度只更新目标 Index，复用容器不串行状态、命令或缩略图。
- 批量表头和行模板使用相同五列布局：文件、输出文件为比例列，大小、状态、操作为固定列；长文本省略并提供完整 Tooltip，窗口变窄时仍保持对齐。
- `ImagePreviewer` 的 Sources 替换或页面 detach 后释放过期图片资源；仅关闭弹层时允许按控件契约保留仍需显示的封面。主视口切换代次后释放旧 Bitmap，晚返回结果不能覆盖当前图片。
- CropCanvas 在窗口缩放、DPI、缩放和平移后仍提交正确的整数像素矩形，控制点不越过图像边界。
- Alert/Dialog/Message 的选型符合第 8 节，Dialog 始终具备 owner、键盘关闭策略和焦点恢复。
- Light/Dark/FollowSystem、125%/150%/200% DPI、键盘导航和简体中文长文案通过页面级快照或集成验收。
- AtomUI 升级后重新核对公开 API、主题注册、ListView 容器回收、ColorPicker Value 绑定和 ImagePreviewer 生命周期；不得通过引用 internal 成员规避破坏性变化。

## 12. 与其他设计文档的关系

- 产品范围与可见行为：`../../product/mvp-scope.md`。
- Desktop 职责与系统适配：`overview.md`。
- 控件启用、Loading 和恢复动作：`interaction-state-design.md`。
- 页面布局和视觉意图：`../../ui-prototype/README.md` 与 01–13 SVG。
- Workflow/Core 状态编排：`../workflows/job-state-orchestration.md`。

若 AtomUI 组件的默认行为与这些正式业务规则冲突，应在 Desktop View/Adapter 中限制组件行为或改用更薄的 Avalonia 实现，不能反向修改业务规则来迁就组件。
