# AtomUI.Labs ImageGallery 接入与迁移设计

> 文档状态：正式设计，生产迁移已实现；后期能力见第 6 节 TODO
>
> 基线时间：2026-08-24

本文冻结 AtomPix 图片浏览器从 Desktop 内置 `AtomPixImageGalleryViewer` / `AtomPixImageViewport` 迁移到 `AtomUI.Labs.Controls.ImageGallery` 的工程方案。迁移只替换 Desktop 的图片呈现与画廊基础设施，不改变产品范围、Workflow/Core 状态、处理请求、输出策略或批量任务语义。

## 1. 设计决策

- AtomPix 图片浏览器的目标基础控件是 AtomUI.Labs 的公开 `ImageGallery`，不再长期维护一套平行的主图缩放、平移、工具栏、虚拟化图片走廊和图片缓存实现。
- 迁移后的 Shell 使用独立标题栏与两种工作区：`ActiveTool=None` 时 ImageGallery 占满内容区；选择工具后 ImageGallery/Crop 工作区与约 `380 px` 右侧处理面板通过普通 Grid 并列。旧沉浸式标题栏和覆盖式 Drawer 不进入目标实现。
- AtomUI.Labs 尚未发布到正式 NuGet 源。过渡期采用“在 AtomUI.Labs 产出 `.nupkg`，再把不可变包复制到 AtomPix 仓库内的本地 NuGet 源”方式参与 restore/build。
- AtomPix 不引用 `D:\work\c#\AtomUI.Labs` 源码项目，不把该绝对路径写入 `.csproj`、脚本、`NuGet.config` 或 CI，也不复制 ImageGallery 源码和私有模板。
- 当前生产 Browser 已使用 AtomUI.Labs `ImageGallery`；旧 `AtomPixImageGalleryViewer`、Browser 用 `AtomPixImageViewport`、旧缩略图容器、主题和 Desktop Preview/Thumbnail 字节缓存均已删除。CropCanvas 是仍需保留的业务编辑控件，不属于重复浏览器实现。
- `ImageGallery` 是 Desktop 视觉依赖。Core、Imaging.Abstractions、Imaging.Magick、Infrastructure 和 Workflows 均不得引用 AtomUI.Labs、Avalonia 或其公开类型。

## 2. 已核对的包基线

| 项目 | 已核对值 | AtomPix 约束 |
| --- | --- | --- |
| Package ID | `AtomUI.Labs.Controls.ImageGallery` | `Directory.Packages.props` 固定明确版本，不使用浮动版本。 |
| 过渡版本 | `6.0.8` | 初始迁移输入固定为此版本；升级必须重新执行本文全部兼容性与行为门禁。 |
| 原始产物 | `D:\work\c#\AtomUI.Labs\output\Nuget\Release\AtomUI.Labs.Controls.ImageGallery.6.0.8.nupkg` | 只作为人工复制来源，不是 AtomPix 的构建输入路径。 |
| SHA-256 | `86B4A7E63D290356B05A804B37D8808C797FF5FC7C036057302ED6A48C2BB35F` | 复制进入 AtomPix 后必须逐字节匹配；Desktop 构建在编译前校验，不匹配立即失败。 |
| AtomUI.Labs 源提交 | `18ea3f9dbbf49f8ab1b1cac04962aac616a3b389` | 来自 nupkg repository metadata，用于追溯二进制来源；AtomPix 不据此自动拉取或重打包。 |
| 目标框架 | `net10.0`、`net8.0` | AtomPix 使用包内 `net10.0` 资产。 |
| 包声明依赖 | `AtomUI.Core >= 6.0.8`、`Avalonia >= 12.0.5` | 实测该二进制使用 AtomUI `6.0.8` 已移除的公开类型，不能与 AtomUI `6.1.3` 混用；AtomPix 的 AtomUI Desktop/ColorPicker/Font/Icon 包统一锁定 `6.0.8`，Avalonia 保持 `12.1.1`。 |
| 注册入口 | `UseImageGallery()` | 已在首个 Window 创建前，与 `UseDesktopControls()`、`UseDesktopColorPicker()` 一起调用公开入口。 |
| AXAML 命名空间 | `https://atomui.net/labs` | 页面只消费公开 `ImageGallery`，不引用 internal 类型或 Template Part。 |
| 包定位 | Experimental | 过渡期按受控供应链二进制管理，不能按正式稳定包降低验收标准。 |

