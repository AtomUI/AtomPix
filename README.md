# AtomPix

English | [简体中文](./README.zh-CN.md)

[![CI](https://github.com/AtomUI/AtomPix/actions/workflows/ci.yml/badge.svg)](https://github.com/AtomUI/AtomPix/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/AtomUI/AtomPix)](https://github.com/AtomUI/AtomPix/releases)
[![UI](https://img.shields.io/badge/UI-Powered%20by%20AtomUI-1677ff)](https://github.com/AtomUI/AtomUI)
[![License: GPL v3 or later](https://img.shields.io/badge/License-GPL_v3_or_later-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)

AtomPix is a focused desktop tool for browsing and processing images locally.

Open individual images or an entire folder, inspect them in a fluid gallery, and compress, convert, resize, or crop without uploading your files to a remote service. All features in the current release are available without an account or subscription.

![AtomPix Home](docs/user-guide/home.png)

## Feature Overview

AtomPix keeps its workflow intentionally compact:

```text
AtomPix
├─ Open images or a folder
├─ Browse and inspect
├─ Compress
├─ Convert format
├─ Resize
├─ Crop
└─ Settings
```

Compression, format conversion, and resizing support both the current image and the complete gallery collection. Cropping is deliberately a single-image operation.

## Image Browser

The image browser is the main AtomPix workspace.

You can:

- Open one or multiple images with the system picker.
- Open a folder and browse the supported images in its current level.
- Append images from other locations to the current gallery.
- Select an image directly from the thumbnail strip.
- Fit the image to the available viewport, view it at actual size, and zoom in or out.
- Switch between browsing and processing without creating a separate batch task page.

The highlighted gallery item is the target of a single-image operation. For compression, conversion, and resizing, **Batch processing** freezes the whole gallery as one processing request.

## Compress Images

AtomPix compresses static JPEG, PNG, and WebP images without silently changing their pixel dimensions or file format.

Available modes include:

- Smart
- High quality
- Balanced
- Maximum compression
- Custom quality

JPEG and WebP use lossy quality settings. PNG uses lossless optimization. Metadata removal is controlled independently, while the resulting file is always retained when encoding succeeds—even when an already optimized source produces a larger output.

Both single-image and batch compression report the actual input and output sizes and describe the result as reduced, unchanged, or increased.

## Convert Formats

Convert supported static images to JPEG, PNG, or WebP. Lossy formats expose a quality control; PNG ignores that value.

When transparent pixels are converted to JPEG, AtomPix applies the configured background color instead of relying on an image-engine default. PNG and WebP preserve transparency when the target format supports it.

![Format Conversion](docs/user-guide/trans-format.png)

## Resize Images

Resize changes image dimensions without cropping or adding padding.

Two modes are available:

- **Pixels** — enter width and height, optionally preserve the original aspect ratio, and prevent smaller images from being enlarged.
- **Percentage** — scale both dimensions by the same percentage.

With aspect ratio preservation enabled, changing either dimension updates the other. When both pixel constraints are supplied, AtomPix fits the complete image inside the requested bounds. Batch resizing applies one shared rule to every image while calculating each final size from its own original dimensions.

## Crop Images

Crop keeps a rectangular region of the current image. Drag the selection and its handles on the canvas, or use exact width, height, X, and Y values.

AtomPix provides free-form cropping plus common `3:2`, `4:3`, `5:4`, and `1:1` aspect ratios. The crop rectangle remains inside the image and the output keeps the source format. Cropping is single-image only in the current release.

![Crop Image](docs/user-guide/crop.png)

## Output and Batch Processing

Every processing tool uses the same output model:

- Save to a subfolder beside each source image, the source folder itself, or a custom directory.
- Keep the original name, add a suffix, or use a custom naming format.
- Automatically rename, skip, or overwrite an unrelated existing output.
- Never overwrite an input image in place.

Batch compression, conversion, and resizing expose live item counts and progress. Failed or unfinished items can be prepared for another run without modifying the completed result history. Multi-image output names receive stable sequence numbers when needed.

## Settings

Settings are presented as a regular application page rather than a modal dialog. You can configure defaults for compression, conversion, output location, naming, conflict handling, transparency, and metadata behavior. New tasks capture the latest saved defaults; already running tasks keep their original settings snapshot.

![About AtomPix](docs/user-guide/about.png)

## Download

Download the latest package from [GitHub Releases](https://github.com/AtomUI/AtomPix/releases).

AtomPix publishes self-contained packages for:

- Windows x64
- macOS x64 and Apple Silicon
- Linux x64 and ARM64

The packages include the required .NET runtime, so installing the .NET SDK is not required for normal use.

## Build from Source

Building from source requires the .NET 10 SDK.

```powershell
dotnet restore AtomPix.slnx --configfile NuGet.config
dotnet build AtomPix.slnx -c Release --no-restore
dotnet run --project src/AtomPix.Desktop/AtomPix.Desktop.csproj
```

The desktop UI is built with Avalonia and AtomUI, the gallery is provided by AtomUI Labs ImageGallery, and image processing is powered by Magick.NET.

## Current Scope

AtomPix currently focuses on mainstream static-image workflows. Animated GIF, multi-frame WebP, TIFF editing, batch cropping, cloud processing, accounts, plugins, and full editor features such as layers, filters, drawing, and text are outside the current release scope.

## License

AtomPix is free software licensed under the [GNU General Public License, version 3 or (at your option) any later version](LICENSE). The SPDX license identifier is `GPL-3.0-or-later`.
