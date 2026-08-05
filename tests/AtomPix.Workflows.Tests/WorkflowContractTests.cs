namespace AtomPix.Workflows.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Licensing;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;
using AtomPix.Workflows.Settings;

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
    public async Task BatchCompressWorkflow_denies_free_subscription()
    {
        var services = CreateServices(subscription: SubscriptionState.Free);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.FeatureNotAvailable, result.Error!.Code);
    }

    [Fact]
    public async Task Single_image_workflows_allow_free_subscription()
    {
        var compressImage = new FakeImageProcessor();
        var compress = new CompressImageWorkflow(CreateServices(subscription: SubscriptionState.Free, image: compressImage));
        var convertImage = new FakeImageProcessor();
        var convert = new ConvertImageWorkflow(CreateServices(subscription: SubscriptionState.Free, image: convertImage));

        var compressResult = await compress.ExecuteAsync(new CompressImageRequest(new LocalPath("free.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);
        var convertResult = await convert.ExecuteAsync(new ConvertImageRequest(new LocalPath("free.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(compressResult.Succeeded);
        Assert.True(convertResult.Succeeded);
        Assert.True(compressImage.CompressCalled);
        Assert.True(convertImage.ConvertCalled);
    }

    [Fact]
    public async Task BatchConvertWorkflow_denies_free_subscription_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(subscription: SubscriptionState.Free, image: image);
        var workflow = new BatchConvertWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([new LocalPath("a.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.FeatureNotAvailable, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.ConvertCalled);
    }

    [Fact]
    public async Task BatchCompressWorkflow_denies_expired_paid_feature_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(subscription: ExpiredSubscription(), image: image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SubscriptionExpired, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.CompressCalled);
    }

    [Fact]
    public async Task CompressImageWorkflow_auto_renames_existing_target()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var services = CreateServices(fs, ActiveSubscription());
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.JobResult.Status);
        Assert.EndsWith("a_atompix_1.jpg", result.Value.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task CompressImageWorkflow_skip_returns_skipped_job()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "a_atompix.jpg"));
        var policy = new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, OverwritePolicy.Skip);
        var services = CreateServices(fs, ActiveSubscription());
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Skipped, result.Value!.JobResult.Status);
        Assert.False(((FakeImageProcessor)services.ImageProcessor).CompressCalled);
    }

    [Fact]
    public async Task ConvertImageWorkflow_uses_target_format_extension()
    {
        var services = CreateServices(subscription: ActiveSubscription());
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("a.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("a_atompix.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }


    [Fact]
    public async Task ConvertImageWorkflow_same_as_input_keep_name_preserves_directory_and_changes_extension()
    {
        var services = CreateServices(subscription: ActiveSubscription());
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
        var services = CreateServices(subscription: ActiveSubscription());
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
        var services = CreateServices(fs, ActiveSubscription());
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
        var services = CreateServices(fs, ActiveSubscription());
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("archive.photo.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("archive.photo_atompix_2.webp", result.Value!.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task CompressImageWorkflow_custom_directory_keep_name_uses_input_extension()
    {
        var services = CreateServices(subscription: ActiveSubscription());
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
        var services = CreateServices(subscription: ActiveSubscription());
        var workflow = new ConvertImageWorkflow(services);
        var policy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.Subfolder, null, "Exports"),
            new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
            OverwritePolicy.AutoRename);
        var profile = new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), ResizePolicy.None, MetadataPolicy.Remove);

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
        var services = CreateServices(fs, ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        Assert.Throws<ArgumentNullException>(() => new ImageWorkflowServices(null!, new FakeSubscriptionStore(ActiveSubscription()), new DefaultFeatureAccessPolicy(), new FakeFileSystemService()));
    }

    [Fact]
    public async Task CompressImageWorkflow_returns_failure_when_input_size_fails()
    {
        var fs = new FakeFileSystemService { FailFileSize = true };
        var services = CreateServices(fs, ActiveSubscription());
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("missing.jpg"), CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task CompressImageWorkflow_returns_failure_when_output_directory_creation_fails()
    {
        var fs = new FakeFileSystemService { FailCreateDirectory = true };
        var services = CreateServices(fs, ActiveSubscription());
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
        var services = CreateServices(fs, ActiveSubscription());
        var workflow = new CompressImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new CompressImageRequest(new LocalPath("a.jpg"), CompressionProfile.BalancedDefault(), policy), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("a_atompix.jpg", result.Value!.JobResult.OutputPath!.Value.Value);
    }


    [Fact]
    public async Task CompressImageWorkflow_processing_failure_returns_failed_job_with_input_size_and_output_path()
    {
        var image = new FakeImageProcessor { FailForInput = "bad.jpg" };
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
            new ConversionProfile((OutputImageFormat)999, null, ResizePolicy.None, MetadataPolicy.Remove));
    }

    [Fact]
    public async Task BatchConvertWorkflow_rejects_empty_input_list()
    {
        var workflow = new BatchConvertWorkflow(CreateServices(subscription: ActiveSubscription()));

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
    public async Task BatchCompressWorkflow_returns_subscription_load_failure_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = new ImageWorkflowServices(
            image,
            new FakeSubscriptionStore(ActiveSubscription()) { LoadFailure = new AtomPixError(AtomPixErrorCode.SubscriptionLoadFailed, AtomPixErrorCategory.Configuration, "failed") },
            new DefaultFeatureAccessPolicy(),
            new FakeFileSystemService());
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SubscriptionLoadFailed, result.Error!.Code);
        Assert.False(image.ProbeCalled);
        Assert.False(image.CompressCalled);
    }

    [Fact]
    public async Task CompressWithDefaultSettingsWorkflow_uses_settings_profiles_and_output_policy()
    {
        var services = CreateServices(subscription: ActiveSubscription());
        var workflow = new CompressWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new CompressImageWorkflow(services));

        var result = await workflow.ExecuteAsync(new CompressWithDefaultSettingsRequest(new LocalPath("a.jpg")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, result.Value!.Result.JobResult.Status);
        Assert.EndsWith("a_atompix.jpg", result.Value.Result.JobResult.OutputPath!.Value.Value);
    }

    [Fact]
    public async Task ConvertWithDefaultSettingsWorkflow_uses_settings_profiles_and_output_policy()
    {
        var services = CreateServices(subscription: ActiveSubscription());
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
                false)
        };
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
        var workflow = new ConvertImageWorkflow(services);

        var result = await workflow.ExecuteAsync(new ConvertImageRequest(new LocalPath("a.png"), ConversionProfile.WebPDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedOutputFormat, result.Error!.Code);
    }

    [Fact]
    public async Task BatchCompressWorkflow_returns_final_progress()
    {
        var workflow = new BatchCompressWorkflow(CreateServices(subscription: ActiveSubscription()));

        var result = await workflow.ExecuteAsync(new BatchCompressRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.FinalProgress.IsCompleted);
        Assert.Equal(2, result.Value.FinalProgress.TotalCount);
        Assert.Equal(2, result.Value.FinalProgress.SucceededCount);
    }


    [Fact]
    public async Task CompressWithDefaultSettingsWorkflow_returns_settings_failure_without_processing()
    {
        var image = new FakeImageProcessor();
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
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
        var services = CreateServices(new FakeFileSystemService(), ActiveSubscription(), image);
        var workflow = new BatchCompressWorkflow(services);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg"), new LocalPath("c.jpg")], CompressionProfile.BalancedDefault(), OutputPolicy.Default),
            cts.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Canceled, result.Value!.BatchResult.Status);
        Assert.Equal(3, result.Value.BatchResult.TotalCount);
        Assert.Equal(2, result.Value.BatchResult.CompletedCount);
        Assert.Equal(1, result.Value.BatchResult.SucceededCount);
        Assert.Equal(1, result.Value.BatchResult.CanceledCount);
        Assert.False(result.Value.FinalProgress.IsCompleted);
        Assert.Equal(2.0 / 3.0, result.Value.FinalProgress.CompletionRatio);
    }

    [Fact]
    public async Task BatchCompressWorkflow_mixed_success_failed_skipped_and_canceled_counts_are_consistent()
    {
        var fs = new FakeFileSystemService();
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "skip_atompix.jpg"));
        var image = new FakeImageProcessor
        {
            FailForInput = "bad.jpg",
            CancelForInput = "cancel.jpg"
        };
        var workflow = new BatchCompressWorkflow(CreateServices(fs, ActiveSubscription(), image));
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
        fs.ExistingFiles.Add(Path.Combine("AtomPix_Output", "skip_atompix.webp"));
        var image = new FakeImageProcessor
        {
            FailConvertForInput = "bad.png",
            CancelConvertForInput = "cancel.png"
        };
        var workflow = new BatchConvertWorkflow(CreateServices(fs, ActiveSubscription(), image));
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
        var workflow = new BatchConvertWorkflow(CreateServices(subscription: ActiveSubscription()));

        var result = await workflow.ExecuteAsync(new BatchConvertRequest([new LocalPath("a.png")], ConversionProfile.WebPDefault(), OutputPolicy.Default), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, result.Error!.Code);
    }
    [Fact]
    public async Task BatchCompressWithDefaultSettingsWorkflow_uses_settings()
    {
        var workflow = new BatchCompressWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new BatchCompressWorkflow(CreateServices(subscription: ActiveSubscription())));

        var result = await workflow.ExecuteAsync(new BatchCompressWithDefaultSettingsRequest([new LocalPath("a.jpg"), new LocalPath("b.jpg")]), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Result.BatchResult.TotalCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
    }

    [Fact]
    public async Task BatchConvertWithDefaultSettingsWorkflow_uses_settings()
    {
        var workflow = new BatchConvertWithDefaultSettingsWorkflow(new FakeAppSettingsStore(), new BatchConvertWorkflow(CreateServices(subscription: ActiveSubscription())));

        var result = await workflow.ExecuteAsync(new BatchConvertWithDefaultSettingsRequest([new LocalPath("a.png"), new LocalPath("b.png")]), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Result.BatchResult.TotalCount);
        Assert.True(result.Value.Result.FinalProgress.IsCompleted);
    }
    private static ImageWorkflowServices CreateServices(FakeFileSystemService? fs = null, SubscriptionState? subscription = null, FakeImageProcessor? image = null)
    {
        return new ImageWorkflowServices(
            image ?? new FakeImageProcessor(),
            new FakeSubscriptionStore(subscription ?? ActiveSubscription()),
            new DefaultFeatureAccessPolicy(),
            fs ?? new FakeFileSystemService());
    }

    private static SubscriptionState ActiveSubscription() => new(SubscriptionStatus.Active, BillingCycle.Monthly, DateTimeOffset.UtcNow.AddMonths(1));

    private static SubscriptionState ExpiredSubscription() => new(SubscriptionStatus.Expired, BillingCycle.Monthly, DateTimeOffset.UtcNow.AddDays(-1));
}

