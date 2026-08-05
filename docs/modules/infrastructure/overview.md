# AtomPix.Infrastructure 模块设计

> 文档状态：架构讨论基线
>
> 基线时间：2026-06-25

## 1. 模块定位

`AtomPix.Infrastructure` 是外部能力实现层。

它实现 Core 定义的存储、配置、授权、额度、文件系统、路径解析和日志等端口，让 Core 和 Workflows 不需要关心外部世界的技术细节。

## 2. 允许包含

- 配置文件读写。
- 订阅状态本地存储。
- 使用额度本地存储。
- 本地历史记录或缓存。
- 文件系统访问封装。
- 应用数据目录、临时目录解析。
- 日志实现和日志初始化。
- 后续账号/订阅服务的 HTTP 客户端实现。
- 面向 DI 的服务注册扩展。

## 3. 禁止包含

- Avalonia、AtomUI、ViewModel 或 UI 状态。
- Magick.NET 或具体图片处理逻辑。
- 压缩、转换、批处理等用户流程编排。
- 授权权益判断规则本身。
- 任务状态流转规则。

## 4. 推荐目录

```text
src/AtomPix.Infrastructure/
  AtomPix.Infrastructure.csproj
  Configuration/
  Licensing/
  
  FileSystem/
  Logging/
  Paths/
  Storage/
  DependencyInjection/
```

## 5. 首批实现

- `JsonAppSettingsStore`
- `LocalSubscriptionStore`
- `JsonRecentItemsStore`
- `AppPathProvider`
- `LocalFileSystemService`
- `InfrastructureServiceCollectionExtensions`

## 6. 设计约束

- Infrastructure 可以依赖 Core，因为它需要实现 Core 定义的端口。
- Core 不能依赖 Infrastructure。
- Infrastructure 不做业务判断，只负责外部能力落地。
- 敏感授权信息不能明文散落在普通配置文件中，具体方案后续单独设计。
- 存储格式属于实现细节，不能泄漏给 Workflows 或 Desktop。

## 7. 依赖规则

```text
AtomPix.Infrastructure
  -> AtomPix.Core
```

如后续引入日志、JSON、SQLite、HTTP 客户端等依赖，应只停留在 Infrastructure 内部或其公开注册扩展中。
## 8. 第一阶段基础设施能力基线

`AtomPix.Infrastructure` 提供外部世界的技术实现，但不承载用户流程和业务策略决策。

第一阶段重点能力：

```text
1. AppSettings 存储
2. SubscriptionState 存储
3. RecentItems 存储
4. 原子文件系统能力
5. 应用数据目录与临时目录
```

日志能力先预留，不做复杂设计。

### 8.1 AppSettings 存储

Core 定义设置存储端口：

```csharp
public interface IAppSettingsStore
{
    Task<OperationResult<AppSettings>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken);
}
```

Infrastructure 第一阶段实现：

```text
JsonAppSettingsStore
```

建议存储位置：

```text
AppData/AtomPix/settings.json
```

行为约定：

- 设置文件不存在：返回默认 `AppSettings`，算成功。
- 设置文件损坏：返回 `SettingsLoadFailed`，不偷偷覆盖用户文件。
- 设置保存失败：返回 `SettingsSaveFailed`。
- 订阅保存失败：返回 `SubscriptionSaveFailed`。
- 最近记录保存失败：返回 `RecentItemsSaveFailed`。

### 8.2 SubscriptionState 存储

Core 定义订阅状态存储端口：

```csharp
public interface ISubscriptionStore
{
    Task<OperationResult<SubscriptionState>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        SubscriptionState subscription,
        CancellationToken cancellationToken);
}
```

Infrastructure 第一阶段实现：

```text
LocalSubscriptionStore
```

建议存储位置：

```text
AppData/AtomPix/subscription.json
```

行为约定：

- 订阅文件不存在：返回 `Free` 状态，算成功。
- 订阅文件损坏：返回 `SubscriptionLoadFailed`，不静默降级为 Free。
- 第一阶段不做复杂加密和签名校验，只保留结构；后续接入支付、激活或联网校验时再增强。