## 3. 过渡期本地 NuGet 包治理

目标仓库结构：

```text
AtomPix/
  eng/
    nuget-local/
      AtomUI.Labs.Controls.ImageGallery.6.0.8.nupkg
    nuget-cache/
      atomui.labs.controls.imagegallery/6.0.8/...
  NuGet.config
  Directory.Packages.props
  src/AtomPix.Desktop/AtomPix.Desktop.csproj
```

约束如下：

1. 人工从第 2 节的原始产物复制到 `eng/nuget-local/`，文件名、长度和 SHA-256 保持不变；不得在 AtomPix 构建过程中跨仓库执行 `dotnet pack`。
2. 根 `NuGet.config` 使用相对路径注册仓库内本地源，同时保留正式 NuGet 源；`Directory.Build.props` 把 restore cache 固定到 `eng/nuget-cache`，避免开发机全局缓存中同版本旧包遮蔽本次重打包制品。仓库内预展开的 `6.0.8` 目录与 nupkg 必须来自同一哈希制品。
3. `Directory.Packages.props` 固定 `AtomUI.Labs.Controls.ImageGallery` 为 `6.0.8`，`AtomPix.Desktop.csproj` 只写无版本 `PackageReference`。测试项目通过 Desktop 的传递依赖或明确的测试需要消费，不复制版本号。
4. Desktop 的 `VerifyAtomUIImageGalleryPackage` 构建目标在编译前校验本地包存在且 SHA-256 匹配；错误消息指出预期文件和实际哈希，不得静默回退到另一制品。
5. lock/资产审计必须证明最终只解析一份 `AtomUI.Core` 和一份 Avalonia 基线，且没有 `NU1605`、包降级、资产冲突或重复主题程序集。
6. 本地包必须进入 CI、三平台 publish 和 release 的同一 restore 路径。CI 不允许访问 `D:\work\c#\AtomUI.Labs`，开发机绝对路径存在也不能改变构建结果。
7. 正式 NuGet 发布了经过验收的 ImageGallery 后，退出过渡方案：切换到正式源、删除仓库内 nupkg/预展开 cache/哈希门禁，再执行一次完整升级验收；不得长期同时保留正式源和 vendored 包两种事实来源。

兼容性结论：新的 ImageGallery `6.0.8` 在 AtomUI `6.1.3` 运行时因 `LanguageProvider` 二进制契约变化而不兼容。为避免混装两套 AtomUI Core，AtomPix 已把全部 AtomUI 包统一到 `6.0.8`；Avalonia 仍固定 `12.1.1`。后续升级必须让 AtomUI 与 Labs 使用同一版本线并重新执行启动、主题、行为和发布回归，禁止只升级其中一侧。

## 4. 目标组件边界

