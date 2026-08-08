# AtomPix Headless 质量闸门（历史验收记录）

> 文档状态：进入 UI 原型前的历史 headless 工程卫生基线
>
> 基线时间：2026-06-26
>
> 范围：Core、Imaging.Abstractions、Infrastructure、Imaging.Magick、Workflows；不包含 Desktop / UI。

> 当前状态：本文证明当时的 Headless 基线足以进入 UI 原型阶段；UI 原型和交互设计现已完成。本文不覆盖后来冻结的独立 Resize/Crop、实时批量进度、资源保护和诊断目标契约，现行测试计划见 `testing-and-release.md` 第 32–46 节。

## 1. 目标

本质量闸门用于判断 AtomPix 底层/headless 能力是否已经稳定到可以进入 Desktop / UI 原型阶段。

当前结论：

```text
底层模块已具备进入 UI 原型阶段的基础条件，但仍不能视为发布就绪。
```

可以进入 UI 原型阶段的含义：

- 可以创建 `AtomPix.Desktop` 工程。
- 可以开始 Avalonia / AtomUI 组合根、页面和 ViewModel 原型。
- UI 层应通过 Workflows 和 DI 组合根调用底层能力。

不代表：

- 可以发布正式版本。
- 可以承诺跨平台真实安装包稳定。
- 可以承诺 NativeAOT 成功。
- 可以承诺真实大图、权限异常、跨平台路径都已充分验证。

## 2. 工程结构检查

当前项目：

```text
src/AtomPix.Core
src/AtomPix.Imaging.Abstractions
src/AtomPix.Infrastructure
src/AtomPix.Imaging.Magick
src/AtomPix.Workflows
```

测试项目：

```text
tests/AtomPix.Core.Tests
tests/AtomPix.Imaging.Abstractions.Tests
tests/AtomPix.Infrastructure.Tests
tests/AtomPix.Imaging.Magick.Tests
tests/AtomPix.Workflows.Tests
```

依赖方向：

```text
AtomPix.Core

AtomPix.Imaging.Abstractions
  -> AtomPix.Core

AtomPix.Infrastructure
  -> AtomPix.Core

AtomPix.Imaging.Magick
  -> AtomPix.Imaging.Abstractions
  -> AtomPix.Core

AtomPix.Workflows
  -> AtomPix.Core
  -> AtomPix.Imaging.Abstractions
```

检查结论：

- Core 未引用外层模块。
- Workflows 未引用 Infrastructure 或 Imaging.Magick 具体实现。
- Infrastructure 未引用 Workflows、Imaging.Magick 或 UI。
- Imaging.Abstractions 未引用具体图片库或 UI。
- Imaging.Magick 未引用 Workflows、Infrastructure 或 UI。
- 当前没有 Desktop 项目，因此没有 UI 类型泄漏到内层。

## 3. 包引用检查

当前使用 Central Package Management：

```text
Directory.Packages.props
```

当前第三方依赖：

```text
Magick.NET-Q8-AnyCPU
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.DependencyInjection.Abstractions
Microsoft.NET.Test.Sdk
xunit
xunit.runner.visualstudio
coverlet.collector
```

检查结论：

- Magick.NET 只被 `AtomPix.Imaging.Magick` 引用。
- DI Abstractions 只用于模块注册扩展。
- 完整 DI 容器只在 headless 测试中使用。
- 测试包只在测试项目中引用。

## 4. 当前 headless 能力基线

已覆盖能力：

- Core 模型不变量。
- 压缩、转换、输出策略模型。
- 设置、最近记录、任务、批量进度。
- 图片处理抽象契约。
- Magick 真实图片探测、预览、压缩、转换。
- 多帧/动画第一阶段拒绝处理。
- 输出路径解析、Skip、Overwrite、AutoRename。
- 同目录临时文件写入与半成品清理。
- 本地 JSON 存储、损坏文件、高版本设置、取消和临时文件清理。
- 批量部分成功、失败、取消和最终进度。
- 真实 DI 容器 headless 装配。

## 5. UI 阶段必须遵守的契约

Desktop / ViewModel 不能绕过以下契约：

- 路径使用 `LocalPath`，不长期传裸字符串。
- 用户可预期失败使用 `OperationResult`。
- 错误展示根据 `AtomPixErrorCode` 做本地化映射。
- 输出目录、命名和覆盖行为通过 `OutputPolicy` 表达。
- 图片处理只能通过 `IImageProcessor` 间接使用。
- Desktop 组合根负责组合 Infrastructure、Imaging.Magick 和 Workflows。
- ViewModel 不直接 new `MagickImageProcessor`、`JsonAppSettingsStore` 等具体实现。

