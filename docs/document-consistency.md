# AtomPix 文档一致性规则

> 文档状态：现行维护基线
>
> 基线时间：2026-10-21
>
> City 迁移设计：Design Frozen
>
> City 迁移交付：Implementation In Progress；Release Blocked

本文区分产品规范、目标设计、当前代码事实和历史验证，避免旧快照或讨论记录被当作现行施工合同。

## 1. 权威顺序

同一问题出现冲突时，按以下顺序定位需要修正的文档：

1. 产品范围与用户可见行为：`product/mvp-scope.md`。
2. 分层、依赖和模块边界：`architecture/overview.md` 与 `modules/**/overview.md`。
3. Workflow/Core 状态编排：`modules/workflows/job-state-orchestration.md`。
4. Desktop 交互、页面流转和状态保留：`modules/desktop/interaction-state-design.md`。
5. Desktop 视觉结构：`ui-design/README.md` 及其标为当前基线的 SVG。
6. AtomUI 控件、ImageGallery 和发布合规边界：`modules/desktop/atomui-component-mapping.md`、`modules/desktop/atomui-labs-imagegallery-migration.md`、`implementation/release-compliance.md`。
7. City 迁移：`iterations/features/with-city-decision-register.md` 是决策/交付索引；主矩阵是仪表盘；各 `with-city-*.md` 是具体施工合同。
8. 实施与测试文档：记录顺序、当前差距和历史/新验证，不得反向改变前述产品或模块合同。

桌面上的 `AtomPix迁移问题记录.md` 是讨论审计轨迹，不是仓库权威源。讨论结论只有写回上述正式文档、通过一致性检查并登记 Decision ID 后，才能指导施工。

同级正式文档不得长期冲突；权威顺序用于判断修改方向，不是保留矛盾的理由。

## 2. 时间与状态语义

- 正式设计/目标合同：已经批准，但代码可能尚未实现。
- 当前代码事实：描述仓库此刻实现，必须与 commit 和验证日期绑定。
- 历史验证：只证明指定日期/commit 的命令结果，后续不得称为“当前”。
- 讨论记录/后续备忘：不进入 MVP 或 Release Gate，除非正式写回。

City 迁移使用两个正交状态轴：

| 轴 | 合法状态 | 含义 |
| --- | --- | --- |
| Design | `Discussion` / `Decisions Approved` / `Writeback Validation Pending` / `Design Frozen` | 决策是否完整写回并通过一致性审计 |
| Delivery | `Not Started` / `In Progress` / `Blocked` / `Verified` | 实现和验收事实 |

`Design Frozen` 不表示已经实现；`Blocked` 也不表示设计未决定。只有正式文档写回、链接/术语/状态检查和当次可复现基线完成后，才可把本轮 Design 改为 Frozen。任何新的产品/框架决策都会重新打开相关 Decision ID 的设计审计。

## 3. 当前产品与模块摘要

- 四项单张：压缩、转换、调整尺寸、裁剪。
- 三项批量：压缩、转换、调整尺寸；无批量裁剪。
- 图片走廊同时是浏览集合和批量输入；Crop 仍只处理当前项。
- Browser 默认全宽；选择工具后与右侧面板普通两列布局。
- Settings 是普通页面，临时替换 presentation；Browser workspace、ActiveTool 和草稿保留，返回时重建 View/visual tree/activation scope。
- Recent 不属于当前产品或运行时：Home 不展示，Host 不加载，成功打开后不写入；兼容字段往返且不主动删除历史文件。
- Store 是设置持久化事实源；City.State 是 transient 只读运行时投影。

模块引用方向保持：

```text
Core <- Imaging.Abstractions <- Imaging.Magick
Core <- Infrastructure
Core + Imaging.Abstractions <- Workflows
上述模块 <- Desktop 组合根
```

City 迁移后只有 Workflows 直接采用 State，Desktop 采用 Core/Mvvm/Routing/Presentation/Localization 并间接观察 State；Core、Imaging、Infrastructure 禁止 City 依赖。

## 4. City 迁移治理