### 8.3 RecentItems 存储

最近记录列表不放入 `AppSettings`，使用独立存储。

Core 可定义模型：

```csharp
public sealed record RecentItem(
    LocalPath Path,
    RecentItemKind Kind,
    DateTimeOffset OpenedAt);
```

```csharp
public enum RecentItemKind
{
    File,
    Directory
}
```

Core 定义端口：

```csharp
public interface IRecentItemsStore
{
    Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        IReadOnlyList<RecentItem> items,
        CancellationToken cancellationToken);
}
```

Infrastructure 第一阶段实现：

```text
JsonRecentItemsStore
```

建议存储位置：

```text
AppData/AtomPix/recent-items.json
```

行为约定：

- 最近记录文件不存在：返回空列表，算成功。
- 最近记录文件损坏：返回空列表，算成功。
- 最近记录不是关键业务数据，不能因为损坏阻断应用启动或图片处理流程。

### 8.4 文件系统能力边界

文件系统相关设计遵循：

```text
Workflows 决策
Infrastructure 执行
```

策略决策必须留在 Workflows：

- 是否允许覆盖。
- 是否跳过。
- 是否自动重命名。
- 最终 `OutputPath` 是什么。
- 什么时候调用图片处理写文件。

Infrastructure 只提供原子文件系统能力：

- 文件是否存在。
- 目录是否存在。
- 创建目录。
- 获取文件大小。
- 组合路径。
- 获取文件名和扩展名。
- 改变扩展名。
- 构造带索引的候选路径。

建议端口定义在 Core：

```csharp
public interface IFileSystemService
{
    bool FileExists(LocalPath path);

    bool DirectoryExists(LocalPath path);

    Task<OperationResult> CreateDirectoryAsync(
        LocalPath directory,
        CancellationToken cancellationToken);

    Task<OperationResult<long>> GetFileSizeAsync(
        LocalPath path,
        CancellationToken cancellationToken);

    LocalPath Combine(LocalPath directory, string fileName);

    string GetFileNameWithoutExtension(LocalPath path);

    string GetExtension(LocalPath path);

    LocalPath ChangeExtension(LocalPath path, string extension);

    LocalPath BuildIndexedPath(LocalPath basePath, int index);
}
```

Infrastructure 第一阶段实现：

```text
LocalFileSystemService
```

禁止事项：

- Infrastructure 不接收 `OverwritePolicy` 后再决定跳过、覆盖或自动重命名。
- Infrastructure 不判断某个功能是否允许保存文件。
- Infrastructure 不决定输出目录策略或文件命名策略。
- Infrastructure 不调用图片处理引擎。

### 8.5 输出路径解析流程

输出路径解析由 Workflows 完成。

典型流程：

```text
1. 根据 OutputLocationPolicy 得到输出目录。
2. 调用 IFileSystemService.CreateDirectoryAsync 准备目录。
3. 根据 OutputNamingPolicy 生成期望文件名。
4. 根据目标格式决定扩展名。
5. 组合 desiredPath。
6. 如果文件不存在，使用 desiredPath。
7. 如果文件存在且 OverwritePolicy = Skip，生成 Skipped 结果。
8. 如果文件存在且 OverwritePolicy = Overwrite，使用 desiredPath。
9. 如果文件存在且 OverwritePolicy = AutoRename，循环查找 _1、_2、_3 等候选路径。
10. 得到最终 OutputPath 后，调用 IImageProcessor。
```

只要 Workflows 调用图片处理写 `OutputPath`，就表示覆盖、跳过或自动重命名等策略决策已经完成。

图片内容写入由 `AtomPix.Imaging.Magick` 通过图片处理契约执行；Infrastructure 不负责编码图片内容。

### 8.6 应用路径提供

Core 定义应用路径端口：

```csharp
public interface IAppPathProvider
{
    LocalPath AppDataDirectory { get; }

    LocalPath TempDirectory { get; }
}
```