internal sealed class FakeImageProcessor : IImageProcessor
{
    public bool ProbeCalled { get; private set; }
    public bool PreviewCalled { get; private set; }
    public bool CompressCalled { get; private set; }
    public bool ConvertCalled { get; private set; }
    public string? FailForInput { get; set; }
    public string? FailConvertForInput { get; set; }
    public string? CancelForInput { get; set; }
    public string? CancelConvertForInput { get; set; }
    public CancellationTokenSource? CancelSourceAfterCompressSuccess { get; set; }
    public string? ProbeFailForInput { get; set; }
    public string? PreviewFailForInput { get; set; }
    public ImageFormatKind ProbeFormat { get; set; } = ImageFormatKind.Jpeg;
    public bool ProbeIsAnimated { get; set; }

    public ImageProcessorCapabilities Capabilities { get; set; } = new(
        new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP },
        new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg, OutputImageFormat.Png, OutputImageFormat.WebP },
        true,
        false);

    public Task<OperationResult<ImageProbeResult>> ProbeAsync(ImageProbeRequest request, CancellationToken cancellationToken)
    {
        ProbeCalled = true;
        if (request.InputPath.Value == ProbeFailForInput)
        {
            return Task.FromResult(OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "invalid image")));
        }

        return Task.FromResult(OperationResult<ImageProbeResult>.Success(new ImageProbeResult(request.InputPath, ProbeFormat, 10, 10, 100, false, ProbeIsAnimated, ProbeIsAnimated ? 2 : 1, false)));
    }

    public Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken)
    {
        PreviewCalled = true;
        if (request.InputPath.Value == PreviewFailForInput)
        {
            return Task.FromResult(OperationResult<ImagePreviewResult>.Failure(new AtomPixError(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "invalid image")));
        }

        return Task.FromResult(OperationResult<ImagePreviewResult>.Success(new ImagePreviewResult([1, 2, 3], "image/jpeg", 10, 10)));
    }

    public Task<OperationResult<ImageCompressResult>> CompressAsync(ImageCompressRequest request, CancellationToken cancellationToken)
    {
        CompressCalled = true;
        if (request.InputPath.Value == FailForInput)
        {
            return Task.FromResult(OperationResult<ImageCompressResult>.Failure(new AtomPixError(AtomPixErrorCode.ImageCompressFailed, AtomPixErrorCategory.ImageProcessing, "failed")));
        }

        if (request.InputPath.Value == CancelForInput)
        {
            return Task.FromResult(OperationResult<ImageCompressResult>.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled")));
        }

        var result = OperationResult<ImageCompressResult>.Success(new ImageCompressResult(request.InputPath, request.OutputPath, ImageFormatKind.Jpeg, 100, 70));
        CancelSourceAfterCompressSuccess?.Cancel();
        CancelSourceAfterCompressSuccess = null;
        return Task.FromResult(result);
    }

    public Task<OperationResult<ImageConvertResult>> ConvertAsync(ImageConvertRequest request, CancellationToken cancellationToken)
    {
        ConvertCalled = true;
        if (request.InputPath.Value == FailConvertForInput)
        {
            return Task.FromResult(OperationResult<ImageConvertResult>.Failure(new AtomPixError(AtomPixErrorCode.ImageConvertFailed, AtomPixErrorCategory.ImageProcessing, "failed")));
        }

        if (request.InputPath.Value == CancelConvertForInput)
        {
            return Task.FromResult(OperationResult<ImageConvertResult>.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled")));
        }

        return Task.FromResult(OperationResult<ImageConvertResult>.Success(new ImageConvertResult(request.InputPath, request.OutputPath, ImageFormatKind.Png, ImageFormatKind.WebP, 100, 60)));
    }
}

internal sealed class FakeFileSystemService : IFileSystemService
{
    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailCreateDirectory { get; set; }

    public bool FailFileSize { get; set; }

    public bool FileExists(LocalPath path) => ExistingFiles.Contains(path.Value);

    public bool DirectoryExists(LocalPath path) => true;

    public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken) =>
        Task.FromResult(FailCreateDirectory
            ? OperationResult.Failure(new AtomPixError(AtomPixErrorCode.OutputDirectoryNotFound, AtomPixErrorCategory.FileSystem, "failed"))
            : OperationResult.Success());

    public Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken) =>
        Task.FromResult(FailFileSize
            ? OperationResult<long>.Failure(new AtomPixError(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "missing"))
            : OperationResult<long>.Success(100));

    public LocalPath Combine(LocalPath directory, string fileName) => new(Path.Combine(directory.Value, fileName));

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

internal sealed class FakeSubscriptionStore : ISubscriptionStore
{
    private readonly SubscriptionState _state;

    public FakeSubscriptionStore(SubscriptionState state) => _state = state;

    public AtomPixError? LoadFailure { get; set; }

    public Task<OperationResult<SubscriptionState>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(LoadFailure is null
        ? OperationResult<SubscriptionState>.Success(_state)
        : OperationResult<SubscriptionState>.Failure(LoadFailure));

    public Task<OperationResult> SaveAsync(SubscriptionState subscription, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success());
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