- 高层矩阵只做仪表盘，不证明单元已经可施工。
- 每项有意义的决定都在 `with-city-decision-register.md` 中具有稳定 Decision ID。
- `Verified` 必须指向实际测试/报告/命令结果；含“目标：”的占位不是证据。
- `Blocked` 必须指明 blocker owner 和解除条件。
- 复杂生命周期、失败和 ownership 写在专项文档，登记表只链接，不复制第二套合同。
- City fixed SHA 或 public API 改变时，必须重新核对相关 Decision ID、真实 AtomPix dependency graph 和发布门。

当前明确的 City blocker：Public API release gate、`CommandExecutionDomain`、Routing-Presentation coordinator/receipt/conditional restore、ViewModel composition release validation。当前明确的 AtomPix blocker：同 SHA runtime/build 消费、published functional 可复现性/CI 接入、Recent 清理和发布合规闭环。

## 5. 维护检查

每次范围或契约变更至少运行：

```powershell
rg "BatchCrop|批量裁剪|四类.*批量" docs
rg "RecentItemsState|Home.*订阅.*Recent|最近项目投影" docs/iterations/features
rg "原样保留.*activation|复用.*activation scope" docs/iterations/features docs/modules/desktop
rg "CancelPrevious|LatestWins" docs/iterations/features/with-city-mvvm.md
rg "UseAlibabaSansFont|AtomUI.Fonts.AlibabaSans|Svg.Custom" docs src Directory.Packages.props
rg "Design Frozen|Writeback Validation Pending|Implementation Blocked" docs/iterations/features docs/document-consistency.md
rg "审-[HML]" docs/iterations/features/with-city*
rg "AtomUI.City" src tests
```

命中不自动等于错误：历史段落和明确禁止项可以存在，但必须标出时间/规范层级。另需检查：

- Markdown 相对链接与 `ui-design/*.svg` 文件存在。
- Decision ID 唯一；专项文档引用能解析。
- Core/Imaging/Infrastructure 的 City 引用为零。
- 当前生产 Host/Home/打开来源路径不存在 Recent 运行时读写。
- 发布依赖/字体/SVG/Native 组件与 `release-compliance.md` 一致。

## 6. 历史代码与验证快照

### 2026-08-26（历史，不是当前证据）

当时生产组合根已贯通四项单张、三项批量、OutputPolicy、ImageGallery 与新 Shell。记录为 Debug 全解决方案 `343 passed`，构建 `0` warning / `0` error。该数字必须保留为历史，不得用于证明 2026-10-21 的分支。

### 2026-10-19（City 迁移前审计记录，待同命令复核）

迁移主矩阵曾记录 Release 功能/Headless `398 passed`、stress `4 passed`、漏洞审计无已知漏洞、`win-x64` Partial Trim publish 和启动 smoke 通过。该记录说明审计当时观察到的基线，不会因文档同步自动升级为当前证据。

## 7. 2026-10-21 写回验证快照

本轮基于 commit `397a6259f8cd5c2bc13ba5660d1fcb647f0c29fc` 的 docs-only working tree 完成设计写回。验证结果追加如下，不覆盖第 6 节历史数字：

```text
Markdown relative links: passed
Decision register: 17 unique IDs; status/evidence checks passed
Core/Imaging/Infrastructure City reference scan: passed, zero hits
git diff --check: passed（仅输出 Git 的 LF/CRLF working-copy 提示）

Release functional + Headless UI: 398 passed
  Core 45
  Imaging.Abstractions 18
  Infrastructure 37
  Workflows 154
  Desktop state/interaction 73
  Desktop Headless UI 16
  Imaging.Magick 55
Stress: 4 passed（workflow/imaging/diagnostics 3 + UI virtualization 1）
NuGet vulnerability audit: 14/14 projects reported no vulnerable packages
win-x64 Partial Trim publish/package: passed
Published EXE 5-second startup smoke: passed
Published-window/icon-rail/settings UI Automation smoke: passed
```

执行命令：

```powershell
./eng/verify.ps1 -PublishCurrentPlatform
./eng/smoke-test.ps1 -RuntimeIdentifier win-x64
./eng/windows-ui-automation.ps1
```

