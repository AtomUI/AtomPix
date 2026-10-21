# AtomPix.Infrastructure × AtomUI.City 审计

> 设计状态：Design Frozen；交付状态：边界已验证，迁移后需回归
>
> 结论：全部 City 能力均不采用（`APX-CITY-BND-003`）

Infrastructure 实现设置、遗留 Recent 文件、文件系统和日志等现有技术端口。它提供持久化事实，但不拥有运行时共享投影；投影和写入编排属于 Workflows。当前产品不再加载或写入 Recent，已有文件不主动删除。

- 不引用 City.State：Store 不发布应用状态，也不持有订阅者。
- 不引用 City.Core：Infrastructure 没有独立模块生命周期；Desktop Host 通过现有 DI 扩展注册实现。
- 不引用 EventBus/Data/Security/PluginSystem：本地文件与日志需求不匹配这些能力。
- 不引用 Localization：技术层返回稳定异常/错误信息，用户文案由 Desktop 映射。
- 不引用 City 工程包：该库保持普通 net10.0 项目。

Store 的原子写、损坏数据处理、缺失文件默认值和日志行为保持现状。State 施工只能在持久化成功后发布，不得要求 Infrastructure 反向调用 Workflows 或 Desktop。
