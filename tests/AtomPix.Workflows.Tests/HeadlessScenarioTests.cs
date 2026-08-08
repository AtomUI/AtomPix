namespace AtomPix.Workflows.Tests;

using ImageMagick;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Magick.Processing;
using AtomPix.Infrastructure.Configuration;
using AtomPix.Infrastructure.FileSystem;
using AtomPix.Infrastructure.Paths;
using AtomPix.Infrastructure.RecentItems;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;

public sealed class HeadlessScenarioTests : IDisposable
{
    private readonly string _root;
    private readonly AppPathProvider _paths;
    private readonly LocalFileSystemService _fileSystem = new();
    private readonly MagickImageProcessor _imageProcessor = new();

    public HeadlessScenarioTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AtomPixHeadlessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPathProvider(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        CreateSampleImages();
    }

    [Fact]
    public async Task User_converts_png_to_webp_without_ui()
    {
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.True(File.Exists(result.Value.JobResult.OutputPath!.Value.Value));
        Assert.EndsWith("sample_atompix.webp", result.Value.JobResult.OutputPath.Value.Value);
    }

    [Fact]
    public async Task User_compresses_jpeg_without_ui()
    {
        var workflow = new CompressImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(PathOf("sample.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.True(File.Exists(result.Value.JobResult.OutputPath!.Value.Value));
    }

    [Fact]
    public async Task User_can_batch_compress_without_subscription_without_ui()
    {
        var workflow = new BatchCompressWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest([PathOf("sample.jpg"), PathOf("sample.png")], CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
        Assert.Equal(2, result.Value.BatchResult.SucceededCount);
    }

    [Fact]
    public async Task User_can_single_compress_and_convert_without_subscription_without_ui()
    {
        var compress = new CompressImageWorkflow(CreateServices());
        var convert = new ConvertImageWorkflow(CreateServices());

        var compressResult = await compress.ExecuteAsync(
            new CompressImageRequest(PathOf("sample.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);
        var convertResult = await convert.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(compressResult.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, compressResult.Value!.JobResult.Status);
        Assert.True(File.Exists(compressResult.Value.JobResult.OutputPath!.Value.Value));
        Assert.True(convertResult.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, convertResult.Value!.JobResult.Status);
        Assert.True(File.Exists(convertResult.Value.JobResult.OutputPath!.Value.Value));
    }

    [Fact]
    public async Task Batch_convert_has_no_subscription_prerequisite()
    {
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([PathOf("sample.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
    }

    [Fact]
    public async Task User_batch_compresses_without_ui()
    {
        var workflow = new BatchCompressWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest([PathOf("sample.jpg"), PathOf("sample.png")], CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.BatchResult.TotalCount);
        Assert.Equal(2, result.Value.BatchResult.SucceededCount);
        Assert.All(result.Value.BatchResult.Items.Where(item => item.Status == ImageJobStatus.Succeeded), item => Assert.True(File.Exists(item.OutputPath!.Value.Value)));
    }

    [Fact]
    public async Task Active_user_batch_converts_without_ui_and_writes_real_outputs()
    {
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([PathOf("sample.png"), PathOf("sample.jpg")], ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
        Assert.Equal(2, result.Value.BatchResult.TotalCount);
        Assert.Equal(2, result.Value.BatchResult.SucceededCount);
        foreach (var item in result.Value.BatchResult.Items)
        {
            Assert.True(File.Exists(item.OutputPath!.Value.Value));
            using var output = new MagickImage(item.OutputPath.Value.Value);
            Assert.Equal(MagickFormat.WebP, output.Format);
        }
    }

    [Fact]
    public async Task AutoRename_scenario_uses_indexed_output_path_without_ui()
    {
        var existingDirectory = Path.Combine(_root, "AtomPix_Output");
        Directory.CreateDirectory(existingDirectory);
        File.WriteAllText(Path.Combine(existingDirectory, "sample_atompix.webp"), "existing");
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("sample_atompix_1.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }


    [Fact]
    public async Task User_opens_image_and_creates_preview_without_ui()
    {
        var open = new OpenImageWorkflow(_imageProcessor);
        var preview = new CreatePreviewWorkflow(_imageProcessor);

        var openResult = await open.ExecuteAsync(new OpenImageRequest(PathOf("sample.jpg")), CancellationToken.None);
        var previewResult = await preview.ExecuteAsync(new CreatePreviewRequest(PathOf("sample.jpg"), 64), CancellationToken.None);

        Assert.True(openResult.Succeeded);
        Assert.Equal(160, openResult.Value!.ProbeResult.Width);
        Assert.True(previewResult.Succeeded);
        Assert.Equal("image/jpeg", previewResult.Value!.Preview.MimeType);
        Assert.True(previewResult.Value.Preview.Width <= 64 || previewResult.Value.Preview.Height <= 64);
    }

    [Fact]
    public async Task Skip_policy_returns_skipped_result_without_overwriting_existing_file()
    {
        var existingDirectory = Path.Combine(_root, "AtomPix_Output");
        Directory.CreateDirectory(existingDirectory);
        var existingPath = Path.Combine(existingDirectory, "sample_atompix.webp");
        await File.WriteAllTextAsync(existingPath, "existing");
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Skipped, result.Value!.JobResult.Status);
        Assert.Equal("existing", await File.ReadAllTextAsync(existingPath));
    }

    [Fact]
    public async Task SameAsInput_policy_writes_next_to_input_without_ui()
    {
        var workflow = new ConvertImageWorkflow(CreateServices());
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(_root, "sample.webp"), result.Value!.JobResult.OutputPath!.Value.Value);
        Assert.True(File.Exists(result.Value.JobResult.OutputPath.Value.Value));
    }

    [Fact]
    public async Task CustomDirectory_policy_creates_directory_and_writes_output_without_ui()
    {
        var outputDirectory = Path.Combine(_root, "custom", "exports");
        var workflow = new ConvertImageWorkflow(CreateServices());
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.CustomDirectory, outputDirectory, null),
            new OutputNamingPolicy(OutputNamingMode.AppendSuffix, "_export"),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(outputDirectory, "sample_export.webp"), result.Value!.JobResult.OutputPath!.Value.Value);
        Assert.True(File.Exists(result.Value.JobResult.OutputPath.Value.Value));
    }
    [Fact]
    public async Task Overwrite_policy_reuses_existing_output_path_without_auto_rename()
    {
        var existingDirectory = Path.Combine(_root, "AtomPix_Output");
        Directory.CreateDirectory(existingDirectory);
        var existingPath = Path.Combine(existingDirectory, "sample_atompix.webp");
        await File.WriteAllTextAsync(existingPath, "existing");
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Overwrite);
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.Equal(existingPath, result.Value.JobResult.OutputPath!.Value.Value);
        Assert.True(new FileInfo(existingPath).Length > 0);
    }

    [Fact]
    public async Task Active_user_batch_convert_allows_partial_failure_without_ui()
    {
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([PathOf("sample.png"), PathOf("missing.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.BatchResult.Status);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.FailedCount);
    }

    [Fact]
    public async Task User_opening_image_can_update_recent_items_without_ui()
    {
        var open = new OpenImageWorkflow(_imageProcessor);
        var recentStore = new JsonRecentItemsStore(_paths);
        var recent = new AddRecentItemWorkflow(recentStore);

        var openResult = await open.ExecuteAsync(new OpenImageRequest(PathOf("sample.jpg")), CancellationToken.None);
        var recentResult = await recent.ExecuteAsync(
            new AddRecentItemRequest(PathOf("sample.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow, MaxCount: 20),
            CancellationToken.None);
        var loaded = await recentStore.LoadAsync(CancellationToken.None);

        Assert.True(openResult.Succeeded);
        Assert.True(recentResult.Succeeded);
        Assert.True(loaded.Succeeded);
        Assert.Single(loaded.Value!);
        Assert.Equal(PathOf("sample.jpg"), loaded.Value![0].Path);
    }

    [Fact]
    public async Task Batch_result_can_be_projected_to_progress_snapshot_without_ui()
    {
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([PathOf("sample.png"), PathOf("missing.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var snapshot = BatchProgressSnapshot.FromResults(
            result.Value!.BatchResult.BatchId,
            result.Value.BatchResult.Type,
            result.Value.BatchResult.TotalCount,
            result.Value.BatchResult.Items,
            currentInputPath: null);

        Assert.True(snapshot.IsCompleted);
        Assert.Equal(1.0, snapshot.CompletionRatio);
        Assert.Equal(1, snapshot.SucceededCount);
        Assert.Equal(1, snapshot.FailedCount);
    }

    [Fact]
    public async Task Default_settings_convert_workflow_uses_saved_settings_without_ui()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var save = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var workflow = new ConvertWithDefaultSettingsWorkflow(settingsStore, new ConvertImageWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(new ConvertWithDefaultSettingsRequest(PathOf("sample.png")), CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(result.Succeeded);
        Assert.EndsWith("sample_atompix.webp", result.Value!.Result.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task Default_settings_resize_workflow_applies_explicit_resize_policy_without_ui()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var settings = new AppSettings(
            new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), MetadataPolicy.Remove),
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultSameFormatEncodingPolicy,
            AppSettings.Default.DefaultOutputPolicy,
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems);
        var save = await settingsStore.SaveAsync(settings, CancellationToken.None);
        var workflow = new ResizeWithDefaultSettingsWorkflow(settingsStore, new ResizeImageWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(
            new ResizeWithDefaultSettingsRequest(PathOf("sample.jpg"), new PixelResizePolicy(80, 80, maintainAspectRatio: true)),
            CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(result.Succeeded);
        using var output = new MagickImage(result.Value!.Result.JobResult.OutputPath!.Value.Value);
        Assert.Equal(80u, output.Width);
        Assert.Equal(60u, output.Height);
    }

    [Fact]
    public async Task Default_settings_batch_convert_handles_mixed_inputs_without_ui()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var save = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var workflow = new BatchConvertWithDefaultSettingsWorkflow(settingsStore, new BatchConvertWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(
            new BatchConvertWithDefaultSettingsRequest([PathOf("sample.png"), PathOf("missing.png"), PathOf("animated.gif")]),
            CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.Result.BatchResult.Status);
        Assert.Equal(3, result.Value.Result.BatchResult.TotalCount);
        Assert.Equal(1, result.Value.Result.BatchResult.SucceededCount);
        Assert.Equal(2, result.Value.Result.BatchResult.FailedCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
        Assert.Equal(3, result.Value.Result.FinalProgress.CompletedCount);
    }


    [Fact]
    public async Task Default_settings_batch_convert_marks_invalid_image_file_without_ui()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var save = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var workflow = new BatchConvertWithDefaultSettingsWorkflow(settingsStore, new BatchConvertWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(
            new BatchConvertWithDefaultSettingsRequest([PathOf("sample.png"), PathOf("not-image.txt")]),
            CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.Result.BatchResult.Status);
        Assert.Equal(1, result.Value.Result.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.Result.BatchResult.FailedCount);
        Assert.Contains(result.Value.Result.BatchResult.Items, item => item.Status == ImageJobStatus.Failed && item.Error!.Code == AtomPixErrorCode.InvalidImageFile);
    }
    [Fact]
    public async Task Default_settings_batch_compress_handles_mixed_inputs_without_ui()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var save = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var workflow = new BatchCompressWithDefaultSettingsWorkflow(settingsStore, new BatchCompressWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(
            new BatchCompressWithDefaultSettingsRequest([PathOf("sample.jpg"), PathOf("missing.jpg"), PathOf("animated.gif")]),
            CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.Result.BatchResult.Status);
        Assert.Equal(3, result.Value.Result.BatchResult.TotalCount);
        Assert.Equal(1, result.Value.Result.BatchResult.SucceededCount);
        Assert.Equal(2, result.Value.Result.BatchResult.FailedCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
    }
    [Fact]
    public async Task Corrupt_settings_blocks_default_workflow_without_overwriting_and_recovers_after_save_without_ui()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        var settingsPath = Path.Combine(_paths.AppDataDirectory.Value, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{bad json");
        var settingsStore = new JsonAppSettingsStore(_paths);
        var workflow = new ConvertWithDefaultSettingsWorkflow(settingsStore, new ConvertImageWorkflow(CreateServices()));

        var failed = await workflow.ExecuteAsync(new ConvertWithDefaultSettingsRequest(PathOf("sample.png")), CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, failed.Error!.Code);
        Assert.Equal("{bad json", await File.ReadAllTextAsync(settingsPath));

        var save = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var recovered = await workflow.ExecuteAsync(new ConvertWithDefaultSettingsRequest(PathOf("sample.png")), CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(recovered.Succeeded);
        Assert.True(File.Exists(recovered.Value!.Result.JobResult.OutputPath!.Value.Value));
    }

    [Fact]
    public async Task Legacy_corrupt_subscription_file_does_not_restrict_batch_processing()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        var subscriptionPath = Path.Combine(_paths.AppDataDirectory.Value, "subscription.json");
        await File.WriteAllTextAsync(subscriptionPath, "{bad json");
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([PathOf("sample.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
        Assert.Equal("{bad json", await File.ReadAllTextAsync(subscriptionPath));
    }

    [Fact]
    public async Task Corrupt_recent_items_file_recovers_when_user_opens_image_without_ui()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        var recentPath = Path.Combine(_paths.AppDataDirectory.Value, "recent-items.json");
        await File.WriteAllTextAsync(recentPath, "{bad json");
        var open = new OpenImageWorkflow(_imageProcessor);
        var recentStore = new JsonRecentItemsStore(_paths);
        var recent = new AddRecentItemWorkflow(recentStore);

        var openResult = await open.ExecuteAsync(new OpenImageRequest(PathOf("sample.jpg")), CancellationToken.None);
        var add = await recent.ExecuteAsync(new AddRecentItemRequest(PathOf("sample.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow, MaxCount: 20), CancellationToken.None);
        var loaded = await recentStore.LoadAsync(CancellationToken.None);

        Assert.True(openResult.Succeeded);
        Assert.True(add.Succeeded);
        Assert.True(loaded.Succeeded);
        Assert.Single(loaded.Value!);
        Assert.Equal(PathOf("sample.jpg"), loaded.Value![0].Path);
        Assert.NotEqual("{bad json", await File.ReadAllTextAsync(recentPath));
    }

    [Fact]
    public async Task Recent_items_deduplicate_sort_and_trim_with_real_store_without_ui()
    {
        var store = new JsonRecentItemsStore(_paths);
        var workflow = new AddRecentItemWorkflow(store);
        var old = DateTimeOffset.UtcNow.AddMinutes(-10);
        var middle = DateTimeOffset.UtcNow.AddMinutes(-5);
        var now = DateTimeOffset.UtcNow;

        Assert.True((await workflow.ExecuteAsync(new AddRecentItemRequest(PathOf("sample.jpg"), RecentItemKind.File, old, MaxCount: 2), CancellationToken.None)).Succeeded);
        Assert.True((await workflow.ExecuteAsync(new AddRecentItemRequest(PathOf("sample.png"), RecentItemKind.File, middle, MaxCount: 2), CancellationToken.None)).Succeeded);
        Assert.True((await workflow.ExecuteAsync(new AddRecentItemRequest(PathOf("sample.jpg"), RecentItemKind.File, now, MaxCount: 2), CancellationToken.None)).Succeeded);
        Assert.True((await workflow.ExecuteAsync(new AddRecentItemRequest(PathOf("not-image.txt"), RecentItemKind.File, now.AddMinutes(1), MaxCount: 2), CancellationToken.None)).Succeeded);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.Succeeded);
        Assert.Equal(2, loaded.Value!.Count);
        Assert.Equal(PathOf("not-image.txt"), loaded.Value[0].Path);
        Assert.Equal(PathOf("sample.jpg"), loaded.Value[1].Path);
    }

    [Fact]
    public async Task Convert_output_target_directory_fails_without_temporary_files_without_ui()
    {
        var input = PathOf("directory-as-output.png");
        File.Copy(PathOf("sample.png").Value, input.Value);
        var outputDirectory = Path.Combine(_root, "directory-as-output.webp");
        Directory.CreateDirectory(outputDirectory);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.CustomDirectory, _root, null),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.Overwrite);
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(input, ConversionProfile.WebPDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Failed, result.Value!.JobResult.Status);
        Assert.Equal(AtomPixErrorCode.ImageWriteFailed, result.Value.JobResult.Error!.Code);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public async Task AutoRename_handles_multiple_real_conflicts_without_ui()
    {
        var existingDirectory = Path.Combine(_root, "AtomPix_Output");
        Directory.CreateDirectory(existingDirectory);
        await File.WriteAllTextAsync(Path.Combine(existingDirectory, "sample_atompix.webp"), "existing");
        await File.WriteAllTextAsync(Path.Combine(existingDirectory, "sample_atompix_1.webp"), "existing");
        await File.WriteAllTextAsync(Path.Combine(existingDirectory, "sample_atompix_2.webp"), "existing");
        var workflow = new ConvertImageWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(PathOf("sample.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(existingDirectory, "sample_atompix_3.webp"), result.Value!.JobResult.OutputPath!.Value.Value);
        Assert.True(File.Exists(result.Value.JobResult.OutputPath.Value.Value));
    }
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ImageWorkflowServices CreateServices() => new(
        _imageProcessor,
        _fileSystem);

    private LocalPath PathOf(string fileName) => new(Path.Combine(_root, fileName));

    private static IReadOnlyList<string> TemporaryFilesIn(string directory) =>
        Directory.EnumerateFiles(directory, ".*.tmp*", SearchOption.AllDirectories).ToArray();

    private void CreateSampleImages()
    {
        using (var image = new MagickImage(MagickColors.Red, 160, 120))
        {
            image.Format = MagickFormat.Jpeg;
            image.Write(Path.Combine(_root, "sample.jpg"));
        }

        using (var image = new MagickImage(MagickColors.Transparent, 160, 120))
        {
            image.Format = MagickFormat.Png;
            image.GetPixels().SetPixel(10, 10, [255, 0, 0, 255]);
            image.Write(Path.Combine(_root, "sample.png"));
        }

        using var collection = new MagickImageCollection();
        collection.Add(new MagickImage(MagickColors.Red, 80, 80) { AnimationDelay = 10 });
        collection.Add(new MagickImage(MagickColors.Blue, 80, 80) { AnimationDelay = 10 });
        collection.Write(Path.Combine(_root, "animated.gif"));

        File.WriteAllText(Path.Combine(_root, "not-image.txt"), "this is not an image");
        File.WriteAllBytes(Path.Combine(_root, "corrupt.jpg"), [0xFF, 0xD8, 0x00, 0x01, 0x02]);
    }
}