Infrastructure 第一阶段实现：

```text
AppPathProvider
```

路径策略：

- Windows：使用用户本地应用数据目录下的 `AtomPix`。
- macOS：使用用户 Application Support 下的 `AtomPix`。
- Linux：优先遵循 XDG 目录约定。

具体平台路径在实现阶段确定，文档只固定抽象边界。

### 8.7 推荐目录

```text
src/AtomPix.Infrastructure/
  Configuration/
    JsonAppSettingsStore.cs
  Subscriptions/
    LocalSubscriptionStore.cs
  RecentItems/
    JsonRecentItemsStore.cs
  FileSystem/
    LocalFileSystemService.cs
  Paths/
    AppPathProvider.cs
  DependencyInjection/
    ServiceCollectionExtensions.cs
```
## 9. Infrastructure 工业级硬化基线

`AtomPix.Infrastructure` 是外部世界能力实现层。它不做业务决策，但必须保证 IO、路径和本地存储行为稳定、可诊断、可测试。

### 9.1 构造与空参数

Infrastructure 中依赖端口的实现类必须在构造函数中拒绝 null 依赖，例如：

- `JsonAppSettingsStore` 必须要求有效的 `IAppPathProvider`。
- `LocalSubscriptionStore` 必须要求有效的 `IAppPathProvider`。
- `JsonRecentItemsStore` 必须要求有效的 `IAppPathProvider`。

保存方法必须拒绝 null 业务对象或集合，避免将非法状态序列化到磁盘。

### 9.2 JSON 存储写入策略

配置、订阅和最近记录等 JSON 文件写入必须遵循：

- 先写入同目录临时文件。
- 序列化和 flush 成功后，再替换目标文件。
- 失败或取消时清理临时文件。
- 不在加载失败时静默覆盖用户原文件。

该策略用于降低保存过程中进程退出或 IO 失败导致目标文件半写入的风险。

### 9.3 取消语义

Infrastructure 异步方法必须显式支持 `CancellationToken`：

- 已取消时返回 `OperationCanceled` 失败结果。
- 不让 `OperationCanceledException` 穿透到 Workflows 或 Desktop。
- 取消不是业务失败，也不能被映射为设置损坏或文件系统错误。

### 9.4 文件系统服务约束

`LocalFileSystemService` 只提供原子文件系统能力，不做覆盖、跳过或自动重命名决策。

路径辅助方法必须满足：

- `Combine(directory, fileName)` 中的 `fileName` 必须是单一路径段，不能是绝对路径，也不能包含目录分隔符。
- `ChangeExtension(path, extension)` 必须要求非空扩展名，并允许调用方传入 `webp` 或 `.webp`。
- `BuildIndexedPath(basePath, index)` 必须要求 `index > 0`，并要求 `basePath` 包含文件名。

### 9.5 应用路径策略

`AppPathProvider` 默认路径必须按平台处理：

- Windows 使用 LocalApplicationData 下的 `AtomPix`。
- macOS 使用 `~/Library/Application Support/AtomPix`。
- Linux 优先使用 `XDG_DATA_HOME/AtomPix`，否则使用 `~/.local/share/AtomPix`。

测试可通过构造函数注入临时目录，避免污染真实用户目录。

### 9.6 测试要求

`AtomPix.Infrastructure.Tests` 至少覆盖：

- 设置、订阅、最近记录的缺省加载、损坏文件加载和保存后读取。
- 取消 token 的返回语义。
- 文件系统路径组合、扩展名变更、索引路径生成的非法输入。
- 构造函数对 null 依赖的拒绝。
## 10. JSON 值对象序列化基线

Infrastructure 的 JSON 存储必须正确处理 Core 值对象。

当前已明确支持：

- `LocalPath` 序列化为路径字符串。
- 反序列化空路径或空白路径时失败。

所有 JSON 存储应使用统一的 `AtomPixJsonOptions`，避免不同存储对同一值对象产生不一致格式。
## 11. 文件系统错误语义补充

