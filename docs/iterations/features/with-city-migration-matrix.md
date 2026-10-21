# AtomPix × AtomUI.City 迁移主矩阵

> 设计状态：Design Frozen
>
> 交付状态：Implementation In Progress；Release Blocked
>
> 实施分支：`feature/with-city`
>
> 当前固定 City SHA：`9097d9d0d8244a864e5b7944ef2bf7ab2be70ae3`（City Release/Public API/package gate 已通过）

本文是 City 迁移的总览仪表盘和导航索引。完整施工合同、blocker 和证据以[逐决策登记表](with-city-decision-register.md)及专项文档为准；产品行为仍以 `docs/product/` 为准，模块边界仍以 `docs/architecture/` 与 `docs/modules/` 为准。

## 1. 状态与治理规则

| 标记 | 含义 |
| --- | --- |
| `禁` | 架构边界禁止该依赖 |
| `否` | 产品不需要，明确不采用 |
| `间` | 只通过 AtomPix facade/上层组合间接消费 |
| `采` | 设计决定采用，尚未完成实现验证 |
| `阻` | 已决定采用，但存在明确前置 blocker |
| `已` | 实现与验收证据均完成 |

`阻` 是交付状态，不代替采用范围、生命周期或失败语义。复杂合同写在专项设计；一项只有在登记表中具有 owner、City contract、implementation、失败语义和 acceptance evidence 后才可施工。高层单字符不构成证据。

City 能力跟随事实、状态和生命周期 owner，而不是最先展示它的 Desktop。Store 是设置的持久化事实源，City.State 是只读运行时投影。图片处理进度和取消保持调用级，不升级为全局 State。AtomPix 不修改固定 City SHA，也不在本仓库实现框架级临时替代。

## 2. 运行时能力仪表盘

| AtomPix \ City | Core | Mvvm | Routing | Presentation | State | EventBus | Localization |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Core | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 |
| Imaging.Abstractions | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 |
| Imaging.Magick | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 |
| Infrastructure | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 | 禁 |
| Workflows | 否 | 否 | 否 | 否 | 采 | 否 | 否 |
| Desktop | 已 | 采 | 已 | 已 | 间 | 否 | 采 |

解释：

- Workflows 只为 `AppSettingsState` 直接采用 City.State；Recent 不迁移。
- Desktop 通过 AtomPix 只读 facade 间接观察 State，不获得 writer。
- Mvvm 的 City 前置能力已经验证；AtomPix 逐类型 ownership map 仍在收口，因此保持“采”。
- Routing/Presentation 已使用通用 coordinator、commit receipt 和安全条件恢复能力。
- EventBus 不采用；显式命令、只读 State 和应用服务足够。

## 3. 其他能力仪表盘

| 能力 | 状态 | 决议 |
| --- | --- | --- |
| Data / Security / PluginSystem | 否 | 当前无远程数据管线、主体权限或插件产品合同 |
| Build | 已 | 同 SHA 本地 `AtomUI.City.Build` 包、包哈希与 gitlink provenance gate 已通过 |
| Generators | 已 | 只经 Build 包 analyzer assets 消费，不直接 ProjectReference；当前构建链验证通过 |
| Testing | 否 | City 测试证明框架；AtomPix 保留自己的 Headless/EXE/发布功能证据 |
| CLI / Templates | 否 | 不匹配当前产品交付 |

`Engineering` 不再伪装成 AtomPix 运行时模块；仓库级 Build/CI/Release 状态独立列在本节和决策登记表。

## 4. 已批准的产品与边界决策

- Settings 替换 presentation，不终止 Browser workspace；重建 View/visual tree/activation scope，不回放用户操作。
- 启动失败按 L1/L2/L3 分级；设置读取失败是可恢复 `Faulted`，结构错误才触发 Host 回滚。
- City 新增最小 `CommandExecutionDomain`，不预先构建通用调度 policy 系统。
- 当前版本移除 Recent UI 与运行时读写；兼容字段往返且不主动删除用户历史文件。
- Settings 保存采用 Store-first、State-second，City snapshot 为 transient。
- 先在 City 增加 Routing-Presentation 技术协调/条件恢复，再实现 AtomPix 业务导航层。
- City composition 先完成发布级验证；AtomPix 再按逐 ViewModel ownership map 迁移。
- runtime 使用固定 SHA ProjectReference；Build/Generators 使用同 SHA 本地 Build 包。
- 发布功能测试必须从受控 fixture 自给自足、隔离运行并进入 CI/Release。
- 最终 publish closure 必须完成替代授权、第三方许可证、Notice、源码和 SBOM 闭环。

## 5. 当前剩余项

| Blocker | Owner | 解除条件 |
| --- | --- | --- |
| AtomPix MVVM ownership 未完全收口 | AtomPix | Home/Settings EntryOwned 与导航意图/前台任务职责拆分完成并补齐逐类型证据 |
| Localization 内容迁移与发布闭环未完成 | AtomPix | 已接通的 8 catalog/registrar/UI bridge 继续完成生产 key/订阅迁移、硬编码扫描 allowlist 与 trim/single-file locpack 验收 |
| 发布许可证闭环未完成 | AtomPix/AtomUI | 字体与 Svg 清理、替代授权、Notice/源码/SBOM gate 通过 |

## 6. 解锁和施工顺序

1. 已完成：City 四项 framework blocker、Release gate 和固定 SHA。
2. 已完成：同 SHA Build 包、provenance、State/Host、Routing/Presentation 与发布功能门。
3. 当前：收口 AtomPix MVVM ownership 和导航/前台任务职责。
4. 当前：八个 Localization catalog、generated registrar 与 UI bridge 已接通；继续迁移生产文案和订阅，并完成发布闭环。
5. Release 前：关闭字体/Svg、Notice/源码/SBOM 合规 blocker；只有真实证据存在时才转为 `已`。

## 7. 专项文档

- [Desktop 总体迁移](with-city.md)
- [逐决策登记表](with-city-decision-register.md)
- [Routing-Presentation 协调](with-city-routing-presentation.md)
- [MVVM、命令与生命周期](with-city-mvvm.md)
- [Workflows State](with-city-workflows.md)
- [Build、Generators 与 Localization](with-city-build-generators-localization.md)
- [Core 边界审计](with-city-core.md)
- [Imaging 边界审计](with-city-imaging.md)
- [Infrastructure 边界审计](with-city-infrastructure.md)
- [工程、CI 与验收](with-city-engineering.md)
- [发布依赖许可证与合规](../../implementation/release-compliance.md)