| 能力 | 目标所有者 | AtomPix 保留职责 |
| --- | --- | --- |
| 当前左侧工作区内的主图、Fit/ActualSize/Custom Zoom、平移与视口几何 | AtomUI.Labs `ImageGallery` | Shell 提供浏览态全宽或操作态左列 Bounds；标题栏与右侧面板由普通布局排除，图标轨仍作为 Gallery 内浮层。缩放不得反馈改变窗口或布局列尺寸。 |
| 顶部工具栏、缩放按钮与百分比显示 | `ImageGallery` 公开属性/命令/Appearance | AtomPix 通过公开配置关闭未进入产品范围的功能，并用 Theme Token 调整外观；不复制模板。 |
| 图片走廊、虚拟化、滚轮横向滚动、选中项滚入可见区 | `ImageGallery` | ViewModel 仍拥有业务集合顺序和 CurrentItem。VM→Gallery 使用稳定 ItemsSource 快照与单向 SelectedItem 投影，View 通过公开 `SelectionChanged` 把真实用户选择提交回 VM；内部资源恢复动作不得伪造业务 CurrentItem 变化。 |
| 追加图片按钮 | `ImageGallery.AddImageCommand` | 绑定现有 `AddImagesCommand`；Picker、规范化、支持性判断与去重规则不变。 |
| 主图/缩略图解码、lease、内存预算、预取与取消 | `ImageGallery` 的 `IImageGallerySource`、加载调度和缓存 | AtomPix 为每项创建 Desktop-only 适配对象，提供稳定 Key/Identity，并把组件异常投影为当前集合代次内的安全错误；不得再维护第二套同用途 Bitmap/字节 LRU。 |
| 文件夹枚举、候选过滤、Probe、任务能力与图片处理 | AtomPix Workflow/Core | 不下放给 ImageGallery；图片能被展示不表示它可以执行四项处理。 |
| 批量 Pending/Running/Succeeded/Failed/Skipped/Canceled | AtomPix ViewModel | Workflow 仍是任务真源；ImageGallery 只承载缩略图状态视觉，不创建第二套任务状态。 |
| Crop 选框、左列 SafeArea、像素坐标换算 | `AtomPixCropCanvas` + ImageGallery 公开资源租约 | Crop 模式把 Gallery 切到 `ResourceOnly`，保留选择、加载和走廊但让空白主图区视觉/命中穿透；View 在 UI 线程按 expected item 调用 `TryAcquireCurrentImage`，把非自有 `IImage` 交给 CropCanvas，并独占、及时释放外部 Lease。CropRectangle 仍由 Desktop 提交给 Workflow。 |
| 右侧普通布局面板、设置页面、贴左图标轨 | AtomUI Desktop Controls + AtomPix Shell | 右侧面板通过 Shell Grid 与 ImageGallery 并列；设置使用 Shell 普通内容页；图标轨在 Home/Browser 显示、进入设置时隐藏。三者均不通过修改 ImageGallery 私有模板实现。 |

生产适配模型位于 `AtomPix.Desktop`，名称为 `ImageGalleryItemAdapter`：

- 实现公开 `IImageGalleryItem`；`Key` 使用会话内稳定、平台规则一致的规范化路径身份，`Title` 使用完整文件名。
- `MainImageSource` 使用公开 `ImageGallerySources.FromFile`，Identity 为规范化路径；`ThumbnailImageSource=null` 明确让组件复用同一 Source，并由请求 Purpose/目标档位区分缓存，不在 AtomPix 重复创建缩略图 Source。
- 适配对象可以引用对应的 Browser item，但不得进入 Core/Workflow 请求、设置持久化或最近记录序列化。
- 同一浏览集合代次内，`GalleryItems` 必须保存并返回同一个只读快照引用，禁止 getter 每次 `ToArray()` 生成新的 ItemsSource；否则布局重绑定会无意义地重建 descriptor，使已就绪资源无法通过 expected-item Lease 校验。只有项目追加、移除、集合替换或页面离场才建立新快照；之后的迟到加载、容器回收与 lease 释放仍由 ImageGallery 自己的 descriptor/lifecycle generation 管理。

## 5. AtomPix 行为配置基线

接入时必须显式配置产品行为，不能直接接受实验控件的全部默认值：

- 初始 `ZoomMode=Fit`，完整图片居中展示并允许左右或上下留白；是否允许小图 Fit 放大以 AtomPix 当前设计为准。
- 缩放范围保持 `25%..400%`；`+/-` 的步进行为直接跟随 ImageGallery 公开默认行为。AtomPix 不再实现固定增加/减少 `25` 个百分点的自定义策略，也不覆盖 `ZoomStep`；`6.0.8` 当前默认乘法因子为 `1.2`。后续组件版本若调整默认步进，按组件升级验收处理，不在 AtomPix 内维护第二套缩放算法。
- 顺时针旋转不是 AtomPix 当前功能，工具栏显式隐藏旋转；其余缩放与平移输入跟随 ImageGallery 公开默认交互，不在 AtomPix 维护第二套手势。
- `IsLoopNavigationEnabled=false`；到达第一张/最后一张后不得循环。
- 顶部工具栏、底部走廊、追加/走廊上一张/走廊下一张按钮使用 ImageGallery 公开 Appearance 与 AtomPix Theme Token 对齐现行设计；主图翻页按钮显式关闭。
- `ThumbnailFilmstripPlacement=Bottom`；走廊继续作为覆盖主图的内部浮层，并始终按 ImageGallery 当前 Bounds 居中。操作态由 Shell 收窄控件本身，走廊无需感知或避让右侧面板。
- 所有模式固定 `IsViewportNavigationEnabled=false`，图片切换统一由走廊按钮或缩略图完成。Crop 模式额外设置 `MainImageMode=ResourceOnly`，保留 `IsFilmstripNavigationEnabled=true`；空白 Viewport 不命中，Pointer 直接交给下层 CropCanvas。Gallery 工具栏在 Crop 模式隐藏。
- `LoadLimits` 从 `ImageProcessorCapabilities.Resources` 映射：编码字节、单边和总像素分别复用 AtomPix 输入硬边界，解码字节按总像素 × 4 的 RGBA 上限计算；主图/缩略图缓存预算显式为 `128 MiB / 32 MiB`。这些是按需上限，不在启动时预分配。错误显示不得泄漏完整路径或底层异常原文。