Infrastructure 只表达文件系统事实，不承担图片业务解释。

约束：

- `GetFileSizeAsync` 对不存在文件返回 `InputFileNotFound`。
- 文件存在但不是有效图片，不由 Infrastructure 判断，交给 Imaging 的 `ProbeAsync` 映射为 `InvalidImageFile`。
- Infrastructure 不把文件系统错误改写为压缩、转换或预览错误。
- Workflows 根据 Infrastructure 返回的文件系统结果决定是否继续流程。

## 12. 路径与文件名边界补充

`LocalFileSystemService` 提供跨平台保守的路径辅助能力。

约束：

- `Combine(directory, fileName)` 中的 `fileName` 必须是单一路径段。
- `fileName` 不能是空白、`.` 或 `..`。
- `fileName` 不能包含 `/` 或 `\`，不能是根路径或绝对路径。
- `ChangeExtension` 支持调用方传入 `webp` 或 `.webp`，并保留多点文件名主体。
- `BuildIndexedPath` 支持无扩展名文件和多点文件名，例如 `photo -> photo_1`，`archive.photo.jpg -> archive.photo_1.jpg`。
- `AppPathProvider` 默认路径必须以 `AtomPix` 作为应用目录名；测试中应通过构造函数注入临时目录。

这些规则只处理路径拼接和文件名候选生成，不决定覆盖、跳过或自动重命名策略。

## 13. 本地 JSON 存储版本与恢复策略补充

AtomPix 第一阶段本地 JSON 存储采用差异化容错策略。

### 13.1 settings.json

设置文件是影响处理结果的关键用户配置。

规则：

- 文件不存在：返回 `AppSettings.Default`，算成功。
- 文件损坏：返回 `SettingsLoadFailed`，不覆盖原文件。
- 文件 schema 高于当前 `AppSettings.CurrentSchemaVersion`：返回 `SettingsLoadFailed`，由 Desktop 提示需要升级软件。
- 保存失败：返回 `SettingsSaveFailed`。
- 保存使用同目录临时文件写入，成功后替换目标文件，失败后清理临时文件。

第一阶段不实现 `.bak` 备份系统，后续如果需要，可在 `JsonFileWriter` 层统一增加备份策略。

### 13.2 subscription.json

订阅状态属于商业化关键状态。

规则：

- 文件不存在：返回 `SubscriptionState.Free`，算成功。
- 文件损坏或内容违反 `SubscriptionState` 不变量：返回 `SubscriptionLoadFailed`。
- 不允许把损坏订阅文件静默降级为 Free，避免掩盖授权状态异常。
- 保存失败：返回 `SubscriptionSaveFailed`。

第一阶段不引入订阅文件 schema version；后续接入激活、签名或联网校验时再整体升级订阅存储模型。

### 13.3 recent-items.json

最近记录是非关键体验数据。

规则：

- 文件不存在：返回空列表成功。
- 文件损坏：返回空列表成功。
- 后续保存最近记录时允许覆盖损坏文件，从空列表恢复为正常文件。
- 保存失败：返回 `RecentItemsSaveFailed`。

### 13.4 临时文件清理

所有 JSON 保存都通过 `JsonFileWriter` 写入：

```text
1. 写入同目录临时文件。
2. 序列化和 flush 成功后替换目标文件。
3. 任一步失败时尽力删除临时文件。
```

临时文件清理失败不应覆盖原始保存错误。

## 14. Infrastructure DI 注册补充

`AtomPix.Infrastructure.DependencyInjection` 提供 `AddAtomPixInfrastructure()`。

默认注册：

- `IAppPathProvider -> AppPathProvider`
- `IAppSettingsStore -> JsonAppSettingsStore`
- `ISubscriptionStore -> LocalSubscriptionStore`
- `IRecentItemsStore -> JsonRecentItemsStore`
- `IFileSystemService -> LocalFileSystemService`

测试和 headless host 可以使用带路径参数的重载注入临时 `appdata` 和 `temp` 目录，避免污染真实用户目录。