## 6. 进入 UI 前仍未覆盖的风险（历史快照）

以下内容是 2026-06-26 进入 UI 前的风险快照，不代表当前实现缺口；各项现状和未完成发布门禁以 `testing-and-release.md` 最新快照为准：

- 真实大图、超大图和高内存压力；当时目标资源契约已经冻结为 `512 MiB / 32768 px / 128 MP`，但代码尚未实现轻量探测、进程级按需上限和对应压力验证。
- 大批量处理性能和取消响应延迟。
- Windows/macOS/Linux 真实路径、权限和文件锁差异。
- 只读目录、网络盘、移动盘、云同步目录。
- 原生 Magick.NET 包体积和平台特定包策略。
- 真实 Desktop single-file/self-contained 发布。
- NativeAOT 与 Avalonia、AtomUI、Magick.NET 的实际兼容性。
- 本地诊断与隐私目标契约已经冻结，但当时代码仍缺少日志作用域、滚动 Provider、脱敏过滤和故障隔离测试。

## 7. 进入 UI 前的发布验证口径（历史快照）

该基线时尚无可执行 Desktop 项目，因此当次发布验证只覆盖类库 publish，不覆盖真实应用产物。当前 Desktop 发布证据见 `testing-and-release.md` 最新快照。

当时执行的验证：

```text
dotnet restore AtomPix.slnx
dotnet build AtomPix.slnx --no-restore /p:UseSharedCompilation=false
dotnet test AtomPix.slnx --no-build --no-restore
dotnet publish src/AtomPix.Workflows/AtomPix.Workflows.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/AtomPix.Imaging.Magick/AtomPix.Imaging.Magick.csproj -c Release -r win-x64 --self-contained true
```

后续进入 Desktop 阶段要求新增（当前已纳入发布验证）：

```text
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

NativeAOT 仍为实验验证项：

```text
dotnet publish src/AtomPix.Desktop/AtomPix.Desktop.csproj -c Release -r win-x64 /p:PublishAot=true
```

## 8. 进入 UI 阶段判定

满足条件：

- 全量 restore/build/test 通过。
- Workflows 和 Imaging.Magick Release publish 通过。
- 质量闸门文档已记录剩余风险。
- UI 阶段遵守第 5 节契约。

结论：

```text
通过上述验证后，可以进入 Desktop / UI 原型阶段。
```
## 9. Round 3 第四阶段验收收口记录

本轮扩大真实 DI 组合验收，所有场景均通过 `ServiceCollection` 注册后从容器解析 workflow，不手工 new workflow：

- 默认转换流程。
- 默认压缩流程。
- 批量压缩流程。
- 批量转换流程。
- 最近记录写入。

当前 headless 测试基线：

```text
Core: 55
Imaging.Abstractions: 17
Infrastructure: 35
Imaging.Magick: 45
Workflows: 85
Total: 237
```

本轮发布验证命令：

```text
dotnet build AtomPix.slnx --no-restore /p:UseSharedCompilation=false
dotnet test AtomPix.slnx --no-build --no-restore
dotnet publish src/AtomPix.Workflows/AtomPix.Workflows.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Workflows/win-x64 /p:UseSharedCompilation=false
dotnet publish src/AtomPix.Imaging.Magick/AtomPix.Imaging.Magick.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/AtomPix.Imaging.Magick/win-x64 /p:UseSharedCompilation=false
```

验证结论：

- 全量构建通过，0 warning / 0 error。
- 全量测试通过，237 passed。
- `AtomPix.Workflows` Release self-contained publish 通过。
- `AtomPix.Imaging.Magick` Release self-contained publish 通过。
- 并行 publish 曾触发共享 Core Release 输出锁竞争；顺序执行并关闭共享编译后通过。后续发布脚本应避免并行发布共享项目输出。

可视化输出产物：

```text
tests/TestOutputs/Images/
```

这些图片用于人工查看转换和压缩效果，不作为最终压缩质量验收。发布前仍需使用真实用户图片集、大图和跨平台样本做人工和自动化补充验收。

阶段性判断：

```text
当前 headless 底层能力可以阶段性收口，可以进入 Desktop / UI 原型讨论和实现。
```

边界：

```text
这不是正式发布就绪结论。Desktop、安装包、NativeAOT、真实跨平台权限、大图性能和真实用户图片质量验收仍未完成。
```