## 6. `6.0.8` 公开 API 差距与前置补齐

源码与包内公开契约核对表明，`6.0.8` 与 AtomPix 当前或后期目标之间存在以下差距；表中已分别标明阻断项、后期 TODO 和已接受的组件行为：

| 缺口 | 影响 | 正式处理 |
| --- | --- | --- |
| 缩略图没有公开状态 Adorner/模板入口 | 无法在虚拟化缩略图右上角表达批量六态及动画 | **TODO（后期迭代，不阻断当前迁移）**：未来在 AtomUI.Labs 增加公开、可虚拟化回收的 item adornment/status presenter 契约；当前批量进度与结果只由右侧面板表达，AtomPix 不访问 internal `ImageGalleryThumbnailItem`。 |
| 没有与 CurrentItem 分离的 `ActiveBatchIndex` 跟随入口 | 批量运行时 CurrentItem 冻结，画廊不能天然跟随 Running 项 | **TODO（后期迭代，不阻断当前迁移）**：未来在 AtomUI.Labs 增加公开 Bring/Follow index 能力及可取消滚动策略；当前由右侧面板显示当前处理项，走廊只跟随 `CurrentItem`。 |
| Crop 复用当前已解码主图 | 若另行 Preview 会产生双重加载、两套方向/清晰度与生命周期 | **已解决**：`ResourceOnly` 保持 Gallery 的逻辑加载/缓存并关闭默认主图绘制和命中；`TryAcquireCurrentImage(expectedItem)` 返回调用方所有 Lease，`CurrentImageResourceChanged` 驱动首次提交与清晰度升级。View 持有 Lease，CropCanvas 只借用 `IImage`；同一项升级期间继续显示旧 Lease，新 Lease 就绪后原子替换，不闪空白。 |
| ImageState 只有 Empty/Loading/Ready/Error，未公开 AtomPix 恢复内容插槽或错误详情 | 无法直接承载“移出不可用项”等产品恢复动作 | AtomPix 可在组件外层保留轻量状态覆盖层；底层异常必须由公开 Source 包装器安全投影，不能反射组件内部异常。 |
| 乘法 `ZoomStep` 与历史 AtomPix 固定 25 个百分点步进不同 | UI 手感与显示百分比发生产品变化 | **已决议，不再是缺口**：AtomPix 跟随 ImageGallery 原生步进，不自定义固定百分点算法。 |

当前迁移没有阻断项。`ActiveBatchIndex` 跟随与缩略图六态插槽均为后期 TODO，缩放步进已经接受组件原生行为，Crop 资源复用已经由正式公开 API 解决。未来需要新能力时应回到 AtomUI.Labs 形成新公开包；AtomPix 不能复制模板、使用 internal 类型、反射 Template Part 或叠加第二套假走廊。

## 7. 迁移实施状态

