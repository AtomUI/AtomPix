# AtomPix 诊断与本地日志设计

> 文档状态：Headless 基础设施与 Desktop 全局错误边界均已实现；真实故障注入和发布隐私复核仍是验收门禁
>
> 范围：Desktop、Workflows、Imaging.Magick、Infrastructure 的诊断协作；不改变 Core 业务状态机

## 1. 目标与边界

第一阶段日志只用于用户设备上的故障定位，不属于产品遥测：

- 默认只写入本地应用数据目录，不上传，不建立远程账号或设备画像。
- 业务成功、失败、取消仍以 Core / Workflow 正式结果为准；日志写入失败不能改变任务结果。
- Core 业务实体和值对象不依赖日志抽象，不在状态机迁移方法中记录技术日志。
- UI 不展示底层异常、调用栈或日志原文；只有需要定位的未预期错误才显示可复制诊断编号。
- 默认禁止记录用户图片的完整路径、文件名、图片内容和元数据。

## 2. 关联标识

诊断链路使用四类标识，各自职责不能混用：

| 标识 | 创建时机 | 作用域 | 是否展示给用户 |
| --- | --- | --- | --- |
| `SessionId` | 每次应用进程启动 | 当前进程 | 否 |
| `OperationId` | 每次用户命令或公开 Workflow 调用开始 | 一次打开、处理、保存或批量操作 | 否 |
| `JobId` / `BatchId` | Core Job 创建后 | 正式任务 | 否 |
| `DiagnosticId` | 需要用户反馈的未预期错误最终落盘时 | 一次具体异常 | 是 |

`OperationId` 使用不可预测的 128 位值。Desktop 发起用户命令时先建立诊断作用域，同一异步调用链中的 Workflow、Imaging 和 Infrastructure 继承该值；Headless 直接调用 Workflow 且没有外层作用域时，由 Workflow 入口创建。

创建 Core Job 后，现有 `JobId` / `BatchId` 加入同一作用域。批量单项还记录 `ItemIndex` 和子 `JobId`，但不为每条进度消息创建新 OperationId。

`DiagnosticId` 使用适合复制的 `APX-` 前缀和 12 位大写十六进制随机值，例如 `APX-A7F3C9214D08`。日志事件同时保存完整 OperationId 和 DiagnosticId。未预期错误转换为 `AtomPixError` 时，可在 `Details["DiagnosticId"]` 携带该值；普通校验、受支持的失败、Skip 和用户取消不要求生成或展示诊断编号。

## 3. 模块职责

```text
Desktop
  -> 建立用户命令作用域、配置全局错误边界、展示/复制 DiagnosticId
Workflows
  -> 建立 Headless 兜底作用域、记录用例开始/终态、加入 JobId/BatchId
Imaging.Magick
  -> 记录引擎阶段和引擎异常的已脱敏诊断
Infrastructure
  -> 提供本地日志 Provider、滚动、保留、清理和隐私过滤
Core / Imaging.Abstractions
  -> 不依赖日志框架，不承载日志写入
```

Workflows、Imaging.Magick、Infrastructure 和 Desktop 使用 `Microsoft.Extensions.Logging` 抽象；具体文件 Provider 只在 Infrastructure 实现，并由 Desktop 或 Headless 组合根完成注册。不得在 Core 增加 `ILogger`，也不得为了日志修改 Core Job 状态迁移。

具体日志 Provider 可以在实现阶段选择或替换，只要满足本文的结构化字段、滚动、隐私过滤和故障隔离约束。

## 4. 记录边界与级别

发布构建默认最低级别为 `Information`；`Debug` 只用于开发构建，不进入 MVP 用户设置。

| 事件 | 级别 | 规则 |
| --- | --- | --- |
| 应用启动、正常退出 | Information | 记录版本、平台和 SessionId。 |
| Workflow 开始、成功、失败、取消 | Information | 记录 OperationId、类型、耗时和正式终态。 |
| Job / Batch 创建与最终终态 | Information | 只记录关键迁移，不记录每次属性读取。 |
| 已知且可恢复的 IO、格式、资源或单项处理失败 | Warning | 记录稳定错误码和恢复类别，不附加重复异常。 |
| 用户输入校验、正常 Skip | Information | 只在用例边界记录摘要，避免逐字段噪声。 |
| 用户主动取消 | Information | 不使用 Warning/Error。 |
| 未预期异常、全局未处理异常 | Error | 生成 DiagnosticId，保留脱敏后的异常类型、消息和调用栈。 |

批量任务不记录每次 UI 进度投影，也不为每个成功项写一条 Information。默认记录批次开始、结束、汇总以及失败/取消单项；否则大批量任务会产生无界日志。

同一异常只能有一个“完整异常所有者”：捕获并转换原始异常的最内侧边界负责记录一次脱敏异常；外层只记录带相同 OperationId、DiagnosticId 和错误码的终态事件，不重复附加调用栈。全局错误边界只记录尚未被正式边界处理的异常。

## 5. 结构化字段

日志采用 UTF-8 JSON Lines。公共字段至少包括：

