# AtomPix × City 工程、CI 与验收设计

> 状态：设计冻结 / City Blocked

## 1. 版本与子模块

- City 位于 `external/AtomUI.City`，`.gitmodules` 跟踪 `develop`，但 AtomPix 永远提交精确 gitlink SHA。
- 当前审计 SHA 为 `e058270f469f23453c7df59029784cb4b53fe414`；该 SHA 未通过 City 公共 API 发布门，不能进入生产引用。
- CI/Release checkout 必须递归获取子模块，并在构建前验证目录、SHA 和 clean 状态。
- 更新 City 只能形成独立提交，同时更新主矩阵、兼容性证据和漏洞审计；不得静默前移 gitlink。

## 2. 项目引用与构建

解锁后，Workflows 只引用 City.State；Desktop 引用 City Core/Mvvm/Routing/Presentation/Localization/Build/Generators 的源项目。Core、Imaging、Infrastructure 不引用 City。测试项目只按被测合同增加必要引用。

AtomPix 使用 net10.0、Avalonia 12.1.1；City 当前自身使用 SDK 10.0.300、Avalonia 12.0.4。必须用真实 AtomPix 图完成 restore/build/test/publish，并检查 `project.assets.json` 中只有一个可接受的 Avalonia 版本族且无 downgrade warning。

## 3. 自动门禁

迁移前和迁移后均运行：

```powershell
./eng/verify.ps1 -PublishCurrentPlatform
./eng/smoke-test.ps1 -RuntimeIdentifier win-x64
./eng/windows-ui-automation.ps1
./eng/windows-published-functional-test.ps1
```

`verify.ps1` 默认不得使用 `-SkipStress` 或 `-SkipAudit`。CI 发布矩阵继续覆盖 `win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`；Windows 增加发布产物 UI 自动化，其余平台至少完成还原、Release 构建、测试、发布和启动烟测。

新增静态边界门：

- Core/Imaging/Infrastructure 不含 City ProjectReference 或 namespace。
- 生产 Desktop 不含旧 ObservableObject/Command/NavigationCoordinator。
- 不存在未登记的裸 `_ =`、`async void` 或直接根容器构建。
- 用户可见中文全部来自本地化目录；测试 fixture 与开发注释不计入。

## 4. City 前置门

每次更新 City SHA 必须先执行 City 官方 Release gate，而不是只挑选通过的项目测试。当前 SHA 的以下事实必须保留在证据中：

- Build/Public API gate：失败，原因是 `IUiCommandFactory` 仍位于 `PublicAPI.Unshipped.txt`。
- TemplateSmoke：三项失败，其中本地包顺序和隔离 SDK 继承是环境问题，不替代公共 API 阻塞结论。
- NuGet restore 对 `Microsoft.AspNetCore.SignalR.Common 10.0.8` 报告高危漏洞；AtomPix 不采用 City.Data，但仍须证明最终运行时依赖图没有带入该包。

## 5. 完成定义

- 主矩阵无 `阻`、无开放审计项，采用项全部变为 `已`。
- AtomPix 全量功能、Headless UI、压力、漏洞、裁剪发布和启动烟测通过。
- Windows 发布功能脚本的全部 case 通过；跨平台发布矩阵通过。
- 架构、模块、测试、发布和文档一致性快照更新为实际代码事实。
- 所有旧基础设施删除均有替代合同和测试证据，不保留双栈。
