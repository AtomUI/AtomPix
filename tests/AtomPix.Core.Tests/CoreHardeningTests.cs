namespace AtomPix.Core.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;

public sealed class CoreHardeningTests
{
    [Fact]
    public void OperationResult_success_rejects_null_value_for_generic_result()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult<string>.Success(null!));
    }

    [Fact]
    public void OperationResult_failure_rejects_null_error()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => OperationResult<string>.Failure(null!));
    }

    [Fact]
    public void AtomPixError_rejects_empty_message()
    {
        Assert.Throws<ArgumentException>(() => new AtomPixError(AtomPixErrorCode.Unknown, AtomPixErrorCategory.Unexpected, " "));
    }

    [Fact]
    public void Custom_compression_requires_quality()
    {
        Assert.Throws<ArgumentException>(() => new CompressionProfile(CompressionMode.Custom, null, MetadataPolicy.Remove));
        Assert.Throws<ArgumentException>(() => new CompressionProfile(CompressionMode.Smart, new ImageQuality(80), MetadataPolicy.Remove));
    }

    [Fact]
    public void ResizePolicy_rejects_inconsistent_shapes()
    {
        Assert.Throws<ArgumentException>(() => new PixelResizePolicy(null, null, true));
        Assert.Throws<ArgumentException>(() => new PixelResizePolicy(100, null, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelResizePolicy(0, 100, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PercentageResizePolicy(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSize(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResolvedResizeSize(100, 0));
    }

    [Fact]
    public void OutputLocationPolicy_rejects_invalid_payload_for_mode()
    {
        Assert.Throws<ArgumentException>(() => new OutputLocationPolicy(OutputLocationMode.SameAsInput, "C:\\out", null));
        Assert.Throws<ArgumentException>(() => new OutputLocationPolicy(OutputLocationMode.Subfolder, null, null));
        Assert.Throws<ArgumentException>(() => new OutputLocationPolicy(OutputLocationMode.CustomDirectory, null, null));
    }

    [Fact]
    public void OutputNamingPolicy_rejects_invalid_payload_for_mode()
    {
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, "_x"));
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.AppendSuffix, null));
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.CustomPattern, "_x", "{name}"));
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{Name}"));
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{index}_{index}"));
        Assert.Throws<ArgumentException>(() => new OutputNamingPolicy(OutputNamingMode.AppendSuffix, "{index}"));

        var custom = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "holiday_{index}_{name}");
        Assert.Equal("holiday_{index}_{name}", custom.GetBasePattern());
    }

    [Fact]
    public void RecentItemsSettings_rejects_non_positive_max_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecentItemsSettings(true, 0));
    }

    [Fact]
    public void Job_ids_reject_empty_guid()
    {
        Assert.Throws<ArgumentException>(() => new ImageJobId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new BatchJobId(Guid.Empty));
    }

    [Fact]
    public void ImageJob_enforces_state_transitions()
    {
        var job = new ImageJob(ImageJobId.New(), ImageJobType.Compress, new LocalPath("a.jpg"), DateTimeOffset.UtcNow);
        job.MarkRunning(job.CreatedAt.AddSeconds(1));
        job.MarkSucceeded(new LocalPath("out.jpg"), job.CreatedAt.AddSeconds(2));

        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.Throws<InvalidOperationException>(() => job.MarkRunning(job.CreatedAt.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => job.MarkFailed(TestError(), job.CreatedAt.AddSeconds(3)));
    }

    [Fact]
    public void ImageJob_rejects_completion_before_start()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new ImageJob(ImageJobId.New(), ImageJobType.Compress, new LocalPath("a.jpg"), now);
        job.MarkRunning(now.AddSeconds(10));

        Assert.Throws<ArgumentOutOfRangeException>(() => job.MarkSucceeded(new LocalPath("out.jpg"), now.AddSeconds(1)));
    }

    [Fact]
    public void BatchJob_derives_terminal_status_from_child_jobs_and_transition_intent()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new BatchJob(BatchJobId.New(), ImageJobType.Compress, Array.Empty<ImageJob>(), now));

        var success = new ImageJob(ImageJobId.New(), ImageJobType.Compress, new LocalPath("a.jpg"), now);
        var failure = new ImageJob(ImageJobId.New(), ImageJobType.Compress, new LocalPath("b.jpg"), now);
        var batch = new BatchJob(BatchJobId.New(), ImageJobType.Compress, [success, failure], now);
        batch.MarkRunning(now);
        success.MarkRunning(now);
        success.MarkSucceeded(new LocalPath("a-out.jpg"), now);
        failure.MarkFailed(TestError(), now);
        batch.CompleteNaturally(now);

        Assert.Equal(BatchJobStatus.PartiallySucceeded, batch.Status);
        Assert.Null(batch.Error);

        var pending = new ImageJob(ImageJobId.New(), ImageJobType.Resize, new LocalPath("c.jpg"), now);
        var canceledBatch = new BatchJob(BatchJobId.New(), ImageJobType.Resize, [pending], now);
        var canceled = new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled");
        canceledBatch.MarkRunning(now);
        canceledBatch.Cancel(canceled, now);
        Assert.Equal(BatchJobStatus.Canceled, canceledBatch.Status);
        Assert.Equal(canceled, canceledBatch.Error);
    }

    [Fact]
    public void ImageJobResult_enforces_terminal_status_shape()
    {
        var id = ImageJobId.New();
        var input = new LocalPath("a.jpg");
        var output = new LocalPath("out.jpg");

        Assert.Throws<ArgumentException>(() => new ImageJobResult(id, ImageJobType.Compress, input, null, ImageJobStatus.Running, null, null, null));
        Assert.Throws<ArgumentNullException>(() => new ImageJobResult(id, ImageJobType.Compress, input, null, ImageJobStatus.Succeeded, 10, 1, null));
        Assert.Throws<ArgumentNullException>(() => new ImageJobResult(id, ImageJobType.Compress, input, output, ImageJobStatus.Succeeded, 10, null, null));
        Assert.Throws<ArgumentNullException>(() => new ImageJobResult(id, ImageJobType.Compress, input, null, ImageJobStatus.Failed, 10, null, null));
    }

    [Fact]
    public void BatchResult_requires_terminal_status_but_allows_no_completed_items_for_an_accepted_batch()
    {
        Assert.Throws<ArgumentException>(() => new BatchResult(BatchJobId.New(), ImageJobType.Compress, BatchJobStatus.Running, 1, Array.Empty<ImageJobResult>(), null));

        var result = new BatchResult(
            BatchJobId.New(),
            ImageJobType.Compress,
            BatchJobStatus.Failed,
            1,
            Array.Empty<ImageJobResult>(),
            new AtomPixError(AtomPixErrorCode.Unknown, AtomPixErrorCategory.Unexpected, "aborted"));

        Assert.Empty(result.Items);
        Assert.Null(result.TotalSizeDeltaBytes);
        Assert.Null(result.TotalSizeChangeKind);
    }


    [Fact]
    public void Strategy_models_reject_unknown_enum_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompressionProfile((CompressionMode)999, null, MetadataPolicy.Remove));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), (MetadataPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionProfile((OutputImageFormat)999, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionProfile(OutputImageFormat.WebP, new ImageQuality(80), (MetadataPolicy)999, TransparencyPolicy.Default));
        Assert.Throws<ArgumentNullException>(() => new ConversionProfile(OutputImageFormat.WebP, new ImageQuality(80), MetadataPolicy.Remove, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputPolicy(OutputPolicy.Default.LocationPolicy, OutputPolicy.Default.NamingPolicy, (OverwritePolicy)999));
    }

    [Fact]
    public void AtomPixError_copies_details_dictionary()
    {
        var details = new Dictionary<string, string> { ["path"] = "a.jpg" };
        var error = new AtomPixError(AtomPixErrorCode.ImageReadFailed, AtomPixErrorCategory.ImageProcessing, "failed", details);

        details["path"] = "b.jpg";

        Assert.Equal("a.jpg", error.Details!["path"]);
    }
    private static AtomPixError TestError() => new(AtomPixErrorCode.Unknown, AtomPixErrorCategory.Unexpected, "failed");

    [Fact]
    public void AppSettings_enforces_schema_version()
    {
        Assert.Equal(AppSettings.CurrentSchemaVersion, AppSettings.Default.SchemaVersion);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings(
            CompressionProfile.SmartDefault(),
            ConversionProfile.WebPDefault(),
            SameFormatEncodingPolicy.Default,
            OutputPolicy.Default,
            ThemeMode.System,
            null,
            new RecentItemsSettings(true, 20),
            schemaVersion: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings(
            CompressionProfile.SmartDefault(),
            ConversionProfile.WebPDefault(),
            SameFormatEncodingPolicy.Default,
            OutputPolicy.Default,
            ThemeMode.System,
            null,
            new RecentItemsSettings(true, 20),
            schemaVersion: AppSettings.CurrentSchemaVersion + 1));
    }

    [Fact]
    public void AppSettings_rejects_inconsistent_shared_metadata_defaults()
    {
        Assert.Throws<ArgumentException>(() => new AppSettings(
            CompressionProfile.SmartDefault(),
            new ConversionProfile(OutputImageFormat.WebP, new ImageQuality(80), MetadataPolicy.Preserve, TransparencyPolicy.Default),
            SameFormatEncodingPolicy.Default,
            OutputPolicy.Default,
            ThemeMode.System,
            null,
            new RecentItemsSettings(true, 20)));
    }

    [Fact]
    public void Crop_and_color_value_objects_reject_invalid_shapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropRectangle(-1, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropRectangle(0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropAspectRatio(0, 1));

        Assert.Equal(new CropAspectRatio(3, 2), new CropAspectRatio(6, 4));
        Assert.Equal("#0A80FF", new RgbColor(10, 128, 255).ToHexString());
        Assert.Equal(new RgbColor(10, 128, 255), RgbColor.Parse("#0a80ff"));
        Assert.False(RgbColor.TryParse("#FF000080", out _));
    }
}
