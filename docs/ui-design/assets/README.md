# UI 设计图图片资产

本目录只存放 `docs/ui-design` SVG 设计图的摄影源素材和品牌源副本，不进入 `AtomPix.Desktop` 生产资产。正式交付的 SVG 已把所需二进制资源内嵌到文件本体，因此本目录不是 SVG 预览依赖。

- `atompix-brand-64.png`：标题栏和图标轨使用的品牌预览副本，来源于生产资产 `AtomPix-64.png`。
- `atompix-brand-128.png`：“关于 AtomPix”使用的品牌预览副本，来源于生产资产 `AtomPix-128.png`。
- `wvrede-fog-10401662_1920.jpg`：Browser 主图、Crop 主图和选中缩略图。
- `kodl68-forest-10394495_1920.jpg`、`suju-foto-nature-10402711_1920.jpg`、`wolfgang_hasselmann-desert-10407209_1920.jpg`：走廊基础缩略图。
- `sergei_spas-dew-10415661_1920.jpg`、`georg_wietschorke-swallow-10423828_1920.jpg`、`ahmetyuksek-autumn-bend-10069119_1920.jpg`：走廊扩展缩略图。

上述 JPG 由用户从 `D:\111` 指定并复制到本目录，源目录不构成构建或预览依赖。品牌源副本与现有生产 PNG 保持一致，不形成新的 Logo 方案。修改源素材后必须重新生成对应 SVG 的内嵌 `symbol`，并把 SVG 复制到不含本目录的临时位置执行独立渲染检查；任何 `href="assets/..."` 或向上跨目录图片引用都不允许进入正式设计图。
