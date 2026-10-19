# AtomPix Desktop × AtomUI.City 迁移设计

> 状态：目标设计已冻结；因 City 公共 API 门失败而阻塞，尚未施工

## 1. 目标与不变量

City 接管 Desktop 的 Host、模块生命周期、MVVM、顶层路由、View 定位、Outlet、UI Dispatcher、本地化与生成器。迁移不得改变 MVP 功能、现有 ImageGallery 交互、Browse/Operate 普通两列布局、四项单张功能、三项批量功能或输出策略。

迁移完成后只能存在一个根服务容器；`App` 不再手工构造 `ServiceCollection`、调用 `BuildServiceProvider` 或直接创建 `MainWindow`。领域、图像和基础设施程序集不得反向引用 City。

## 2. Host 与模块生命周期

- 新增 `AtomPixDesktopModule`，声明 Desktop 对 City Presentation、Routing、Localization 以及 AtomPix Workflows/Imaging/Infrastructure 注册的依赖。
- City Host 是唯一组合根，按“基础服务 → Workflows State 初始化 → Presentation 附加 → 主窗口会话注册 → 初始路由”启动。
- 初始化任一步失败时由 Host 回滚已启动模块；不得显示半初始化窗口。
- 正常退出顺序为“阻止新导航 → 完成或取消关闭确认 → 释放当前路由租约 → 关闭窗口会话 → 停止模块 → 释放 Host”。
- `App` 只桥接 Avalonia lifetime 与 City Host；`Program` 继续负责平台启动参数。

## 3. 路由与页面所有权

固定三个顶层路由：

| Route ID | 目标 | 生命周期 |
| --- | --- | --- |
| `home` | Home 页面 | 每次路由进入创建，离开释放 |
| `browser` | Browser 页面及其工具区 | 路由作用域 |
| `settings` | Settings 页面 | 每次路由进入创建，离开释放 |

压缩、转换、缩放和裁剪是 Browser 路由内部的工具模式；Batch 是 Browser 发起的短生命周期对话流程。它们不进入顶层导航历史。

Home 向 Browser 传递大量文件和探测结果时，Desktop 内部的 `BrowserNavigationSessionStore` 保存不可变 `BrowserNavigationContext`，生成一次性 `Guid` 会话键；City 路由参数只携带该键。Browser resolver 必须原子消费会话，缺失、重复消费或失效时返回现有错误展示，不回退到隐藏的全局字段。

## 4. Presentation 与 View 生命周期

- 使用 City View registry、ViewModel lease、Window session 与 route outlet。
- 删除 Shell 的 `CurrentPage` 对象切换、导航协调器和用于路由的手写 DataTemplate；Shell 只持有 outlet 与窗口级 UI 状态。
- Home、Settings 和 Browser ViewModel 不再是根容器 singleton。Browser 的工具 ViewModel、预览资源和订阅随 Browser route scope 释放。
- Batch/消息/结果对话框继续由 AtomPix Desktop adapter 管理，但所有 owner window 与关闭时序必须来自当前 City window session。
- 文件选择器、文件夹选择器、剪贴板、外部启动器和 AtomUI 反馈 adapter 保留；它们是产品/平台集成，不由 City 替代。

窗口 Closing 首次触发时允许应用取消并异步确认；确认通过后只允许一次授权关闭。City 不得提前释放窗口会话，也不得在取消关闭后留下已停用的 ViewModel。该合同必须有 Headless 覆盖。

## 5. State 与本地化消费

Desktop 注入只读 `IAtomPixApplicationState`。设置页通过 Workflows 用例写入，成功后由 State 推送；Home 订阅最近项目投影。Desktop 不取得 State writer，也不直接写 Store。

所有用户可见中文迁入 City.Localization 目录，包括 XAML 标题、按钮、空状态、验证、异常映射、通知和对话框。当前只提供中文目录和中文 invariant fallback；`System` 与 `zh-CN` 设置继续存在，系统文化没有资源时回退中文，不新增英文验收范围。

## 6. 删除与保留

删除：手工组合根、`DesktopNavigationCoordinator`、`DesktopRoute` 顶层对象路由、Shell `CurrentPage`、本地 ObservableObject/命令实现、重复 Dispatcher 抽象和无所有者 fire-and-forget。

保留：业务页面与视觉结构、AtomUI/ImageGallery 控件、平台 picker/launcher/clipboard、产品对话框、现有 Workflow 端口、错误结果模型及用户操作语义。

## 7. 验收

- 三个顶层路由的进入/离开/回退、Browser 会话原子消费和工具状态保持。
- route scope、View lease、图片资源、订阅和取消令牌均按生命周期释放。
- 关闭被取消后窗口仍可操作；授权关闭只发生一次。
- 中文资源完整且动态文化通知不产生重复订阅。
- 现有 Headless Shell、浏览、设置、单张和批量完整链路保持通过。
