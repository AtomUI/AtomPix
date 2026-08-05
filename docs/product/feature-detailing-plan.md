# AtomPix 功能细化讨论计划

> 文档状态：讨论过程基线
>
> 基线时间：2026-06-25
>
> 文档用途：记录需求与架构细化的讨论顺序，作为后续协商细节时的依据；本文档不是代码实现方案。

## 1. 目标

本文档用于约定 AtomPix 在进入代码实现前，需求和架构细节应按什么顺序继续细化。

当前已有文档已经明确：

- 第一阶段 MVP 功能范围：`docs/product/mvp-scope.md`
- 顶层架构和模块边界：`docs/architecture/overview.md`
- 模块职责和依赖方向：`docs/modules/overview.md`

后续讨论不应直接跳到 UI 或具体图片库调用细节，而应先把稳定的业务语言、图片处理契约和用户流程定清楚。

## 2. 推荐讨论顺序

推荐按以下顺序展开：

```text
1. Core 业务模型
2. Imaging.Abstractions 图片处理契约
3. Workflows 用户流程
4. Infrastructure 存储与系统能力
5. Imaging.Magick 实现细节
6. 测试与发布策略
7. Desktop 信息架构和交互
```

## 3. 顺序理由

`Core` 决定 AtomPix 的业务语言，例如压缩策略、转换策略、任务状态、授权权益和错误模型。

`Imaging.Abstractions` 决定图片引擎如何被调用，以及图片处理能力如何被 Workflows 消费。

`Workflows` 决定用户动作如何串联 Core 规则、图片处理契约和外部存储能力。

`Infrastructure` 决定设置、订阅状态、额度、日志、文件系统等外部能力如何落地。

`Imaging.Magick` 是第一阶段图片引擎实现，应被抽象契约约束，而不是反向污染契约和业务层。

`Desktop` 应该最后讨论。Core、Imaging.Abstractions、Workflows、Infrastructure 和 Imaging.Magick 都可以先以 headless 方式实现和测试，不需要等待 UI 设计。

## 4. 第一阶段细化主题

第一轮优先细化 `Core` 业务模型，包含：

```text
1. CompressionProfile
2. ConversionProfile
3. OutputPolicy
4. ImageJob / BatchJob
5. Subscription / Feature Access
6. AppSettings / OperationResult / Error
```

需要回答的问题：

- 压缩模式有哪些？
- 自定义压缩参数有哪些？
- 输出文件如何命名？
- 同名文件如何处理？
- 任务有哪些状态？
- 批量任务如何表达？
- 设置项默认值是什么？
- 错误如何统一表达？
- 订阅和权益如何占位但不过度复杂化？

## 5. 第二阶段细化主题

第二轮细化 `Imaging.Abstractions` 图片处理契约，包含：

```text
IImageProcessor
ImageProbeResult
ImagePreviewRequest
ImagePreviewResult
ImageCompressRequest
ImageCompressResult
ImageConvertRequest
ImageConvertResult
ImageFormatKind
AtomPixError
```

需要回答的问题：

- 图片处理接口是否足够覆盖 MVP？
- 预览结果使用 `byte[]`、`Stream` 还是其他结构？
- 是否允许返回 Avalonia 类型？
- 是否需要进度回调？
- 是否支持取消？
- 如何表达输入格式不支持？
- 如何表达压缩前后文件大小？

## 6. 第三阶段细化主题

第三轮细化 `Workflows` 用户流程，包含：

```text
OpenImageWorkflow
CreatePreviewWorkflow
CompressImageWorkflow
BatchCompressWorkflow
ConvertImageWorkflow
BatchConvertWorkflow
LoadSettingsWorkflow
SaveSettingsWorkflow
```

需要回答的问题：

- 每个流程输入什么？
- 每个流程输出什么？
- 权益检查在哪里发生？
- 第一阶段是否需要使用额度？结论：不需要，后续如引入再单独设计。
- 失败如何返回？
- 批量任务如何统计？
- 是否允许部分成功？

## 7. 后续细化主题

`Infrastructure`：

- 设置文件格式。
- 订阅状态本地保存。
- 使用额度本地保存。
- 应用数据目录。
- 临时目录。
- 日志策略。

`Desktop`：

- 页面结构。
- 导航结构。
- ViewModel 状态。
- AtomUI 控件选择。
- 拖拽和文件选择器。
- 批量任务表格。

`Imaging.Magick`：

- Magick.NET 包选择。
- 格式映射。
- 压缩参数映射。
- 异常映射。
- 大图处理策略。
- 暂不开放 PDF / EPS / PS / 视频等外部依赖格式。

`测试与发布`：

- 单元测试边界。
- 图片处理样本集。
- 批量任务测试。
- 跨平台发布方式。
- self-contained 发布验证。

## 8. 文档维护规则

- 本文档只记录讨论顺序和待细化主题，不承载最终功能细节。
- 某个主题讨论完成后，应把最终结论落入对应正式文档。
- 产品范围变化更新 `docs/product/mvp-scope.md`。
- 架构边界变化更新 `docs/architecture/overview.md` 和对应模块文档。
- 具体模块职责变化更新 `docs/modules/**/overview.md`。