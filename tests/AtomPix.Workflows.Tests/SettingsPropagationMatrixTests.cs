namespace AtomPix.Workflows.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;
using AtomPix.Infrastructure.Configuration;
using AtomPix.Infrastructure.FileSystem;
using AtomPix.Infrastructure.Paths;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;
using ImageMagick;

public sealed class SettingsPropagationMatrixTests : IDisposable
{
    private readonly string _root;
    private readonly JsonAppSettingsStore _settingsStore;
    private readonly ImageWorkflowServices _services;

    public SettingsPropagationMatrixTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AtomPixSettingsMatrix", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var paths = new AppPathProvider(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        _settingsStore = new JsonAppSettingsStore(paths);
        _services = new ImageWorkflowServices(new MagickImageProcessor(), new LocalFileSystemService());
    }

    public static IEnumerable<object[]> ConfigurationFeatureMatrix()
    {
        foreach (var setting in Enum.GetValues<SettingDimension>())
        foreach (var feature in Enum.GetValues<ImageFeature>())
            yield return [setting, feature];
    }

    [Theory]
    [MemberData(nameof(ConfigurationFeatureMatrix))]
    public async Task Saved_setting_is_used_by_new_feature_job_and_real_output(
        SettingDimension setting,
        ImageFeature feature)
    {
        var caseRoot = Path.Combine(_root, $"{setting}-{feature}");
        Directory.CreateDirectory(caseRoot);
        var savedSettings = CreateSettings(setting, caseRoot);
        var save = await new SaveSettingsWorkflow(_settingsStore).ExecuteAsync(
            new SaveSettingsRequest(savedSettings),
            CancellationToken.None);
        Assert.True(save.Succeeded);

        var input = CreateInput(caseRoot, setting, feature);
        PrecreateCollisionWhenRequired(setting, savedSettings, input, feature);

        var job = await ExecuteFeatureAsync(feature, input);

        if (setting == SettingDimension.OverwritePolicy)
        {
            Assert.Equal(ImageJobStatus.Skipped, job.Status);
            return;
        }

        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.NotNull(job.OutputPath);
        Assert.True(File.Exists(job.OutputPath!.Value.Value));
        using var output = new MagickImage(job.OutputPath.Value.Value);
        Assert.True(output.Width > 0);
        Assert.True(output.Height > 0);

        AssertOutputLocationAndName(setting, savedSettings, input, job.OutputPath.Value.Value);
        AssertFeatureOutput(setting, feature, savedSettings, output, job.OutputPath.Value.Value);
    }

    [Theory]
    [InlineData(ImageFeature.Compress)]
    [InlineData(ImageFeature.Convert)]
    [InlineData(ImageFeature.Resize)]
    public async Task Saved_defaults_are_used_by_three_item_batch(ImageFeature feature)
    {
        var caseRoot = Path.Combine(_root, $"batch-{feature}");
        var outputRoot = Path.Combine(caseRoot, "configured-batch-output");
        Directory.CreateDirectory(caseRoot);
        var metadata = MetadataPolicy.Remove;
        var settings = new AppSettings(
            new CompressionProfile(CompressionMode.Custom, new ImageQuality(39), metadata),
            new ConversionProfile(
                OutputImageFormat.Jpeg,
                new ImageQuality(43),
                metadata,
                new TransparencyPolicy(RgbColor.Parse("#336699"))),
            new SameFormatEncodingPolicy(new ImageQuality(73), metadata),
            new OutputPolicy(
                new OutputLocationPolicy(OutputLocationMode.CustomDirectory, outputRoot, null),
                new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{index}_{name}_batch"),
                OverwritePolicy.AutoRename),
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems);
        var save = await new SaveSettingsWorkflow(_settingsStore).ExecuteAsync(
            new SaveSettingsRequest(settings),
            CancellationToken.None);
        Assert.True(save.Succeeded);
        var inputs = CreateBatchInputs(caseRoot, 3);

        IReadOnlyList<ImageJobResult> jobs = feature switch
        {
            ImageFeature.Compress => (await new BatchCompressWithDefaultSettingsWorkflow(
                    _settingsStore,
                    new BatchCompressWorkflow(_services))
                .ExecuteAsync(new BatchCompressWithDefaultSettingsRequest(inputs), CancellationToken.None))
                .Value!.Result.ItemResults.Select(item => item.JobResult).ToArray(),
            ImageFeature.Convert => (await new BatchConvertWithDefaultSettingsWorkflow(
                    _settingsStore,
                    new BatchConvertWorkflow(_services))
                .ExecuteAsync(new BatchConvertWithDefaultSettingsRequest(inputs), CancellationToken.None))
                .Value!.Result.ItemResults.Select(item => item.JobResult).ToArray(),
            ImageFeature.Resize => (await new BatchResizeWithDefaultSettingsWorkflow(
                    _settingsStore,
                    new BatchResizeWorkflow(_services))
                .ExecuteAsync(
                    new BatchResizeWithDefaultSettingsRequest(
                        inputs,
                        new PixelResizePolicy(40, 30, maintainAspectRatio: false)),
                    CancellationToken.None))
                .Value!.Result.ItemResults.Select(item => item.JobResult).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
        };

        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.Equal(ImageJobStatus.Succeeded, job.Status);
            Assert.NotNull(job.OutputPath);
            Assert.Equal(Path.GetFullPath(outputRoot), Path.GetFullPath(Path.GetDirectoryName(job.OutputPath!.Value.Value)!));
            Assert.Contains("_batch", Path.GetFileNameWithoutExtension(job.OutputPath.Value.Value), StringComparison.Ordinal);
            Assert.True(File.Exists(job.OutputPath.Value.Value));
        });
    }

    [Fact]
    public async Task Saving_settings_during_single_job_does_not_mutate_active_request()
    {
        var caseRoot = Path.Combine(_root, "concurrency-single");
        Directory.CreateDirectory(caseRoot);
        var oldSettings = CreateSnapshotSettings(caseRoot, "old");
        var newSettings = CreateSnapshotSettings(caseRoot, "new");
        await _settingsStore.SaveAsync(oldSettings, CancellationToken.None);
        var input = CreateBatchInputs(caseRoot, 1).Single();
        var processor = new BlockingImageProcessor(new MagickImageProcessor());
        var workflow = new CompressWithDefaultSettingsWorkflow(
            _settingsStore,
            new CompressImageWorkflow(new ImageWorkflowServices(processor, new LocalFileSystemService())));

        var active = workflow.ExecuteAsync(new CompressWithDefaultSettingsRequest(input), CancellationToken.None);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _settingsStore.SaveAsync(newSettings, CancellationToken.None);
        processor.Release();
        var oldResult = await active;

        Assert.True(oldResult.Succeeded);
        Assert.Equal(
            Path.GetFullPath(oldSettings.DefaultOutputPolicy.LocationPolicy.CustomDirectory!),
            Path.GetFullPath(Path.GetDirectoryName(oldResult.Value!.Result.JobResult.OutputPath!.Value.Value)!));

        var next = await workflow.ExecuteAsync(new CompressWithDefaultSettingsRequest(input), CancellationToken.None);
        Assert.True(next.Succeeded);
        Assert.Equal(
            Path.GetFullPath(newSettings.DefaultOutputPolicy.LocationPolicy.CustomDirectory!),
            Path.GetFullPath(Path.GetDirectoryName(next.Value!.Result.JobResult.OutputPath!.Value.Value)!));
    }

    [Fact]
    public async Task Saving_settings_during_batch_does_not_mutate_remaining_items()
    {
        var caseRoot = Path.Combine(_root, "concurrency-batch");
        Directory.CreateDirectory(caseRoot);
        var oldSettings = CreateSnapshotSettings(caseRoot, "old");
        var newSettings = CreateSnapshotSettings(caseRoot, "new");
        await _settingsStore.SaveAsync(oldSettings, CancellationToken.None);
        var inputs = CreateBatchInputs(caseRoot, 3);
        var processor = new BlockingImageProcessor(new MagickImageProcessor());
        var workflow = new BatchCompressWithDefaultSettingsWorkflow(
            _settingsStore,
            new BatchCompressWorkflow(new ImageWorkflowServices(processor, new LocalFileSystemService())));

        var active = workflow.ExecuteAsync(new BatchCompressWithDefaultSettingsRequest(inputs), CancellationToken.None);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _settingsStore.SaveAsync(newSettings, CancellationToken.None);
        processor.Release();
        var result = await active;

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.Result.ItemResults.Count);
        Assert.All(result.Value.Result.ItemResults, item =>
        {
            Assert.Equal(ImageJobStatus.Succeeded, item.JobResult.Status);
            Assert.Equal(
                Path.GetFullPath(oldSettings.DefaultOutputPolicy.LocationPolicy.CustomDirectory!),
                Path.GetFullPath(Path.GetDirectoryName(item.JobResult.OutputPath!.Value.Value)!));
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A failed assertion may leave a decoder handle alive briefly on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort; every case uses a unique temporary root.
        }
    }

    private async Task<ImageJobResult> ExecuteFeatureAsync(ImageFeature feature, LocalPath input)
    {
        switch (feature)
        {
            case ImageFeature.Compress:
            {
                var result = await new CompressWithDefaultSettingsWorkflow(
                        _settingsStore,
                        new CompressImageWorkflow(_services))
                    .ExecuteAsync(new CompressWithDefaultSettingsRequest(input), CancellationToken.None);
                Assert.True(result.Succeeded);
                return result.Value!.Result.JobResult;
            }
            case ImageFeature.Convert:
            {
                var result = await new ConvertWithDefaultSettingsWorkflow(
                        _settingsStore,
                        new ConvertImageWorkflow(_services))
                    .ExecuteAsync(new ConvertWithDefaultSettingsRequest(input), CancellationToken.None);
                Assert.True(result.Succeeded);
                return result.Value!.Result.JobResult;
            }
            case ImageFeature.Resize:
            {
                var result = await new ResizeWithDefaultSettingsWorkflow(
                        _settingsStore,
                        new ResizeImageWorkflow(_services))
                    .ExecuteAsync(
                        new ResizeWithDefaultSettingsRequest(input, new PixelResizePolicy(48, 36, maintainAspectRatio: false)),
                        CancellationToken.None);
                Assert.True(result.Succeeded);
                return result.Value!.Result.JobResult;
            }
            case ImageFeature.Crop:
            {
                var result = await new CropWithDefaultSettingsWorkflow(
                        _settingsStore,
                        new CropImageWorkflow(_services))
                    .ExecuteAsync(
                        new CropWithDefaultSettingsRequest(input, new CropRectangle(4, 4, 48, 36)),
                        CancellationToken.None);
                Assert.True(result.Succeeded);
                return result.Value!.Result.JobResult;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }
    }

    private LocalPath CreateInput(string caseRoot, SettingDimension setting, ImageFeature feature)
    {
        var usePng = feature == ImageFeature.Convert
                     && setting is SettingDimension.ConversionFormat or SettingDimension.TransparencyBackground;
        var path = Path.Combine(caseRoot, usePng ? "source.png" : "source.jpg");
        using var image = new MagickImage(usePng ? MagickColors.Transparent : MagickColors.CornflowerBlue, 96, 72);
        if (usePng)
        {
            image.GetPixels().SetPixel(36, 24, [255, 40, 20, 255]);
            image.Format = MagickFormat.Png;
        }
        else
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "AtomPixSettingsMatrix");
            image.SetProfile(exif);
            image.Format = MagickFormat.Jpeg;
            image.Quality = 94;
        }
        image.Write(path);
        return new LocalPath(path);
    }

    private static IReadOnlyList<LocalPath> CreateBatchInputs(string caseRoot, int count)
    {
        var inputs = new List<LocalPath>(count);
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(caseRoot, $"batch-source-{index + 1}.jpg");
            using var image = new MagickImage(MagickColors.CornflowerBlue, 96, 72)
            {
                Format = MagickFormat.Jpeg,
                Quality = 94
            };
            image.Write(path);
            inputs.Add(new LocalPath(path));
        }

        return inputs;
    }

    private static AppSettings CreateSnapshotSettings(string caseRoot, string snapshotName)
    {
        var metadata = MetadataPolicy.Remove;
        return new AppSettings(
            new CompressionProfile(CompressionMode.Custom, new ImageQuality(snapshotName == "old" ? 31 : 79), metadata),
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultSameFormatEncodingPolicy,
            new OutputPolicy(
                new OutputLocationPolicy(
                    OutputLocationMode.CustomDirectory,
                    Path.Combine(caseRoot, $"{snapshotName}-output"),
                    null),
                new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, $"{{name}}_{snapshotName}"),
                OverwritePolicy.AutoRename),
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems);
    }

    private static AppSettings CreateSettings(SettingDimension setting, string caseRoot)
    {
        var metadata = setting == SettingDimension.Metadata
            ? MetadataPolicy.Preserve
            : MetadataPolicy.Remove;
        var compression = setting switch
        {
            SettingDimension.CompressionMode => new CompressionProfile(CompressionMode.Maximum, new ImageQuality(65), metadata),
            SettingDimension.CompressionQuality => new CompressionProfile(CompressionMode.Custom, new ImageQuality(37), metadata),
            _ => new CompressionProfile(CompressionMode.Smart, null, metadata)
        };
        var conversion = setting switch
        {
            SettingDimension.ConversionFormat => new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(82), metadata, new TransparencyPolicy(RgbColor.Parse("#FFFFFF"))),
            SettingDimension.ConversionQuality => new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(37), metadata, new TransparencyPolicy(RgbColor.Parse("#FFFFFF"))),
            SettingDimension.TransparencyBackground => new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(92), metadata, new TransparencyPolicy(RgbColor.Parse("#22AA44"))),
            _ => new ConversionProfile(OutputImageFormat.WebP, new ImageQuality(80), metadata, new TransparencyPolicy(RgbColor.Parse("#FFFFFF")))
        };
        var sameFormat = new SameFormatEncodingPolicy(new ImageQuality(90), metadata);

        var location = setting switch
        {
            SettingDimension.OutputLocationMode => new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
            SettingDimension.SubfolderName => new OutputLocationPolicy(OutputLocationMode.Subfolder, null, "ConfiguredSubfolder"),
            SettingDimension.CustomOutputDirectory => new OutputLocationPolicy(OutputLocationMode.CustomDirectory, Path.Combine(caseRoot, "custom-output"), null),
            _ => OutputPolicy.Default.LocationPolicy
        };
        var naming = setting == SettingDimension.FileNamePattern
            ? new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{name}_configured")
            : OutputPolicy.Default.NamingPolicy;
        var overwrite = setting == SettingDimension.OverwritePolicy
            ? OverwritePolicy.Skip
            : OverwritePolicy.AutoRename;

        return new AppSettings(
            compression,
            conversion,
            sameFormat,
            new OutputPolicy(location, naming, overwrite),
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems);
    }

    private static void PrecreateCollisionWhenRequired(
        SettingDimension setting,
        AppSettings settings,
        LocalPath input,
        ImageFeature feature)
    {
        if (setting != SettingDimension.OverwritePolicy) return;
        var extension = feature == ImageFeature.Convert ? ".webp" : Path.GetExtension(input.Value);
        var directory = Path.Combine(Path.GetDirectoryName(input.Value)!, "AtomPix_Output");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(input.Value)}_atompix{extension}"), "existing");
    }

    private static void AssertOutputLocationAndName(
        SettingDimension setting,
        AppSettings settings,
        LocalPath input,
        string outputPath)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        if (setting == SettingDimension.OutputLocationMode)
            Assert.Equal(Path.GetDirectoryName(input.Value), outputDirectory);
        else if (setting == SettingDimension.SubfolderName)
            Assert.EndsWith("ConfiguredSubfolder", outputDirectory, StringComparison.OrdinalIgnoreCase);
        else if (setting == SettingDimension.CustomOutputDirectory)
            Assert.Equal(Path.GetFullPath(settings.DefaultOutputPolicy.LocationPolicy.CustomDirectory!), Path.GetFullPath(outputDirectory));

        if (setting == SettingDimension.FileNamePattern)
            Assert.StartsWith("source_configured", Path.GetFileNameWithoutExtension(outputPath), StringComparison.Ordinal);
    }

    private static void AssertFeatureOutput(
        SettingDimension setting,
        ImageFeature feature,
        AppSettings settings,
        MagickImage output,
        string outputPath)
    {
        if (feature == ImageFeature.Convert)
        {
            var expectedExtension = settings.DefaultConversionProfile.OutputFormat switch
            {
                OutputImageFormat.Jpeg => ".jpg",
                OutputImageFormat.Png => ".png",
                OutputImageFormat.WebP => ".webp",
                _ => throw new ArgumentOutOfRangeException()
            };
            Assert.Equal(expectedExtension, Path.GetExtension(outputPath), ignoreCase: true);
        }
        else if (feature == ImageFeature.Resize)
        {
            Assert.Equal(48u, output.Width);
            Assert.Equal(36u, output.Height);
        }
        else if (feature == ImageFeature.Crop)
        {
            Assert.Equal(48u, output.Width);
            Assert.Equal(36u, output.Height);
        }

        if (setting == SettingDimension.Metadata)
            Assert.NotNull(output.GetExifProfile());

        if (setting == SettingDimension.TransparencyBackground && feature == ImageFeature.Convert)
        {
            var pixel = output.GetPixels().GetPixel(0, 0).ToColor()!;
            Assert.InRange(pixel.R, (ushort)25, (ushort)50);
            Assert.InRange(pixel.G, (ushort)145, (ushort)190);
            Assert.InRange(pixel.B, (ushort)45, (ushort)90);
        }
    }

    public enum SettingDimension
    {
        CompressionMode,
        CompressionQuality,
        Metadata,
        ConversionFormat,
        ConversionQuality,
        TransparencyBackground,
        OutputLocationMode,
        SubfolderName,
        CustomOutputDirectory,
        FileNamePattern,
        OverwritePolicy
    }

    public enum ImageFeature
    {
        Compress,
        Convert,
        Resize,
        Crop
    }

    private sealed class BlockingImageProcessor : IImageProcessor
    {
        private readonly IImageProcessor _inner;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public BlockingImageProcessor(IImageProcessor inner) => _inner = inner;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ImageProcessorCapabilities Capabilities => _inner.Capabilities;

        public Task<OperationResult<ImageProbeResult>> ProbeAsync(ImageProbeRequest request, CancellationToken cancellationToken) =>
            _inner.ProbeAsync(request, cancellationToken);

        public Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken) =>
            _inner.CreatePreviewAsync(request, cancellationToken);

        public async Task<OperationResult<ImageCompressResult>> CompressAsync(ImageCompressRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                Started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await _inner.CompressAsync(request, cancellationToken);
        }

        public Task<OperationResult<ImageConvertResult>> ConvertAsync(ImageConvertRequest request, CancellationToken cancellationToken) =>
            _inner.ConvertAsync(request, cancellationToken);

        public Task<OperationResult<ImageResizeResult>> ResizeAsync(ImageResizeRequest request, CancellationToken cancellationToken) =>
            _inner.ResizeAsync(request, cancellationToken);

        public Task<OperationResult<ImageCropResult>> CropAsync(ImageCropRequest request, CancellationToken cancellationToken) =>
            _inner.CropAsync(request, cancellationToken);

        public void Release() => _release.TrySetResult();
    }
}