```text
TimestampUtc
Level
EventId
EventName
SessionId
OperationId
WorkflowName
Outcome
DurationMs
JobId / BatchId / ItemIndex（存在时）
ErrorCode / ErrorCategory（存在时）
DiagnosticId（存在时）
AppVersion
OperatingSystem
ProcessArchitecture
ImageEngineVersion
```

图片诊断可以记录与内容无关的技术事实：格式、文件字节数、逻辑宽高、帧数、是否透明、处理模式以及资源限制种类。字段不可用时保持缺失，不补写虚假的零值。

禁止通过自由文本拼接已存在的结构化字段。EventName 和字段名保持稳定，作为日志筛选和自动化测试依据；最终用户文案仍由 Desktop 根据 `AtomPixErrorCode` 本地化。

## 6. 路径与隐私保护

产品构建默认禁止写入：

- 输入、输出、缓存或设置文件的完整路径和文件名。
- 图片字节、缩略图、EXIF/XMP、ICC 内容或其他用户元数据。
- 用户自定义输出命名格式、剪贴板内容、授权令牌或账号凭据。
- 可跨会话追踪用户或设备的持久标识。

需要关联同一路径时记录 `PathToken`：使用每次启动随机生成、只存在内存中的密钥，对规范化路径计算带密钥摘要并截断为至少 12 个十六进制字符。同一路径只在当前 Session 内得到相同 Token；不同 Session 不能据此关联用户文件。扩展名、文件大小和图片尺寸可以作为独立结构化字段记录，文件名本身不记录。

`AtomPixError.Details` 是进程内业务上下文，可能为了恢复动作暂时携带真实路径；把 Error 投影到日志前必须经过统一隐私过滤，不能直接序列化整个 Details。

异常消息本身也可能包含路径。日志实现不得直接写入原始 `Exception.ToString()`；应分别记录异常类型、经过路径清理的消息和经过路径清理的调用栈。过滤器先替换当前作用域中已知的输入/输出路径，再对残留绝对路径形式做防御性清理。

第一阶段不提供“关闭脱敏”开关。以后如增加用户主动导出的诊断包，必须单独设计预览、确认和二次脱敏流程，不能复用本地日志写入作为自动上传授权。

## 7. 本地存储与生命周期

默认位置为平台应用数据目录下的私有日志目录：

```text
AppData/AtomPix/logs/
```

具体平台根目录继续由 `IAppPathProvider` 解析。日志按日期和文件大小滚动：

- 单个文件最多 `10 MiB`。
- 最多保留最近 `7` 天。
- 日志目录总量最多 `50 MiB`，达到上限时优先清理最旧文件。
- 启动时和滚动后执行尽力而为的清理；清理失败不阻断应用启动。

日志写入、flush、滚动或清理失败都不能让图片任务失败、改变 Job 状态或覆盖原始业务错误。日志组件不得尝试通过自身再次记录日志故障，避免递归失败；开发环境可以退化到调试输出，产品运行时静默失去该条诊断。

日志目录不保存图片临时文件，Magick 私有像素缓存也不能写入该目录。

## 8. Desktop 呈现

- 已知错误继续显示本地化原因和恢复动作，通常不显示诊断编号。
- `AtomPixErrorCategory.Unexpected`、`AtomPixErrorCode.Unknown` 或 Desktop 全局错误边界显示通用错误文案和可复制 DiagnosticId。
- UI 不显示 OperationId、原始异常消息、调用栈或日志原文。
- 复制诊断编号只复制 `APX-...` 值，不附带路径、图片信息或剪贴板中的其他内容。
- 第一阶段不提供日志浏览器、自动上传或遥测开关。

## 9. 验收要求

实现时至少验证：

1. 一次 Desktop 命令到 Workflow、Imaging、Infrastructure 的日志共享同一 OperationId。
2. Headless 直接调用 Workflow 时自动建立 OperationId。
3. Job 创建后日志同时包含 JobId；批量包含 BatchId、失败项索引和子 JobId。
4. 用户取消、正常 Skip 和校验失败不记录为 Error，也不生成诊断编号。
5. 未预期异常只记录一次完整脱敏调用栈，UI DiagnosticId 可以定位对应事件。
6. 输入路径、输出路径、文件名、异常消息和 `AtomPixError.Details` 中的路径不会以明文进入日志。
7. 同一路径在同一 Session 的 PathToken 一致，跨 Session 不一致。
8. 日志目录满足单文件、保留天数和总量上限。
9. 日志目录无权限、磁盘已满、写入失败和清理失败均不改变正式 Workflow 结果。
10. Core 和 Imaging.Abstractions 的公共依赖中不存在日志框架。

当前代码已经实现 `Microsoft.Extensions.Logging` 接入、Headless Workflow OperationId、Job/Batch 终态关联、Magick 内层异常继承、本地 JSON Lines 滚动 Provider、会话级 PathToken、异常路径清理与日志故障隔离。仍待 Desktop 实现用户命令外层作用域、全局未处理异常边界和 DiagnosticId 复制交互；发布前还需扩大真实磁盘满、长期保留与多进程写入压力验证。
