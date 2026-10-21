# AtomPix Imaging × AtomUI.City 审计

> 设计状态：Design Frozen；交付状态：边界已验证，迁移后需回归
>
> 结论：Imaging.Abstractions 与 Imaging.Magick 全部 City 能力均不采用（`APX-CITY-BND-002`）

Imaging.Abstractions 定义探测、编码和像素处理端口；Imaging.Magick 实现这些端口、安全提交文件并映射技术错误。两者不拥有应用 Host、导航、共享设置、用户主体或插件生命周期。

- Abstractions 继续只依赖 Core；任何 City 类型都会污染可替换后端合同。
- Magick 继续只依赖 Core 与 Imaging.Abstractions；编码并发、临时文件和提交恢复使用现有实现，不包装成 City.State/EventBus/Data。
- 进度、取消和错误继续通过标准 .NET 与 AtomPix 合同传递。
- 图像库异常不得直接本地化；Workflows 映射稳定错误，Desktop 最终选择中文资源。
- City Build/Generators 不参与图像后端构建，避免把 UI 发布约束引入可复用库。

验收包括项目引用边界、所有现有 Imaging 测试、故障注入、格式/透明度/尺寸语义和发布裁剪链路。City 迁移不得改变 Magick 输出质量或文件安全提交行为。
