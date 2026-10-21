# AtomPix × City 工程、CI 与验收设计

> 设计状态：Design Frozen
>
> 交付状态：Verified（工程与发布功能门）；整体 Release 仍受许可证约束

## 1. 版本、子模块与包

- City 位于 `external/AtomUI.City`；AtomPix 提交精确 gitlink SHA，不在迁移中漂移跟踪分支。
- 当前固定 SHA 为 `c2e4176159b9b441af2d7ce065d5acde6a888543`；City Release build、2252 项测试（沙箱网络失败项在允许网络的同配置下定向复验）、Build package provenance、composition、`CommandExecutionDomain` 与 Routing-Presentation gate 已通过。聚合 `check-release.sh` 的 format 首关仍受既有非本次文件格式基线阻断，不把它误记为全绿。
- CI/Release 递归获取 submodule，并验证目录、SHA 和 clean 状态。
- runtime 模块使用 `ProjectReference`；构建期只消费同 SHA 生成的本地 `AtomUI.City.Build` 包。完整合同见[接线设计](with-city-build-generators-localization.md)。
- 每次 City 更新形成独立 AtomPix 提交，同时更新决策登记表、兼容证据、依赖与许可证审计。

## 2. City 前置门

新 SHA 必须同时满足：

1. City solution、模块测试、package/release/Public API gate 全部通过。
2. `IUiCommandFactory` 位于合法 shipped baseline 或由正式替代合同取代。
3. `CommandExecutionDomain` 完成竞态、取消、activation drain 和 Public API 验证。
4. Routing-Presentation coordinator、commit receipt 和条件恢复完成故障注入与 release validation。
5. ViewModel composition 从 `Release Validation Pending` 提升为 `Release Verified`。
6. 同 SHA `AtomUI.City.Build` 包可由干净环境确定性生成并被 AtomPix 消费。
7. AtomPix 的 Avalonia `12.1.1`、`net10.0` 真实依赖图无 downgrade、重复资产或不可接受漏洞。

City gate 仍记录 `Microsoft.AspNetCore.SignalR.Common 10.0.8` 的上游告警；AtomPix 2026-10-21 的 14 项目漏洞审计未报告易受攻击包，且该 Data 依赖不在产品 publish closure 中。后续 City SHA 更新仍须重新审计。

## 3. 自动门禁

迁移前后执行：

```powershell
./eng/verify.ps1 -PublishCurrentPlatform
./eng/smoke-test.ps1 -RuntimeIdentifier win-x64
./eng/windows-ui-automation.ps1
./eng/windows-published-functional-test.ps1
```

`verify.ps1` 默认不得跳过 stress 或 vulnerability audit。Windows published functional 已使用受控 fixture manifest、显式 `ATOMPIX_APPDATA_DIRECTORY`/`ATOMPIX_TEMP_DIRECTORY`、唯一 run root、正常关闭和 JSON/trace 证据，并接入 CI/Release artifact。CI 发布矩阵继续覆盖 `win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`；Windows 额外运行发布产物 UI 自动化与完整功能矩阵。

2026-10-21 的最终 Localization 发布候选来自 commit `2ef33a6d0f063bb62c4d7ca179f46b8158a99a80`：win-x64 Partial Trim single-file EXE SHA-256 为 `6DF8DB3B4B0ECF987D1FEDB3955786B69C77DB7E8C7AD55B2FF9FE6E17553A51`，ZIP SHA-256 为 `793F99FDF4C7684303F0FCF776B147F69516374BA64DF928491C361F95CF54DB`；启动 smoke、发布窗口 UIA 与 GUI 功能矩阵 `9/9` 通过，功能报告为 `.artifacts/published-functional/20261021T093545336Z-16700-68239d5f/published-functional-results.json`。

静态边界门：

- Core/Imaging/Infrastructure 不含 City reference 或 namespace。
- Desktop 不含旧 ObservableObject/Command/NavigationCoordinator 双栈。
- 无未登记的裸 `_ =`、非事件型 `async void` 或直接根容器构建。
- 生产 Host/Home/打开来源路径不加载或写入 Recent。
- 用户可见文本全部来自 catalog；允许清单只覆盖测试、诊断和注释。
- Decision ID 唯一，`Verified` 有实际证据，`Blocked` 有 owner 和解除条件。

## 4. 可复现的 Windows 发布功能测试

`tests/TestAssets/Images` 是唯一受版本控制的不可变源图片集；新增 manifest 记录 SHA-256、格式、尺寸和透明度要求。默认脚本必须自给自足：

```text
校验 source manifest
-> 创建唯一 run root
-> 每个 case 复制独立 input
-> 每个 case 建立独立 output / LOCALAPPDATA / results
-> 启动真实发布版 AtomPix
-> 驱动 UI 并验证输出、输入 hash、设置和关闭
-> 生成 JSON / trace；失败保留截图和现场
```

要求：

- 删除对日期型 `.artifacts/trim-validation/gui-cases-*` 的默认依赖；`-CaseRoot` 只作诊断覆盖。
- 所有设置路径从 `$session.AppData` 计算，不读取测试父进程的 LocalApplicationData。
- 每 case 开始时 output 为空；验证精确输出数量、格式、尺寸、可解码性、临时文件清理和输入 hash。
- 场景 ID、配置和断言必须一致；使用语言无关的稳定 `AutomationId`，并另行断言固定 `zh-CN` 的 Name。
- 优先正常关闭并等待终态；强制 kill 仅为 cleanup，关闭失败写入结果。
- 本地默认和 CI/Release 都运行全部 case；`-Only` 仅用于诊断。
- publish 和基础进程 smoke 后运行；失败使 job 失败。JSON/trace 始终上传，失败时增加截图、隔离设置、输入输出和进程诊断。

迁移代表场景包括 Settings 往返 workspace 保留、Routing 成功而 Presentation 失败后的安全恢复、Settings State `Faulted` 恢复、close denial 和四项单张/三项批量真实输出。可可靠故障注入的内部阶段留在 Headless 测试，不把所有内部错误塞进 UIA。

## 5. 发布合规

第三方许可证、替代授权、Notice、对应源码、SBOM 和最终产物闭包遵守[发布依赖许可证与合规设计](../../implementation/release-compliance.md)。字体和 `Svg.Custom` 清理、Magick.Native Notice/源码材料、Partial Trim 产物扫描均为 Release blocker；NativeAOT 保持实验状态。

## 6. 完成定义

- 决策登记表无 `Blocked`/`Not Started`，采用项均有具体实现与证据。
- City 新固定 SHA 通过全部前置门；AtomPix runtime/build 双通道来源一致。
- AtomPix 全量功能、Headless UI、stress、vulnerability、发布功能、五平台 publish/smoke 通过。
- 发布闭包无未知/不兼容许可证，Notice/SBOM/源码访问随每种分发形式提供。
- 架构、模块、测试、发布和文档一致性快照更新为实际代码事实；历史数字不冒充当前证据。
- 旧基础设施删除且无长期双栈；每个删除项有替代合同和测试。