第一次在受限沙箱内执行 `verify.ps1` 时，Avalonia BuildServices 写入用户 `AppData` 被拒绝，Release build 因环境权限失败；同一命令随后在允许该正式构建路径的环境中完整重跑并返回成功。Restore/build 曾输出 7 条 `NU1900`，表示当时无法取得在线漏洞 metadata；后续独立 `dotnet list ... --vulnerable` 阶段成功访问配置源并确认 14 个项目均未报告已知漏洞。win-x64 publish 仍输出既有 COM、AtomUI、Avalonia、DynamicData 和 ReactiveUI Partial Trim 分析警告，这些警告已在发布基线中保留，不被隐藏，也不等同于 NativeAOT 已获正式支持。

`eng/windows-published-functional-test.ps1` 本轮没有执行：`APX-CITY-TEST-001` 已明确记录它依赖未受控历史 case root、隔离路径和 CI 接线尚未修复。现有启动/UIA smoke 不能冒充完整 published functional 验收。该缺口以及 City、Build package 和发布合规 blocker 使 Delivery 保持 `Implementation Blocked`；它们不妨碍已批准且完成写回验证的设计进入 `Design Frozen`。

## 8. 2026-10-21 迁移施工验证快照

本节晚于第 7 节，是当前实现证据；第 7 节保留为施工前历史，不能覆盖本节结论。

```text
City fixed SHA: 9097d9d0d8244a864e5b7944ef2bf7ab2be70ae3
AtomUI.City.Build: 1.0.0-atompix.9097d9d
Build package SHA-256: 97087E411C1FFDB7B264A45D057333BF67EC031FD409B885D2DBA6753FD2FB66

AtomPix Release build: passed, 0 warning / 0 error
City Release DLL closure hash check: passed
Non-stress tests: 409 passed
  Core 45
  Imaging.Abstractions 18
  Infrastructure 39
  Workflows 158
  Desktop 76
  Desktop Headless UI 18
  Imaging.Magick 55
Stress: 4 passed（业务/成像/诊断 3 + UI virtualization 1）
NuGet vulnerability audit: 14/14 projects reported no vulnerable packages
git diff --check: passed（仅 LF/CRLF working-copy 提示）

win-x64 Partial Trim single-file publish/package: passed
Published EXE SHA-256: 0f00ccf57796cca0d58cee9b88553121e1b3b21852ac2bef68b1c9fb760e14e6
ZIP SHA-256: 26e8265ab580e4b60ea3369def07e1946732e3f0f7a7a0af6e049bca7fa6a544
Published window/icon rail/settings UIA smoke: passed
Published functional matrix: 9/9 passed
Fixture manifest SHA-256: 139898ad1b240e2066da58e1ed73fc102c307307be1896200bc71db59c31432f
Functional report: .artifacts/published-functional/20261021T083530097Z-19232-018467b1/published-functional-results.json
```

施工中发现 Windows `Environment.GetFolderPath(LocalApplicationData)` 不会可靠地随测试父进程的 `LOCALAPPDATA` 覆盖而隔离。AtomPix 因此新增显式绝对路径环境变量 `ATOMPIX_APPDATA_DIRECTORY` 与 `ATOMPIX_TEMP_DIRECTORY`；发布功能测试为每个 case 提供独立目录并验证设置跨重启恢复。fixture manifest 校验源文件 SHA-256，所有 case 复制到唯一 run root，正常关闭失败会使测试失败而非用强制 kill 掩盖。

本快照关闭 City framework、State、Routing-Presentation、Build provenance 和 Windows published functional blocker。`APX-CITY-LOC-001` 已进入 `In Progress`：八个 `zh-CN` catalog、generated registrar、City Dispatcher presentation bridge 和 Headless 资源查询已验证，但生产文案/key/订阅迁移、硬编码扫描 allowlist 与最新发布 locpack 复验尚未完成。整体迁移仍是 `Implementation In Progress / Release Blocked`：`APX-CITY-MVVM-002` 尚需 ownership map 最终收口，`APX-CITY-LIC-001` 仍受 AtomUI 传递 Alibaba Sans/Svg.Custom、替代授权、Magick.Native 材料和 SBOM/Notice/source gate 阻塞。
