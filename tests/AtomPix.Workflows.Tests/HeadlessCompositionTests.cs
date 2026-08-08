namespace AtomPix.Workflows.Tests;

using System.Text.Json;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
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

        var settingsStore = services.GetRequiredService<IAppSettingsStore>();
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
    public async Task Headless_workflow_creates_one_operation_scope_and_redacted_terminal_log()
    {
        using var services = BuildProvider();
        var workflow = services.GetRequiredService<CompressImageWorkflow>();
        var input = new LocalPath(Path.Combine(_root, "sample.jpg"));

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(input, CompressionProfile.BalancedDefault(), AtomPix.Core.Output.OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var logDirectory = Path.Combine(_root, "appdata", "logs");
        var lines = Directory.GetFiles(logDirectory, "*.jsonl")
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("CompressImageWorkflow", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(lines, line => line.Contains("sample.jpg", StringComparison.OrdinalIgnoreCase));
        using var started = JsonDocument.Parse(lines[0]);
        using var completed = JsonDocument.Parse(lines[1]);
        Assert.Equal("WorkflowStarted", started.RootElement.GetProperty("EventName").GetString());
        Assert.Equal("WorkflowCompleted", completed.RootElement.GetProperty("EventName").GetString());
        Assert.Equal(
            started.RootElement.GetProperty("OperationId").GetString(),
            completed.RootElement.GetProperty("OperationId").GetString());
        Assert.Equal(result.Value!.JobResult.JobId.Value, completed.RootElement.GetProperty("JobId").GetGuid());
    }

    [Fact]
    public async Task Image_engine_failure_inherits_workflow_operation_scope_without_leaking_file_name()
    {
        var inputPath = Path.Combine(_root, "private-corrupt.jpg");
        await File.WriteAllBytesAsync(inputPath, [0xFF, 0xD8, 0x00, 0x01]);
        using var services = BuildProvider();
        var workflow = services.GetRequiredService<CompressImageWorkflow>();

        var result = await workflow.ExecuteAsync(
            new CompressImageRequest(new LocalPath(inputPath), CompressionProfile.BalancedDefault(), AtomPix.Core.Output.OutputPolicy.Default),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
        var lines = Directory.GetFiles(Path.Combine(_root, "appdata", "logs"), "*.jsonl")
            .SelectMany(File.ReadLines)
            .ToArray();
        Assert.DoesNotContain(lines, line => line.Contains("private-corrupt.jpg", StringComparison.OrdinalIgnoreCase));
        var events = lines.Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Equal(3, events.Length);
            Assert.Contains(events, item => item.RootElement.GetProperty("EventName").GetString() == "ImageEngineFailure");
            var operationIds = events
                .Select(item => item.RootElement.GetProperty("OperationId").GetString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.Single(operationIds);
        }
        finally
        {
            foreach (var item in events) item.Dispose();
        }
    }

    [Fact]
    public async Task Dependency_injection_composes_default_compress_and_batch_flows()
    {
        using var services = BuildProvider();
        var settingsStore = services.GetRequiredService<IAppSettingsStore>();
        await settingsStore.SaveAsync(new AppSettings(
            new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), MetadataPolicy.Remove),
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultSameFormatEncodingPolicy,
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
            Assert.Equal(120u, output.Width);
            Assert.Equal(80u, output.Height);
        }

        var resize = services.GetRequiredService<ResizeWithDefaultSettingsWorkflow>();
        var resizeResult = await resize.ExecuteAsync(
            new ResizeWithDefaultSettingsRequest(
                new LocalPath(Path.Combine(_root, "sample.jpg")),
                new PixelResizePolicy(80, 80, maintainAspectRatio: true)),
            CancellationToken.None);

        Assert.True(resizeResult.Succeeded);
        Assert.Equal(new ImageSize(80, 53), resizeResult.Value!.Result.ActualOutputSize);

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
    public async Task Dependency_injection_exposes_batch_features_without_subscription_services()
    {
        using var services = BuildProvider();
        var batchConvert = services.GetRequiredService<BatchConvertWorkflow>();

        var result = await batchConvert.ExecuteAsync(
            new BatchConvertRequest([new LocalPath(Path.Combine(_root, "sample.png"))], ConversionProfile.WebPDefault(), AtomPix.Core.Output.OutputPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BatchJobStatus.Succeeded, result.Value!.BatchResult.Status);
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

