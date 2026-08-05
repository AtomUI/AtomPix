namespace AtomPix.Workflows.Tests;

using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Licensing;
using AtomPix.Core.Ports;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Magick.DependencyInjection;
using AtomPix.Infrastructure.DependencyInjection;
using AtomPix.Workflows.DependencyInjection;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;

public sealed class HeadlessCompositionTests : IDisposable
{
    private readonly string _root;

    public HeadlessCompositionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AtomPixCompositionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        using (var image = new MagickImage(MagickColors.Transparent, 96, 64))
        {
            image.Format = MagickFormat.Png;
            image.GetPixels().SetPixel(10, 10, [255, 0, 0, 255]);
            image.Write(Path.Combine(_root, "sample.png"));
        }

        using (var image = new MagickImage(MagickColors.Red, 120, 80))
        {
            image.Format = MagickFormat.Jpeg;
            image.Write(Path.Combine(_root, "sample.jpg"));
        }
    }

    [Fact]
    public async Task Dependency_injection_composes_real_headless_conversion_flow()
    {
        using var services = BuildProvider();

        var subscriptionStore = services.GetRequiredService<ISubscriptionStore>();
        var settingsStore = services.GetRequiredService<IAppSettingsStore>();
        await subscriptionStore.SaveAsync(new SubscriptionState(SubscriptionStatus.Active, BillingCycle.Monthly, DateTimeOffset.UtcNow.AddMonths(1)), CancellationToken.None);
        await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);

        var convert = services.GetRequiredService<ConvertWithDefaultSettingsWorkflow>();
        var result = await convert.ExecuteAsync(new ConvertWithDefaultSettingsRequest(new LocalPath(Path.Combine(_root, "sample.png"))), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(result.Value!.Result.JobResult.OutputPath!.Value.Value));
        Assert.EndsWith("sample_atompix.webp", result.Value.Result.JobResult.OutputPath.Value.Value);

        var recent = services.GetRequiredService<AddRecentItemWorkflow>();
        var recentResult = await recent.ExecuteAsync(
            new AddRecentItemRequest(new LocalPath(Path.Combine(_root, "sample.png")), RecentItemKind.File, DateTimeOffset.UtcNow, 20),
            CancellationToken.None);

        Assert.True(recentResult.Succeeded);
        Assert.Single(recentResult.Value!.Items);
    }

    [Fact]
    public async Task Dependency_injection_composes_default_compress_and_batch_flows()
    {
        using var services = BuildProvider();
        var subscriptionStore = services.GetRequiredService<ISubscriptionStore>();
        var settingsStore = services.GetRequiredService<IAppSettingsStore>();
        await subscriptionStore.SaveAsync(new SubscriptionState(SubscriptionStatus.Active, BillingCycle.Monthly, DateTimeOffset.UtcNow.AddMonths(1)), CancellationToken.None);
        await settingsStore.SaveAsync(new AppSettings(
            new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), AtomPix.Core.Compression.ResizePolicy.FitWithinBounds(80, 80), MetadataPolicy.Remove),
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultOutputPolicy,
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems), CancellationToken.None);

        var compress = services.GetRequiredService<CompressWithDefaultSettingsWorkflow>();
        var compressResult = await compress.ExecuteAsync(new CompressWithDefaultSettingsRequest(new LocalPath(Path.Combine(_root, "sample.jpg"))), CancellationToken.None);

        Assert.True(compressResult.Succeeded);
        Assert.Equal(ImageJobStatus.Succeeded, compressResult.Value!.Result.JobResult.Status);
        Assert.True(File.Exists(compressResult.Value.Result.JobResult.OutputPath!.Value.Value));
        using (var output = new MagickImage(compressResult.Value.Result.JobResult.OutputPath.Value.Value))
        {
            Assert.Equal(80u, output.Width);
            Assert.Equal(53u, output.Height);
        }

        var batchCompress = services.GetRequiredService<BatchCompressWithDefaultSettingsWorkflow>();
        var batchCompressResult = await batchCompress.ExecuteAsync(
            new BatchCompressWithDefaultSettingsRequest([
                new LocalPath(Path.Combine(_root, "sample.jpg")),
                new LocalPath(Path.Combine(_root, "sample.png"))
            ]),
            CancellationToken.None);

        Assert.True(batchCompressResult.Succeeded);
        Assert.Equal(2, batchCompressResult.Value!.Result.BatchResult.TotalCount);
        Assert.Equal(2, batchCompressResult.Value.Result.BatchResult.SucceededCount);

        var batchConvert = services.GetRequiredService<BatchConvertWithDefaultSettingsWorkflow>();
        var batchConvertResult = await batchConvert.ExecuteAsync(
            new BatchConvertWithDefaultSettingsRequest([
                new LocalPath(Path.Combine(_root, "sample.png")),
                new LocalPath(Path.Combine(_root, "sample.jpg"))
            ]),
            CancellationToken.None);

        Assert.True(batchConvertResult.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, batchConvertResult.Value!.Result.BatchResult.Status);
        Assert.Equal(2, batchConvertResult.Value.Result.BatchResult.SucceededCount);
    }

    [Fact]
    public async Task Dependency_injection_enforces_feature_access_for_batch_workflows()
    {
        using var services = BuildProvider();
        var batchConvert = services.GetRequiredService<BatchConvertWorkflow>();

        var freeResult = await batchConvert.ExecuteAsync(
            new BatchConvertRequest([new LocalPath(Path.Combine(_root, "sample.png"))], ConversionProfile.WebPDefault(), AtomPix.Core.Output.OutputPolicy.Default),
            CancellationToken.None);

        Assert.False(freeResult.Succeeded);
        Assert.Equal(AtomPixErrorCode.FeatureNotAvailable, freeResult.Error!.Code);

        var subscriptionStore = services.GetRequiredService<ISubscriptionStore>();
        await subscriptionStore.SaveAsync(new SubscriptionState(SubscriptionStatus.Active, BillingCycle.Monthly, DateTimeOffset.UtcNow.AddMonths(1)), CancellationToken.None);
        var activeResult = await batchConvert.ExecuteAsync(
            new BatchConvertRequest([new LocalPath(Path.Combine(_root, "sample.png"))], ConversionProfile.WebPDefault(), AtomPix.Core.Output.OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(activeResult.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, activeResult.Value!.BatchResult.Status);
    }

    private ServiceProvider BuildProvider() => new ServiceCollection()
        .AddAtomPixInfrastructure(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"))
        .AddAtomPixMagickImaging()
        .AddAtomPixWorkflows()
        .BuildServiceProvider(validateScopes: true);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