1. **已完成—供应链**：精确 nupkg、仓库相对本地源、中央版本、仓库 restore cache 和 SHA-256 构建门禁均已建立。
2. **已完成—兼容性**：AtomUI 全家桶统一为 `6.0.8`，Avalonia 保持 `12.1.1`；应用通过公开 `UseImageGallery()` 注册。
3. **已完成—公开 API**：采用包含 `ResourceOnly`、解码尺寸提示、当前资源事件和安全 Lease 的新包，不使用 internal 或模板部件。
4. **已完成—适配层**：Desktop-only `ImageGalleryItemAdapter` 提供稳定路径身份；业务 ViewModel 不持有 Bitmap、预览字节或组件缓存。
5. **已完成—生产切换**：Browser AXAML 只承载 Labs `ImageGallery`；Shell 使用普通全宽/两列布局，Crop 使用资源模式与 Canvas 组合。
6. **已完成—旧实现清理**：旧 Viewer、Viewport、缩略图 View、主题、Preview/Thumbnail LRU 和自定义缩放命令已经删除，不保留运行时双轨。
7. **待每次发布执行—验证**：第 8 节自动门禁与人工视觉矩阵必须随 release 重跑；历史一次通过不能替代未来升级验收。
8. **后期—退出过渡包**：待正式 NuGet 可用后按第 3 节切换并再次全量回归。

## 8. 验收矩阵

迁移完成至少满足：

- restore 在干净 NuGet 缓存和 CI 三平台环境中只依赖仓库内容与正式源；本地 nupkg 缺失、篡改或版本不符时确定性失败。
- AtomUI `6.0.8`、Avalonia `12.1.1` 与 ImageGallery 依赖图无混装/冲突；应用首窗前完成主题、语言和 ImageGallery 注册，发布产物包含所需程序集与资源。
- JPEG（含 EXIF 方向/灰度/CMYK/ICC）、PNG、静态 WebP、BMP 的主图与缩略图符合现有支持范围；动画/多帧边界不因控件接入而改变处理能力判定。
- 默认 Fit 显示完整图片；`+/-/1:1/适应`、缩放上下限、窗口缩放、平移和留白正确，图片 Extent 绝不推动顶层窗口变大。
- 打开图片、打开文件夹、增量追加、稳定去重、上一张/下一张、缩略图点击、选中项滚入可见区和走廊滚轮横移保持现有业务语义。
- 10,000 项走廊保持虚拟化；快速换图、滚动、离场和集合换代正确取消旧加载，缓存总量受预算约束，Bitmap/lease 无跨代泄漏。
- **TODO（后期迭代，不进入当前迁移验收）**：`ActiveBatchIndex` 自动跟随、用户滚动暂停、缩略图六态徽标与 Running 图标动画；当前批量进度、当前处理项与结果必须先在右侧面板完整表达。
- 普通浏览与 Crop 均不呈现视口导航按钮；Crop 模式使用 `ResourceOnly` 禁用默认主图呈现和工具而保留走廊导航。CropCanvas 获得 Pointer，并只借用与 expected item 匹配的外部 Lease 图片。切图、模式退出和 View detach 释放旧 Lease；同一项清晰度升级期间保留旧 Lease 连续呈现，在新资源就绪后替换并释放，初次进入不得要求用户点击缩略图触发显示。
- Browse/Operate 切换不改变窗口尺寸；ImageGallery 在全宽内容区与操作态左列之间获得正确 Bounds，重新执行 Fit 与响应式布局，画廊始终按组件自身区域居中。独立标题栏、图标轨和工具栏命中层级正确。
- Empty/Loading/Error/Unavailable 与恢复动作仍按 Desktop 状态机投影；错误信息脱敏，旧代次错误不能覆盖当前项。
- UI 自动化使用公开控件与稳定 AutomationProperties，不依赖 ImageGallery internal 类型、类名、伪类或 Template Part。
- Release build、Desktop 状态测试、Headless UI 渲染、压力测试、三平台 publish、启动烟测、依赖/漏洞审计及 `git diff --check` 全部通过后，才可删除旧实现。

## 9. 与其他文档的关系

- 产品范围和用户可见行为仍以 [`../../product/mvp-scope.md`](../../product/mvp-scope.md) 为准。
- Desktop 状态、批量跟随与 Crop 交互仍以 [`interaction-state-design.md`](interaction-state-design.md) 为准。
- AtomUI/AtomUI.Labs 组件选型以 [`atomui-component-mapping.md`](atomui-component-mapping.md) 为入口，本文提供 ImageGallery 专项迁移细则。
- 测试与发布门禁以 [`../../implementation/testing-and-release.md`](../../implementation/testing-and-release.md) 为准，并追加本文第 8 节的专项矩阵。
