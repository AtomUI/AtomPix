# AtomPix.Core × AtomUI.City 审计

> 设计状态：Design Frozen；交付状态：边界已验证，迁移后需回归
>
> 结论：全部 City 能力均不采用（`APX-CITY-BND-001`）

Core 拥有值对象、图片操作参数、输出规则、Job 业务状态、错误与结果模型。这些语义在没有 UI、Host、路由、网络和插件时仍完整成立。

- 不引用 City.Core：Core 不拥有应用 Host、模块启动或资源关闭事务。
- 不引用 City.State/EventBus：Job 状态由领域对象和 Workflow 调用驱动，不是跨应用共享状态或总线事件。
- 不引用 Mvvm/Routing/Presentation/Localization：Core 返回稳定错误语义，不返回用户文案和 UI 类型。
- 不引用 Data/Security/PluginSystem：当前产品没有对应领域合同。
- 不引用 Build/Generators/Testing/CLI/Templates：现有纯 .NET 项目和 xUnit 合同已足够。

验收通过项目引用图和源代码扫描保证 Core 没有 `AtomUI.City.*` 引用；现有 Core 测试必须原样通过。未来需求若引入新的跨前端领域能力，应先修改架构文档并重新打开矩阵单元，不能由 Desktop 反向渗透。
