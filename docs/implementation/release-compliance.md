# AtomPix 发布依赖许可证与合规设计

> 设计状态：Design Frozen；交付状态：Implementation Blocked
>
> 适用范围：ZIP、DMG、AppImage、安装器、独立下载、Partial Trim/single-file 与实验 NativeAOT

本文记录工程合规决策，不替代针对具体发行地区的法律意见。判断对象是每次真实 publish 的最终依赖闭包和分发物，不是项目文件中的直接 PackageReference 清单。

## 1. 自有组件的替代授权

AtomUI.City、AtomUI OSS 和 AtomUI.Labs 由 AtomPix 权利主体本人或同一受控权利主体完整持有。各独立项目保持原有公开许可证和商业模式不变；AtomPix 每次正式发布实际使用的精确源码快照，另行按 `GPL-3.0-or-later` 向 AtomPix 及其下游接收者授权。

每次发布记录必须绑定：

- 组件名称、package version 和 commit SHA。
- 权利主体声明。
- 覆盖下游接收者的 `GPL-3.0-or-later` 替代授权文本。
- 与二进制精确对应的源码归档或稳定获取地址。

已随产品发布的 GPL 快照授权不得追溯撤销；未来未发布版本不自动受该授权影响。本方案不增加 AtomPix 专用链接例外，也不为这些自有组件分别建立 LGPL relink kit。`AtomUI.Labs.Controls.ImageGallery` 的精确发布快照遵守同一规则。

## 2. 当前必须清理的外部依赖

### 2.1 Alibaba Sans

在归档能够明确允许字体文件嵌入和再次分发的原始授权前，正式 AtomPix 发布闭包不得包含 `AtomUI.Fonts.AlibabaSans` 或其字体文件。Desktop 改用许可证边界明确的 SIL OFL 字体或系统字体。营销材料中的“免费商用”不能代替可再分发授权证据。

2026-10-21 施工事实：AtomPix 已删除直接 `AtomUI.Fonts.AlibabaSans` PackageReference 和 `UseAlibabaSansFont()` 调用，运行时回到系统字体；但 `AtomUI.Desktop.Controls 6.0.8` 的 NuGet 依赖图仍传递引用该字体包。因此该 blocker 尚未关闭，必须在 AtomUI 包边界移除依赖并用真实 publish closure 复核。

### 2.2 Svg.Custom / MS-PL

AtomUI 当前经 `Svg.Controls.Avalonia -> Svg.Model/Svg.SceneGraph -> Svg.Custom 4.5.0` 引入 MS-PL 组件。AtomUI 必须移除或替换该依赖；AtomPix 不增加永久 GPL 链接例外，也不把某次 Trim 碰巧删除程序集作为长期合规策略。

2026-10-21 复核仍能在还原依赖图中观察到该传递链，故保持阻塞。

只有真实 publish closure 扫描证明该组件不存在，且 AtomUI 的替代实现/依赖测试通过，blocker 才能关闭。

## 3. Magick.NET 与本机组件

保留 `Magick.NET-Q8-AnyCPU`，不因许可证治理重写图片引擎。正式发布必须：

- 附带对应版本完整、未裁剪的 `Notice.txt`。
- 固定 Magick.NET、Magick.Native 以及 Notice 列出的静态组件准确版本。
- 归档或提供稳定取得对应源码的路径。
- 提供重建本机库的说明、工具链/参数和补丁清单。
- 在隔离环境实际验证源码、说明和 checksum 可用，不能只保存失效链接。

libde265、libheif、glib、pango、liblqr 等义务按精确 Native 包的 Notice 和对应许可履行。

## 4. 其他第三方依赖

Avalonia、SkiaSharp、HarfBuzzSharp、ReactiveUI、Microsoft.Extensions、ANGLE 及其他实际进入发布物的依赖统一进入 third-party notices。每项记录名称、版本、来源、许可证 SPDX/原文、copyright/Notice、是否含本机或静态链接组件、对应源码义务和最终产物位置。

不得假定 NuGet cache 中存在 LICENSE/NOTICE 就等于它们已进入最终 ZIP、安装器或下载页面。

## 5. 自动 Release Gate

对每个 RID 和发布形式执行：

```text
restore lock/assets
-> publish
-> 扫描 managed/native/bundled closure
-> 与批准的 dependency/license inventory 对账
-> 生成 SBOM + third-party notices + source-offer/source-index
-> 检查最终归档/安装器/下载附属物
-> 拒绝未知、不兼容或材料缺失的产物
```

强制失败条件：

- 未知许可证、GPL 不兼容组件或缺少权利主体授权。
- 包版本/commit 与授权或对应源码不一致。
- 必需 LICENSE/NOTICE/copyright 缺失。
- 本机/静态组件没有对应源码和重建说明。
- ZIP、DMG、AppImage、安装器或 raw download 的材料不等价。
- SBOM 与实际 publish closure 不一致。

报告至少包含 RID、publish mode、入口/归档 hash、dependency closure hash、SBOM hash、notices hash、source archive/index hash 和 gate 结果。产物、报告和授权记录按 Release 保留。

## 6. Trim、single-file 与 NativeAOT

Partial Trim/single-file 继续作为正式模式，但每次必须从最终 bundle/解包闭包扫描，不能仅依据未裁剪的 `project.assets.json`。即使代码被裁剪，许可证材料也按实际进入产物的组件及其分发条件生成；禁止用 Trim 掩盖设计上仍存在的不兼容依赖。

NativeAOT 在同一许可证/源码闭环、完整功能回归和可重建验证通过前保持实验状态，不进入正式 Release 必选矩阵，也不得覆盖正式单文件产物。

## 7. 当前 blocker 与验收

| Blocker | Owner | 关闭证据 |
| --- | --- | --- |
| 自有组件精确快照替代授权未归档 | AtomPix/权利主体 | 授权文本、commit/package、源码归档和下游措辞审计 |
| Alibaba Sans 再分发授权不明 | AtomPix/AtomUI | 从发布闭包移除，或归档明确原始授权 |
| `Svg.Custom` MS-PL 依赖 | AtomUI | 替换依赖且真实 publish closure 不再包含它 |
| Magick.Native 材料未形成发布闭环 | AtomPix | Notice、精确源码、rebuild 文档和隔离验证 |
| SBOM/Notice/source gate 未自动化 | AtomPix | 五 RID、所有分发形式的自动门禁与保留报告 |

以上 blocker 全部关闭后，`APX-CITY-LIC-001` 才能转为 `Verified`。
