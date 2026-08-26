namespace AtomPix.Workflows.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Infrastructure.Diagnostics;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;
using AtomPix.Workflows.Settings;
using Microsoft.Extensions.Logging;

public sealed class WorkflowContractTests
{
    [Fact]
    public async Task OpenImageWorkflow_calls_probe()
    {
        var image = new FakeImageProcessor();
        var workflow = new OpenImageWorkflow(image);

        var result = await workflow.ExecuteAsync(new OpenImageRequest(new LocalPath("a.jpg")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ProbeCalled);
    }

    [Fact]
    public async Task OpenImageWorkflow_propagates_probe_failure()
    {
        var image = new FakeImageProcessor { ProbeFailForInput = "bad.jpg" };
        var workflow = new OpenImageWorkflow(image);

        var result = await workflow.ExecuteAsync(new OpenImageRequest(new LocalPath("bad.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
    }

    [Fact]
    public async Task Workflow_diagnostic_boundary_converts_unexpected_exception_once_and_redacts_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "AtomPixWorkflowDiagnostics", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(Path.Combine(root, "logs")));
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var image = new FakeImageProcessor { ThrowOnProbe = true };
            var workflow = new CompressImageWorkflow(
                CreateServices(image: image),
                loggerFactory.CreateLogger<CompressImageWorkflow>());
            var secretInput = new LocalPath(Path.Combine(root, "private", "secret.jpg"));

            var result = await workflow.ExecuteAsync(
                new CompressImageRequest(secretInput, CompressionProfile.BalancedDefault(), OutputPolicy.Default),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(AtomPixErrorCode.Unknown, result.Error!.Code);
            Assert.Equal(AtomPixErrorCategory.Unexpected, result.Error.Category);
            Assert.Matches("^APX-[0-9A-F]{12}$", result.Error.Details!["DiagnosticId"]);
            var lines = Directory.GetFiles(Path.Combine(root, "logs"), "*.jsonl").SelectMany(File.ReadLines).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.Single(lines, line => line.Contains("WorkflowUnexpectedFailure", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("secret.jpg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatePreviewWorkflow_calls_preview()
    {
        var image = new FakeImageProcessor();
        var workflow = new CreatePreviewWorkflow(image);

        var result = await workflow.ExecuteAsync(new CreatePreviewRequest(new LocalPath("a.jpg"), 256), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.PreviewCalled);
    }

    [Fact]
    public async Task CreatePreviewWorkflow_propagates_preview_failure()
    {
        var image = new FakeImageProcessor { PreviewFailForInput = "bad.jpg" };
        var workflow = new CreatePreviewWorkflow(image);

        var result = await workflow.ExecuteAsync(new CreatePreviewRequest(new LocalPath("bad.jpg"), 256), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
    }

    [Fact]
    public async Task BatchCompressWorkflow_is_available_without_subscription()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(image: image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.CompressCalled);
    }

    [Fact]
    public async Task Single_image_workflows_are_available_without_subscription()
    {
        var compressImage = new FakeImageProcessor();
        var compress = new CompressImageWorkflow(CreateServices(image: compressImage));
        var convertImage = new FakeImageProcessor();
        var convert = new ConvertImageWorkflow(CreateServices(image: convertImage));

        var compressResult = await compress.ExecuteAsync(new CompressImageRequest(new LocalPath("free.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);
        var convertResult = await convert.ExecuteAsync(new ConvertImageRequest(new LocalPath("free.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(compressResult.Succeeded);
        Assert.True(convertResult.Succeeded);
        Assert.True(compressImage.CompressCalled);
        Assert.True(convertImage.ConvertCalled);
    }

    [Fact]
    public async Task BatchConvertWorkflow_is_available_without_subscription()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(image: image);
        var workflow = new BatchConvertWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([new LocalPath("a.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ProbeCalled);
        Assert.True(image.ConvertCalled);
    }

    [Fact]
    public async Task BatchCompressWorkflow_has_no_subscription_prerequisite()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(image: image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ProbeCalled);
        Assert.True(image.CompressCalled);
    }

    [Fact]
    public async Task CompressImageWorkflow_auto_renames_existing_target()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.EndsWith("a_atompix_1.jpg", result.Value.JobResult.OutputPath!.Value.Value);
        Assert.Equal(OutputWriteDisposition.AutoRenamed, result.Value.OutputDisposition);
    }

    [Fact]
    public async Task CompressImageWorkflow_skip_returns_skipped_job()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Skipped, result.Value!.JobResult.Status);
        Assert.Equal(OutputWriteDisposition.SkippedExisting, result.Value.OutputDisposition);
        Assert.False(((FakeImageProcessor)services.ImageProcessor).CompressCalled);
    }

    [Fact]
    public async Task CompressImageWorkflow_overwrite_reports_actual_output_disposition()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Overwrite);
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.Equal(OutputWriteDisposition.Overwritten, result.Value.OutputDisposition);
        Assert.EndsWith("a_atompix.jpg", result.Value.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_uses_target_format_extension()
    {
        var services = CreateServices();
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("a.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("a_atompix.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }


    [Fact]
    public async Task ConvertImageWorkflow_same_as_input_keep_name_preserves_directory_and_changes_extension()
    {
        var services = CreateServices();
        var workflow = new ConvertImageWorkflow(services);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.AutoRename);
        var input = new LocalPath(Path.Combine("input", "archive.photo.png"));

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(input, ConversionProfile.WebPDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine("input", "archive.photo.webp"), result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_custom_directory_appends_suffix()
    {
        var services = CreateServices();
        var workflow = new ConvertImageWorkflow(services);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.CustomDirectory, Path.Combine("custom", "out"), null),
            new OutputNamingPolicy(OutputNamingMode.AppendSuffix, "_export"),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath(Path.Combine("input", "photo.png")), ConversionProfile.WebPDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine("custom", "out", "photo_export.webp"), result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_auto_renames_multi_dot_file_name()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "archive.photo_atompix.webp"));
        var services = CreateServices(fs);
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("archive.photo.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("archive.photo_atompix_1.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_auto_renames_until_available()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "archive.photo_atompix.webp"));
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "archive.photo_atompix_1.webp"));
        var services = CreateServices(fs);
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("archive.photo.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("archive.photo_atompix_2.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task CompressImageWorkflow_custom_directory_keep_name_uses_input_extension()
    {
        var services = CreateServices();
        var workflow = new CompressImageWorkflow(services);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.CustomDirectory, Path.Combine("custom", "compressed"), null),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath(Path.Combine("input", "photo.jpg")), CompressionProfile.BalancedDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine("custom", "compressed", "photo.jpg"), result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_subfolder_keep_name_uses_target_extension()
    {
        var services = CreateServices();
        var workflow = new ConvertImageWorkflow(services);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.Subfolder, null, "Exports"),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.AutoRename);
        var profile = new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath(Path.Combine("input", "photo.png")), profile, policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine("input", "Exports", "photo.jpg"), result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertImageWorkflow_skip_returns_skipped_job_without_processing()
    {
        var image = new FakeImageProcessor();
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.webp"));
        var services = CreateServices(fs, image);
        var workflow = new ConvertImageWorkflow(services);
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("a.png"), ConversionProfile.WebPDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Skipped, result.Value!.JobResult.Status);
        Assert.False(image.ConvertCalled);
    }

    [Fact]
    public async Task CompressImageWorkflow_rejects_input_without_extension_before_resolving_output()
    {
        var image = new FakeImageProcessor { ProbeFormat = ImageFormatKind.Jpeg };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("photo"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedOutputFormat, result.Error!.Code);
        Assert.False(image.CompressCalled);
    }
    [Fact]
    public async Task BatchCompressWorkflow_allows_partial_success()
    {
        var image = new FakeImageProcessor { FailForInput = "bad.jpg" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg"), new LocalPath("bad.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.BatchResult.Status);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.FailedCount);
    }

    [Fact]
    public async Task LoadSettingsWorkflow_returns_store_value()
    {
        var store = new FakeAppSettingsStore();
        var workflow = new LoadSettingsWorkflow(store);

        var result = await workflow.ExecuteAsync(new LoadSettingsRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AppSettings.Default, result.Value!.Settings);
    }

    [Fact]
    public async Task SaveSettingsWorkflow_saves_store_value()
    {
        var store = new FakeAppSettingsStore();
        var workflow = new SaveSettingsWorkflow(store);

        var result = await workflow.ExecuteAsync(new SaveSettingsRequest(AppSettings.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(store.Saved);
    }

    [Fact]
    public async Task SaveSettingsWorkflow_returns_store_failure()
    {
        var store = new FakeAppSettingsStore { SaveFailure = new AtomPixError(AtomPixErrorCode.SettingsSaveFailed, AtomPixErrorCategory.Configuration, "failed") };
        var workflow = new SaveSettingsWorkflow(store);

        var result = await workflow.ExecuteAsync(new SaveSettingsRequest(AppSettings.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(store.Saved);
        Assert.Equal(AtomPixErrorCode.SettingsSaveFailed, result.Error!.Code);
    }


    [Fact]
    public void Workflows_reject_null_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenImageWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new CreatePreviewWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new CompressImageWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new ConvertImageWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new BatchCompressWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new BatchConvertWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new LoadSettingsWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new SaveSettingsWorkflow(null!));
        Assert.Throws<ArgumentNullException>(() => new ImageWorkflowServices(null!, new FakeFileSystemService()));
    }

    [Fact]
    public async Task CompressImageWorkflow_returns_failure_when_input_size_fails()
    {
        var fs = new FakeFileSystemService { FailFileSize = true };
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("missing.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task CompressImageWorkflow_returns_failure_when_output_directory_creation_fails()
    {
        var fs = new FakeFileSystemService { FailCreateDirectory = true };
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OutputDirectoryNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task CompressImageWorkflow_overwrite_uses_existing_target_without_rename()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Overwrite);
        var services = CreateServices(fs);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("a_atompix.jpg", result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task CompressImageWorkflow_overwrite_rejects_output_that_is_the_input_before_job_creation()
    {
        var image = new FakeImageProcessor();
        var fs = new FakeFileSystemService();
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.Overwrite);
        var workflow = new CompressImageWorkflow(CreateServices(fs, image));

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(new LocalPath("input.jpg"), CompressionProfile.BalancedDefault(), policy),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OutputPathConflictsWithInput, result.Error!.Code);
        Assert.False(image.CompressCalled);
        Assert.Equal(0, fs.CreateDirectoryCallCount);
    }


    [Fact]
    public async Task CompressImageWorkflow_processing_failure_returns_failed_job_with_input_size_and_output_path()
    {
        var image = new FakeImageProcessor { FailForInput = "bad.jpg" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("bad.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Failed, result.Value!.JobResult.Status);
        Assert.Equal(100, result.Value.JobResult.InputSizeBytes);
        Assert.Null(result.Value.JobResult.OutputSizeBytes);
        Assert.EndsWith("bad_atompix.jpg", result.Value.JobResult.OutputPath!.Value.Value);
        Assert.Equal(AtomPixErrorCode.ImageCompressFailed, result.Value.JobResult.Error!.Code);
    }

    [Fact]
    public async Task ConvertImageWorkflow_processing_failure_returns_failed_job_with_input_size_and_output_path()
    {
        var image = new FakeImageProcessor { FailConvertForInput = "bad.png" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("bad.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Failed, result.Value!.JobResult.Status);
        Assert.Equal(100, result.Value.JobResult.InputSizeBytes);
        Assert.Null(result.Value.JobResult.OutputSizeBytes);
        Assert.EndsWith("bad_atompix.webp", result.Value.JobResult.OutputPath!.Value.Value);
        Assert.Equal(AtomPixErrorCode.ImageConvertFailed, result.Value.JobResult.Error!.Code);
    }
    [Fact]
    public void ConversionProfile_rejects_invalid_output_format_before_workflow_execution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConversionProfile((OutputImageFormat)999, null, MetadataPolicy.Remove, TransparencyPolicy.Default));
    }

    [Fact]
    public async Task BatchConvertWorkflow_rejects_empty_input_list()
    {
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([], ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidInputPath, result.Error!.Code);
    }

    [Fact]
    public async Task Settings_workflows_reject_null_requests()
    {
        var store = new FakeAppSettingsStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => new LoadSettingsWorkflow(store).ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new SaveSettingsWorkflow(store).ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new SaveSettingsWorkflow(store).ExecuteAsync(new SaveSettingsRequest(null!), CancellationToken.None));
    }

    [Fact]
    public async Task AddRecentItemWorkflow_deduplicates_sorts_trims_and_saves()
    {
        var store = new FakeRecentItemsStore([
            new RecentItem(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow.AddMinutes(-10)),
            new RecentItem(new LocalPath("C:\\img\\b.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow.AddMinutes(-5))
        ]);
        var workflow = new AddRecentItemWorkflow(store);
        var now = DateTimeOffset.UtcNow;

        var result = await workflow.ExecuteAsync(
            new AddRecentItemRequest(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, now, MaxCount: 2),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(store.Saved);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("C:\\img\\a.jpg", result.Value.Items[0].Path.Value);
        Assert.Equal(now, result.Value.Items[0].OpenedAt);
    }

    [Fact]
    public async Task AddRecentItemWorkflow_returns_load_failure_without_saving()
    {
        var store = new FakeRecentItemsStore([])
        {
            LoadFailure = new AtomPixError(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "failed")
        };
        var workflow = new AddRecentItemWorkflow(store);

        var result = await workflow.ExecuteAsync(new AddRecentItemRequest(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow, MaxCount: 2), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(store.Saved);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, result.Error!.Code);
    }

    [Fact]
    public async Task AddRecentItemWorkflow_returns_save_failure()
    {
        var store = new FakeRecentItemsStore([])
        {
            SaveFailure = new AtomPixError(AtomPixErrorCode.SettingsSaveFailed, AtomPixErrorCategory.Configuration, "failed")
        };
        var workflow = new AddRecentItemWorkflow(store);

        var result = await workflow.ExecuteAsync(new AddRecentItemRequest(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow, MaxCount: 2), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(store.Saved);
        Assert.Equal(AtomPixErrorCode.SettingsSaveFailed, result.Error!.Code);
    }

    [Fact]
    public async Task BatchCompressWorkflow_does_not_require_subscription_storage()
    {
        var image = new FakeImageProcessor();
        var services = new ImageWorkflowServices(
            image,
            new FakeFileSystemService());
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ProbeCalled);
        Assert.True(image.CompressCalled);
    }

    [Fact]
    public async Task CompressWithDefaultSettingsWorkflow_uses_settings_profiles_and_output_policy()
    {
        var services = CreateServices();
        var workflow = new CompressWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new CompressImageWorkflow(services));

        var result = await workflow.ExecuteAsync(new CompressWithDefaultSettingsRequest(new LocalPath("a.jpg")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.Result.JobResult.Status);
        Assert.EndsWith("a_atompix.jpg", result.Value.Result.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertWithDefaultSettingsWorkflow_uses_settings_profiles_and_output_policy()
    {
        var services = CreateServices();
        var workflow = new ConvertWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new ConvertImageWorkflow(services));

        var result = await workflow.ExecuteAsync(new ConvertWithDefaultSettingsRequest(new LocalPath("a.png")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.Result.JobResult.Status);
        Assert.EndsWith("a_atompix.webp", result.Value.Result.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task CompressImageWorkflow_rejects_animated_input_before_compressing()
    {
        var image = new FakeImageProcessor { ProbeIsAnimated = true };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("animated.gif"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedInputFormat, result.Error!.Code);
        Assert.False(image.CompressCalled);
    }


    [Fact]
    public async Task CompressImageWorkflow_propagates_probe_failure_without_processing()
    {
        var image = new FakeImageProcessor { ProbeFailForInput = "bad.jpg" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("bad.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
        Assert.False(image.CompressCalled);
    }

    [Fact]
    public async Task BatchConvertWorkflow_records_probe_failure_as_failed_item_and_continues()
    {
        var image = new FakeImageProcessor { ProbeFailForInput = "bad.png" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new BatchConvertWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([new LocalPath("good.png"), new LocalPath("bad.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.BatchResult.Status);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.FailedCount);
        Assert.Contains(result.Value.BatchResult.Items, item => item.Status == ImageJobStatus.Failed && item.Error!.Code == AtomPixErrorCode.InvalidImageFile);
    }
    [Fact]
    public async Task ConvertImageWorkflow_rejects_output_format_not_declared_by_processor()
    {
        var image = new FakeImageProcessor
        {
            Capabilities = new ImageProcessorCapabilities(
                new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP },
                new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg, OutputImageFormat.Png },
                true,
                false,
                new ImageResourceCapabilities(1024 * 1024, 10000, 10000, 100_000_000, 10000, 10000, 100_000_000),
                null,
                null)
        };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("a.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedOutputFormat, result.Error!.Code);
    }

    [Fact]
    public async Task BatchCompressWorkflow_returns_final_progress()
    {
        var workflow = new BatchCompressWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.FinalProgress.IsCompleted);
        Assert.Equal(2, result.Value.FinalProgress.TotalCount);
        Assert.Equal(2, result.Value.FinalProgress.SucceededCount);
    }

    [Fact]
    public async Task Recent_item_management_workflows_load_remove_and_clear_without_touching_files()
    {
        var first = new RecentItem(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow);
        var second = new RecentItem(new LocalPath("C:\\img\\folder"), RecentItemKind.Directory, DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new FakeRecentItemsStore([first, second]);

        var loaded = await new LoadRecentItemsWorkflow(store).ExecuteAsync(new LoadRecentItemsRequest(20), CancellationToken.None);
        Assert.True(loaded.Succeeded);
        Assert.Equal([first, second], loaded.Value!.Items);

        var removed = await new RemoveRecentItemWorkflow(store).ExecuteAsync(
            new RemoveRecentItemRequest(first.Path, first.Kind),
            CancellationToken.None);
        Assert.True(removed.Succeeded);
        Assert.Equal([second], removed.Value!.Items);

        var cleared = await new ClearRecentItemsWorkflow(store).ExecuteAsync(new ClearRecentItemsRequest(), CancellationToken.None);
        Assert.True(cleared.Succeeded);
        var afterClear = await store.LoadAsync(CancellationToken.None);
        Assert.Empty(afterClear.Value!);
    }

    [Fact]
    public async Task BatchCompressWorkflow_freezes_custom_output_plan_and_publishes_ordered_progress()
    {
        var progress = new RecordingProgress<BatchExecutionProgress<BatchCompressItemResult>>();
        var workflow = new BatchCompressWorkflow(CreateServices());
        var outputPolicy = new OutputPolicy(
            OutputPolicy.Default.LocationPolicy,
            new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "holiday"),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest(
                [new LocalPath("a.jpg"), new LocalPath("b.jpg")],
                CompressionProfile.BalancedDefault(),
                outputPolicy),
            progress,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Collection(
            result.Value!.ItemResults,
            item => Assert.EndsWith(Path.Combine("AtomPix_Output", "holiday_001.jpg"), item.JobResult.OutputPath!.Value.Value),
            item => Assert.EndsWith(Path.Combine("AtomPix_Output", "holiday_002.jpg"), item.JobResult.OutputPath!.Value.Value));
        Assert.Equal(5, progress.Items.Count);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], progress.Items.Select(item => item.Sequence));
        Assert.All(progress.Items, item =>
        {
            Assert.Same(progress.Items[0].OutputPlan, item.OutputPlan);
            Assert.Equal(2, item.OutputPlan.Items.Count);
            Assert.Equal("holiday_{index}", item.OutputPlan.EffectivePattern);
            Assert.EndsWith(Path.Combine("AtomPix_Output", "holiday_001.jpg"), item.OutputPlan.Items[0].OutputPath.Value);
            Assert.EndsWith(Path.Combine("AtomPix_Output", "holiday_002.jpg"), item.OutputPlan.Items[1].OutputPath.Value);
        });
        Assert.Null(progress.Items[0].ChangedItem);
        Assert.Equal(0, progress.Items[0].Summary.CompletedCount);
        Assert.Equal(ImageJobStatus.Running, progress.Items[1].ChangedItem!.Status);
        Assert.Equal(ImageJobStatus.Succeeded, progress.Items[2].ChangedItem!.Status);
        Assert.Equal(ImageJobStatus.Running, progress.Items[3].ChangedItem!.Status);
        Assert.Equal(ImageJobStatus.Succeeded, progress.Items[4].ChangedItem!.Status);
        Assert.Equal(2, progress.Items[^1].Summary.CompletedCount);
        Assert.Equal(2, progress.Items[^1].Summary.SucceededCount);
    }

    [Fact]
    public async Task BatchCompressWorkflow_rejects_output_that_would_overwrite_another_input()
    {
        var fileSystem = new FakeFileSystemService();
        var image = new FakeImageProcessor();
        var workflow = new BatchCompressWorkflow(CreateServices(fileSystem, image));
        var outputPolicy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
            new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{name}"),
            OverwritePolicy.Overwrite);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest(
                [new LocalPath("a.jpg"), new LocalPath("a_001.jpg")],
                CompressionProfile.BalancedDefault(),
                outputPolicy),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OutputPathConflictsWithInput, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.CompressCalled);
        Assert.Equal(0, fileSystem.CreateDirectoryCallCount);
    }

    [Fact]
    public async Task BatchResizeWorkflow_resolves_shared_percentage_against_each_source_size()
    {
        var image = new FakeImageProcessor();
        image.ProbeSizes["wide.jpg"] = new ImageSize(400, 200);
        image.ProbeSizes["tall.jpg"] = new ImageSize(200, 400);
        var workflow = new BatchResizeWorkflow(CreateServices(image: image));

        var result = await workflow.ExecuteAsync(
            new BatchResizeRequest(
                [new LocalPath("wide.jpg"), new LocalPath("tall.jpg")],
                new PercentageResizePolicy(50),
                OutputPolicy.Default,
                SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
        Assert.Collection(
            result.Value.ItemResults,
            item =>
            {
                Assert.Equal(new ImageSize(400, 200), item.InputSize);
                Assert.Equal(new ResolvedResizeSize(200, 100), item.TargetSize);
                Assert.Equal(new ImageSize(200, 100), item.ActualOutputSize);
            },
            item =>
            {
                Assert.Equal(new ImageSize(200, 400), item.InputSize);
                Assert.Equal(new ResolvedResizeSize(100, 200), item.TargetSize);
                Assert.Equal(new ImageSize(100, 200), item.ActualOutputSize);
            });
        Assert.Equal(
            [new ResolvedResizeSize(200, 100), new ResolvedResizeSize(100, 200)],
            image.ResizeRequests.Select(request => request.TargetSize));
    }

    [Fact]
    public async Task CompressImageWorkflow_rejects_oversized_file_before_probe_or_job_setup()
    {
        var fileSystem = new FakeFileSystemService();
        fileSystem.FileSizes["large.jpg"] = 101;
        var image = new FakeImageProcessor();
        image.SetResources(new ImageResourceCapabilities(100, 1000, 1000, 1_000_000, 1000, 1000, 1_000_000));
        var workflow = new CompressImageWorkflow(CreateServices(fileSystem, image: image));

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(new LocalPath("large.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileTooLarge, result.Error!.Code);
        Assert.Equal("101", result.Error.Details!["ActualValue"]);
        Assert.Equal("100", result.Error.Details["MaximumValue"]);
        Assert.False(image.ProbeCalled);
        Assert.False(image.CompressCalled);
        Assert.Equal(0, fileSystem.CreateDirectoryCallCount);
    }

    [Fact]
    public async Task CompressImageWorkflow_rejects_oversized_dimensions_after_probe_before_job_setup()
    {
        var fileSystem = new FakeFileSystemService();
        var image = new FakeImageProcessor();
        image.ProbeSizes["wide.jpg"] = new ImageSize(101, 10);
        image.SetResources(new ImageResourceCapabilities(1000, 100, 100, 10_000, 100, 100, 10_000));
        var workflow = new CompressImageWorkflow(CreateServices(fileSystem, image: image));

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(new LocalPath("wide.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageDimensionsExceedLimit, result.Error!.Code);
        Assert.Equal("101", result.Error.Details!["ActualWidth"]);
        Assert.True(image.ProbeCalled);
        Assert.False(image.CompressCalled);
        Assert.Equal(0, fileSystem.CreateDirectoryCallCount);
    }

    [Fact]
    public async Task BatchCompressWorkflow_fails_static_resource_item_and_continues_next_item()
    {
        var fileSystem = new FakeFileSystemService();
        fileSystem.FileSizes["large.jpg"] = 101;
        fileSystem.FileSizes["ok.jpg"] = 90;
        var image = new FakeImageProcessor();
        image.SetResources(new ImageResourceCapabilities(100, 1000, 1000, 1_000_000, 1000, 1000, 1_000_000));
        var workflow = new BatchCompressWorkflow(CreateServices(fileSystem, image));

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest(
                [new LocalPath("large.jpg"), new LocalPath("ok.jpg")],
                CompressionProfile.BalancedDefault(),
                OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.PartiallySucceeded, result.Value!.BatchResult.Status);
        Assert.Collection(
            result.Value.ItemResults,
            item => Assert.Equal(AtomPixErrorCode.InputFileTooLarge, item.JobResult.Error!.Code),
            item => Assert.Equal(ImageJobStatus.Succeeded, item.JobResult.Status));
        Assert.Single(image.ProbeRequests);
        Assert.EndsWith("ok.jpg", image.ProbeRequests[0].InputPath.Value);
        Assert.Single(image.CompressRequests);
    }

    [Fact]
    public async Task BatchCompressWorkflow_aborts_after_disk_space_failure_without_fabricating_pending_results()
    {
        var diskError = new AtomPixError(
            AtomPixErrorCode.InsufficientDiskSpace,
            AtomPixErrorCategory.FileSystem,
            "Output volume has insufficient free space.");
        var image = new FakeImageProcessor();
        image.CompressFailures["full.jpg"] = diskError;
        var workflow = new BatchCompressWorkflow(CreateServices(image: image));

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest(
                [new LocalPath("full.jpg"), new LocalPath("pending.jpg")],
                CompressionProfile.BalancedDefault(),
                OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Failed, result.Value!.BatchResult.Status);
        Assert.Equal(AtomPixErrorCode.InsufficientDiskSpace, result.Value.BatchResult.Error!.Code);
        Assert.Equal(2, result.Value.BatchResult.TotalCount);
        Assert.Equal(1, result.Value.BatchResult.CompletedCount);
        Assert.Single(result.Value.ItemResults);
        Assert.False(result.Value.FinalProgress.IsCompleted);
        Assert.Single(image.CompressRequests);
        Assert.EndsWith("full.jpg", image.CompressRequests[0].InputPath.Value);
    }


    [Fact]
    public async Task CompressWithDefaultSettingsWorkflow_returns_settings_failure_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(new FakeFileSystemService(), image);
        var store = new FakeAppSettingsStore { LoadFailure = new AtomPixError(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "failed") };
        var workflow = new CompressWithDefaultSettingsWorkflow(store, new CompressImageWorkflow(services));

        var result = await workflow.ExecuteAsync(new CompressWithDefaultSettingsRequest(new LocalPath("a.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.CompressCalled);
    }

    [Fact]
    public async Task BatchConvertWithDefaultSettingsWorkflow_returns_settings_failure_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(new FakeFileSystemService(), image);
        var store = new FakeAppSettingsStore { LoadFailure = new AtomPixError(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "failed") };
        var workflow = new BatchConvertWithDefaultSettingsWorkflow(store, new BatchConvertWorkflow(services));

        var result = await workflow.ExecuteAsync(new BatchConvertWithDefaultSettingsRequest([new LocalPath("a.png")]), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.ConvertCalled);
    }

    [Fact]
    public async Task CompressImageWorkflow_processing_cancel_returns_canceled_job_result()
    {
        var image = new FakeImageProcessor { CancelForInput = "cancel.jpg" };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("cancel.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Canceled, result.Value!.JobResult.Status);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, result.Value.JobResult.Error!.Code);
        Assert.Equal(100, result.Value.JobResult.InputSizeBytes);
        Assert.Null(result.Value.JobResult.OutputSizeBytes);
    }

    [Fact]
    public async Task BatchCompressWorkflow_midway_cancel_keeps_completed_items_and_stops()
    {
        using var cts = new CancellationTokenSource();
        var image = new FakeImageProcessor { CancelSourceAfterCompressSuccess = cts };
        var services = CreateServices(new FakeFileSystemService(), image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg"), new LocalPath("c.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            cts.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Canceled, result.Value!.BatchResult.Status);
        Assert.Equal(3, result.Value.BatchResult.TotalCount);
        Assert.Equal(1, result.Value.BatchResult.CompletedCount);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(0, result.Value.BatchResult.CanceledCount);
        Assert.False(result.Value.FinalProgress.IsCompleted);
        Assert.Equal(1.0 / 3.0, result.Value.FinalProgress.CompletionRatio);
    }

    [Fact]
    public async Task BatchCompressWorkflow_mixed_success_failed_skipped_and_canceled_counts_are_consistent()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "skip_atompix_003.jpg"));
        var image = new FakeImageProcessor
        {
            FailForInput = "bad.jpg",
            CancelForInput = "cancel.jpg"
        };
        var workflow = new BatchCompressWorkflow(CreateServices(fs, image));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest([new LocalPath("good.jpg"), new LocalPath("bad.jpg"), new LocalPath("skip.jpg"), new LocalPath("cancel.jpg")], CompressionProfile.BalancedDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Canceled, result.Value!.BatchResult.Status);
        Assert.Equal(4, result.Value.BatchResult.TotalCount);
        Assert.Equal(4, result.Value.BatchResult.CompletedCount);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.FailedCount);
        Assert.Equal(1, result.Value.BatchResult.SkippedCount);
        Assert.Equal(1, result.Value.BatchResult.CanceledCount);
        Assert.Equal(result.Value.BatchResult.TotalCount, result.Value.FinalProgress.TotalCount);
        Assert.Equal(result.Value.BatchResult.CompletedCount, result.Value.FinalProgress.CompletedCount);
        Assert.Equal(result.Value.BatchResult.SucceededCount, result.Value.FinalProgress.SucceededCount);
        Assert.Equal(result.Value.BatchResult.FailedCount, result.Value.FinalProgress.FailedCount);
        Assert.Equal(result.Value.BatchResult.SkippedCount, result.Value.FinalProgress.SkippedCount);
        Assert.Equal(result.Value.BatchResult.CanceledCount, result.Value.FinalProgress.CanceledCount);
        Assert.True(result.Value.FinalProgress.IsCompleted);
    }

    [Fact]
    public async Task BatchConvertWorkflow_mixed_success_failed_skipped_and_canceled_counts_are_consistent()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "skip_atompix_003.webp"));
        var image = new FakeImageProcessor
        {
            FailConvertForInput = "bad.png",
            CancelConvertForInput = "cancel.png"
        };
        var workflow = new BatchConvertWorkflow(CreateServices(fs, image));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);

        var result = await workflow.ExecuteAsync(
            new BatchConvertRequest([new LocalPath("good.png"), new LocalPath("bad.png"), new LocalPath("skip.png"), new LocalPath("cancel.png")], ConversionProfile.WebPDefault(), policy),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Canceled, result.Value!.BatchResult.Status);
        Assert.Equal(4, result.Value.BatchResult.TotalCount);
        Assert.Equal(4, result.Value.BatchResult.CompletedCount);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.FailedCount);
        Assert.Equal(1, result.Value.BatchResult.SkippedCount);
        Assert.Equal(1, result.Value.BatchResult.CanceledCount);
        Assert.Equal(result.Value.BatchResult.TotalCount, result.Value.FinalProgress.TotalCount);
        Assert.Equal(result.Value.BatchResult.CompletedCount, result.Value.FinalProgress.CompletedCount);
        Assert.Equal(result.Value.BatchResult.SucceededCount, result.Value.FinalProgress.SucceededCount);
        Assert.Equal(result.Value.BatchResult.FailedCount, result.Value.FinalProgress.FailedCount);
        Assert.Equal(result.Value.BatchResult.SkippedCount, result.Value.FinalProgress.SkippedCount);
        Assert.Equal(result.Value.BatchResult.CanceledCount, result.Value.FinalProgress.CanceledCount);
        Assert.True(result.Value.FinalProgress.IsCompleted);
    }

    [Fact]
    public async Task BatchConvertWorkflow_single_preflight_cancel_records_canceled_item()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var workflow = new BatchConvertWorkflow(CreateServices());

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([new LocalPath("a.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, result.Error!.Code);
    }
    [Fact]
    public async Task BatchCompressWithDefaultSettingsWorkflow_uses_settings()
    {
        var workflow = new BatchCompressWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new BatchCompressWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(new BatchCompressWithDefaultSettingsRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg")]), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Result.BatchResult.TotalCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
    }

    [Fact]
    public async Task BatchConvertWithDefaultSettingsWorkflow_uses_settings()
    {
        var workflow = new BatchConvertWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new BatchConvertWorkflow(CreateServices()));

        var result = await workflow.ExecuteAsync(new BatchConvertWithDefaultSettingsRequest([new LocalPath("a.png"), new LocalPath("b.png")]), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Result.BatchResult.TotalCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
    }

    [Fact]
    public async Task ResizeImageWorkflow_resolves_policy_in_core_then_executes_exact_target()
    {
        var image = new FakeImageProcessor();
        var workflow = new ResizeImageWorkflow(CreateServices(image: image));

        var result = await workflow.ExecuteAsync(
            new ResizeImageRequest(
                new LocalPath("a.jpg"),
                new PixelResizePolicy(5, null, maintainAspectRatio: true),
                OutputPolicy.Default,
                SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ResizeCalled);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.Equal(new ImageSize(10, 10), result.Value.InputSize);
        Assert.Equal(new ResolvedResizeSize(5, 5), result.Value.TargetSize);
        Assert.Equal(new ImageSize(5, 5), result.Value.ActualOutputSize);
    }

    [Fact]
    public async Task ResizeImageWorkflow_still_encodes_when_prevent_upscaling_keeps_original_size()
    {
        var image = new FakeImageProcessor();
        var workflow = new ResizeImageWorkflow(CreateServices(image: image));

        var result = await workflow.ExecuteAsync(
            new ResizeImageRequest(
                new LocalPath("a.jpg"),
                new PixelResizePolicy(20, 20, maintainAspectRatio: true, preventUpscaling: true),
                OutputPolicy.Default,
                SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.ResizeCalled);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.Equal(new ResolvedResizeSize(10, 10), result.Value.TargetSize);
        Assert.Equal(new ImageSize(10, 10), result.Value.ActualOutputSize);
    }

    [Fact]
    public async Task CropImageWorkflow_rejects_out_of_bounds_area_before_processing()
    {
        var image = new FakeImageProcessor();
        var workflow = new CropImageWorkflow(CreateServices(image: image));

        var result = await workflow.ExecuteAsync(
            new CropImageRequest(
                new LocalPath("a.jpg"),
                new CropRectangle(8, 8, 3, 3),
                OutputPolicy.Default,
                SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidCropOptions, result.Error!.Code);
        Assert.False(image.CropCalled);
    }

    [Fact]
    public async Task CropImageWorkflow_executes_valid_rectangle_and_preserves_the_plan()
    {
        var image = new FakeImageProcessor();
        var workflow = new CropImageWorkflow(CreateServices(image: image));
        var cropArea = new CropRectangle(1, 2, 5, 4);

        var result = await workflow.ExecuteAsync(
            new CropImageRequest(new LocalPath("a.jpg"), cropArea, OutputPolicy.Default, SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(image.CropCalled);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.Same(cropArea, result.Value.CropArea);
        Assert.Equal(new ImageSize(5, 4), result.Value.ActualOutputSize);
    }

    [Fact]
    public async Task OpenFolderWorkflow_builds_naturally_sorted_lightweight_browser_collection()
    {
        var fs = new FakeFileSystemService();
        var image = new FakeImageProcessor();
        var directory = Path.GetFullPath("gallery");
        fs.DirectoryFiles[directory] =
        [
            new LocalPath(Path.Combine(directory, "photo10.jpg")),
            new LocalPath(Path.Combine(directory, "notes.txt")),
            new LocalPath(Path.Combine(directory, "photo2.png")),
            new LocalPath(Path.Combine(directory, ".", "photo2.png"))
        ];
        var workflow = new OpenFolderWorkflow(fs, image);

        var result = await workflow.ExecuteAsync(new OpenFolderRequest(new LocalPath("gallery")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new LocalPath(directory), result.Value!.DirectoryPath);
        Assert.Equal(["photo2.png", "photo10.jpg"], result.Value.Items.Select(item => item.DisplayName));
        Assert.All(result.Value.Items, item => Assert.True(Path.IsPathFullyQualified(item.Path.Value)));
        Assert.Equal(1, result.Value.UnsupportedFileCount);
        Assert.False(image.ProbeCalled);
    }

    [Fact]
    public async Task AppendBatchInputsWorkflow_appends_multiple_sources_and_reports_skips()
    {
        var fs = new FakeFileSystemService();
        var image = new FakeImageProcessor();
        var directory = Path.GetFullPath("batch-folder");
        var existing = new LocalPath(Path.GetFullPath("existing.jpg"));
        var selected = new LocalPath(Path.GetFullPath("selected.png"));
        var missing = new LocalPath(Path.GetFullPath("missing.jpg"));
        var unsupported = new LocalPath(Path.GetFullPath("notes.txt"));
        var b10 = new LocalPath(Path.Combine(directory, "b10.jpg"));
        var b2 = new LocalPath(Path.Combine(directory, "b2.jpg"));
        foreach (var path in new[] { selected, unsupported, b10, b2 }) fs.ExistingFiles.Add(path.Value);
        fs.DirectoryFiles[directory] = [b2, b10];
        var workflow = new AppendBatchInputsWorkflow(fs, image);

        var result = await workflow.ExecuteAsync(
            new AppendBatchInputsRequest(
                [existing],
                [selected, missing, selected, unsupported],
                [new LocalPath(directory)]),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([existing, selected, b10, b2], result.Value!.InputPaths);
        Assert.Equal(3, result.Value.AddedCount);
        Assert.Equal(1, result.Value.DuplicateCount);
        Assert.Equal(1, result.Value.UnsupportedCount);
        Assert.Equal(1, result.Value.UnreadableCount);
        Assert.Contains(result.Value.SkippedItems, item => item.Path == missing && item.Reason == BatchInputSkipReason.Missing);
        Assert.False(image.ProbeCalled);
    }

    [Fact]
    public async Task AppendBatchInputsWorkflow_rejects_recursive_mode_without_changing_inputs()
    {
        var workflow = new AppendBatchInputsWorkflow(new FakeFileSystemService(), new FakeImageProcessor());

        var result = await workflow.ExecuteAsync(
            new AppendBatchInputsRequest([], [], [new LocalPath("folder")], IncludeSubdirectories: true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidInputPath, result.Error!.Code);
    }

    private static ImageWorkflowServices CreateServices(FakeFileSystemService? fs = null, FakeImageProcessor? image = null)
    {
        return new ImageWorkflowServices(
            image ?? new FakeImageProcessor(),
            fs ?? new FakeFileSystemService());
    }

}

internal sealed class FakeImageProcessor : IImageProcessor
{
    public bool ProbeCalled { get; private set; }
    public bool PreviewCalled { get; private set; }
    public bool CompressCalled { get; private set; }
    public bool ConvertCalled { get; private set; }
    public bool ResizeCalled { get; private set; }
    public bool CropCalled { get; private set; }
    public string? FailForInput { get; set; }
    public string? FailConvertForInput { get; set; }
    public string? CancelForInput { get; set; }
    public string? CancelConvertForInput { get; set; }
    public CancellationTokenSource? CancelSourceAfterCompressSuccess { get; set; }
    public string? ProbeFailForInput { get; set; }
    public string? PreviewFailForInput { get; set; }
    public ImageFormatKind ProbeFormat { get; set; } = ImageFormatKind.Jpeg;
    public bool ProbeIsAnimated { get; set; }
    public bool ThrowOnProbe { get; set; }

    public Dictionary<string, ImageSize> ProbeSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ImageProbeRequest> ProbeRequests { get; } = [];

    public List<ImageCompressRequest> CompressRequests { get; } = [];

    public Dictionary<string, AtomPixError> CompressFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ImageResizeRequest> ResizeRequests { get; } = [];

    public ImageProcessorCapabilities Capabilities { get; set; } = new(
        new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP },
        new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg, OutputImageFormat.Png, OutputImageFormat.WebP },
        true,
        false,
        new ImageResourceCapabilities(1024 * 1024, 10000, 10000, 100_000_000, 10000, 10000, 100_000_000),
        new ImageResizeCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP, ImageFormatKind.Bmp },
            10000,
            10000,
            100_000_000),
        new ImageCropCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP, ImageFormatKind.Bmp },
            10000,
            10000,
            100_000_000));

    public Task<OperationResult<ImageProbeResult>> ProbeAsync(ImageProbeRequest request, CancellationToken cancellationToken)
    {
        ProbeCalled = true;
        ProbeRequests.Add(request);
        if (ThrowOnProbe) throw new InvalidOperationException($"Unexpected failure for {request.InputPath.Value}");
        if (MatchesConfiguredPath(request.InputPath, ProbeFailForInput))
        {
            return Task.FromResult(OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "invalid image")));
        }

        var size = ProbeSizes.TryGetValue(Path.GetFileName(request.InputPath.Value), out var configuredSize)
            ? configuredSize
            : new ImageSize(10, 10);
        return Task.FromResult(OperationResult<ImageProbeResult>.Success(new ImageProbeResult(
            request.InputPath,
            ProbeFormat,
            size.Width,
            size.Height,
            100,
            false,
            false,
            ProbeIsAnimated,
            ProbeIsAnimated ? 2 : 1,
            false,
            false)));
    }

    public Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken)
    {
        PreviewCalled = true;
        if (MatchesConfiguredPath(request.InputPath, PreviewFailForInput))
        {
            return Task.FromResult(OperationResult<ImagePreviewResult>.Failure(new AtomPixError(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "invalid image")));
        }

        return Task.FromResult(OperationResult<ImagePreviewResult>.Success(new ImagePreviewResult([1, 2, 3], "image/jpeg", 10, 10)));
    }

    public Task<OperationResult<ImageCompressResult>> CompressAsync(ImageCompressRequest request, CancellationToken cancellationToken)
    {
        CompressCalled = true;
        CompressRequests.Add(request);
        if (CompressFailures.TryGetValue(Path.GetFileName(request.InputPath.Value), out var configuredFailure))
        {
            return Task.FromResult(OperationResult<ImageCompressResult>.Failure(configuredFailure));
        }
        if (MatchesConfiguredPath(request.InputPath, FailForInput))
        {
            return Task.FromResult(OperationResult<ImageCompressResult>.Failure(new AtomPixError(AtomPixErrorCode.ImageCompressFailed, AtomPixErrorCategory.ImageProcessing, "failed")));
        }

        if (MatchesConfiguredPath(request.InputPath, CancelForInput))
        {
            return Task.FromResult(OperationResult<ImageCompressResult>.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled")));
        }

        var result = OperationResult<ImageCompressResult>.Success(new ImageCompressResult(
            request.InputPath,
            request.OutputPath,
            ImageFormatKind.Jpeg,
            ImageFormatKind.Jpeg,
            100,
            70,
            new ImageQuality(80)));
        CancelSourceAfterCompressSuccess?.Cancel();
        CancelSourceAfterCompressSuccess = null;
        return Task.FromResult(result);
    }

    public Task<OperationResult<ImageConvertResult>> ConvertAsync(ImageConvertRequest request, CancellationToken cancellationToken)
    {
        ConvertCalled = true;
        if (MatchesConfiguredPath(request.InputPath, FailConvertForInput))
        {
            return Task.FromResult(OperationResult<ImageConvertResult>.Failure(new AtomPixError(AtomPixErrorCode.ImageConvertFailed, AtomPixErrorCategory.ImageProcessing, "failed")));
        }

        if (MatchesConfiguredPath(request.InputPath, CancelConvertForInput))
        {
            return Task.FromResult(OperationResult<ImageConvertResult>.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled")));
        }

        return Task.FromResult(OperationResult<ImageConvertResult>.Success(new ImageConvertResult(
            request.InputPath,
            request.OutputPath,
            ImageFormatKind.Png,
            ImageFormatKind.WebP,
            100,
            60,
            new TransparencyProcessingResult(TransparencyOutcome.NotPresent, null))));
    }

    public Task<OperationResult<ImageResizeResult>> ResizeAsync(ImageResizeRequest request, CancellationToken cancellationToken)
    {
        ResizeCalled = true;
        ResizeRequests.Add(request);
        var inputSize = ProbeSizes.TryGetValue(Path.GetFileName(request.InputPath.Value), out var configuredSize)
            ? configuredSize
            : new ImageSize(10, 10);
        return Task.FromResult(OperationResult<ImageResizeResult>.Success(new ImageResizeResult(
            request.InputPath,
            request.OutputPath,
            ProbeFormat,
            inputSize,
            request.TargetSize.ToImageSize(),
            100,
            80)));
    }

    public Task<OperationResult<ImageCropResult>> CropAsync(ImageCropRequest request, CancellationToken cancellationToken)
    {
        CropCalled = true;
        return Task.FromResult(OperationResult<ImageCropResult>.Success(new ImageCropResult(
            request.InputPath,
            request.OutputPath,
            ProbeFormat,
            new ImageSize(10, 10),
            new ImageSize(request.CropArea.Width, request.CropArea.Height),
            100,
            75)));
    }

    private static bool MatchesConfiguredPath(LocalPath actual, string? configured) =>
        configured is not null
        && (string.Equals(actual.Value, configured, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(actual.Value), Path.GetFileName(configured), StringComparison.OrdinalIgnoreCase));

    public void SetResources(ImageResourceCapabilities resources) =>
        Capabilities = new ImageProcessorCapabilities(
            Capabilities.SupportedInputFormats,
            Capabilities.SupportedOutputFormats,
            Capabilities.SupportsMetadata,
            Capabilities.SupportsAnimatedImages,
            resources,
            Capabilities.Resize,
            Capabilities.Crop);
}

internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Items { get; } = [];

    public void Report(T value) => Items.Add(value);
}

internal sealed class FakeFileSystemService : IFileSystemService
{
    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<LocalPath>> DirectoryFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailCreateDirectory { get; set; }

    public bool FailFileSize { get; set; }

    public Dictionary<string, long> FileSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CreateDirectoryCallCount { get; private set; }

    public bool FileExists(LocalPath path) => ExistingFiles.Any(existing => PathsEqual(new LocalPath(existing), path));

    public bool DirectoryExists(LocalPath path) => true;

    public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken)
    {
        CreateDirectoryCallCount++;
        return Task.FromResult(FailCreateDirectory
            ? OperationResult.Failure(new AtomPixError(AtomPixErrorCode.OutputDirectoryNotFound, AtomPixErrorCategory.FileSystem, "failed"))
            : OperationResult.Success());
    }

    public Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken) =>
        Task.FromResult(FailFileSize
            ? OperationResult<long>.Failure(new AtomPixError(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "missing"))
            : OperationResult<long>.Success(FileSizes.TryGetValue(Path.GetFileName(path.Value), out var size) ? size : 100));

    public Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesAsync(LocalPath directory, CancellationToken cancellationToken)
    {
        var files = DirectoryFiles
            .FirstOrDefault(item => PathsEqual(new LocalPath(item.Key), directory))
            .Value;
        return Task.FromResult(OperationResult<IReadOnlyList<LocalPath>>.Success(files ?? Array.Empty<LocalPath>()));
    }

    public OperationResult<LocalPath> NormalizePath(LocalPath path) =>
        OperationResult<LocalPath>.Success(new LocalPath(Path.GetFullPath(path.Value)));

    public bool PathsEqual(LocalPath left, LocalPath right) =>
        string.Equals(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value), StringComparison.OrdinalIgnoreCase);

    public int ComparePaths(LocalPath left, LocalPath right) =>
        StringComparer.OrdinalIgnoreCase.Compare(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));

    public LocalPath Combine(LocalPath directory, string fileName) => new(Path.Combine(directory.Value, fileName));

    public string GetFileName(LocalPath path) => Path.GetFileName(path.Value);

    public string GetFileNameWithoutExtension(LocalPath path) => Path.GetFileNameWithoutExtension(path.Value);

    public string GetExtension(LocalPath path) => Path.GetExtension(path.Value);

    public LocalPath ChangeExtension(LocalPath path, string extension) => new(Path.ChangeExtension(path.Value, extension));

    public LocalPath BuildIndexedPath(LocalPath basePath, int index)
    {
        var directory = Path.GetDirectoryName(basePath.Value) ?? ".";
        var file = Path.GetFileNameWithoutExtension(basePath.Value);
        var ext = Path.GetExtension(basePath.Value);
        return new LocalPath(Path.Combine(directory, $"{file}_{index}{ext}"));
    }
}

internal sealed class FakeAppSettingsStore : IAppSettingsStore
{
    public bool Saved { get; private set; }

    public AtomPixError? LoadFailure { get; set; }
    public AtomPixError? SaveFailure { get; set; }

    public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(LoadFailure is null
        ? OperationResult<AppSettings>.Success(AppSettings.Default)
        : OperationResult<AppSettings>.Failure(LoadFailure));

    public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Saved = true;
        return Task.FromResult(SaveFailure is null ? OperationResult.Success() : OperationResult.Failure(SaveFailure));
    }
}
internal sealed class FakeRecentItemsStore : IRecentItemsStore
{
    private IReadOnlyList<RecentItem> _items;

    public FakeRecentItemsStore(IReadOnlyList<RecentItem> items)
    {
        _items = items;
    }

    public bool Saved { get; private set; }
    public AtomPixError? LoadFailure { get; set; }
    public AtomPixError? SaveFailure { get; set; }

    public Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LoadFailure is null
            ? OperationResult<IReadOnlyList<RecentItem>>.Success(_items)
            : OperationResult<IReadOnlyList<RecentItem>>.Failure(LoadFailure));

    public Task<OperationResult> SaveAsync(IReadOnlyList<RecentItem> items, CancellationToken cancellationToken)
    {
        Saved = true;
        _items = items;
        return Task.FromResult(SaveFailure is null ? OperationResult.Success() : OperationResult.Failure(SaveFailure));
    }
}









