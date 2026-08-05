# AtomPix.Desktop 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

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
- 直接读写配置文件、订阅状态文件、订阅状态文件。
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
ISubscriptionStore -> LocalSubscriptionStore

```

注册可以发生在 `Composition/` 或 `DependencyInjection/` 目录中。ViewModel 只依赖 Workflows 或抽象接口，不直接依赖具体实现。

## 6. UI 边界

Desktop 负责把用户输入转换为 Workflows 请求，也负责把 Workflows 返回结果转换成界面状态。

例如预览流程：

```text
ViewModel 调用 CreatePreviewWorkflow
Workflow 返回 ImagePreviewResult
Desktop 将 EncodedBytes 转换为 Avalonia Bitmap
View 显示 Bitmap
```

Avalonia 类型只停留在 Desktop 内部。
