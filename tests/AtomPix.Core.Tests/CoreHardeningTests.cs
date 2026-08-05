namespace AtomPix.Core.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Licensing;
using AtomPix.Core.Output;
using AtomPix.Core.Results;
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
        Assert.Throws<ArgumentException>(() => new CompressionProfile(CompressionMode.Custom, null, ResizePolicy.None, MetadataPolicy.Remove));
    }

    [Fact]
    public void ResizePolicy_rejects_inconsistent_shapes()
    {
        Assert.Throws<ArgumentException>(() => new ResizePolicy(ResizeMode.None, 100, null, null));
        Assert.Throws<ArgumentException>(() => new ResizePolicy(ResizeMode.FitWithinBounds, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizePolicy(ResizeMode.Percentage, null, null, 0));
        Assert.Throws<ArgumentException>(() => new ResizePolicy(ResizeMode.Percentage, 100, null, 50));
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
    }

    [Fact]
    public void SubscriptionState_enforces_status_shape()
    {
        Assert.Throws<ArgumentException>(() => new SubscriptionState(SubscriptionStatus.Free, BillingCycle.Monthly, null));
        Assert.Throws<ArgumentException>(() => new SubscriptionState(SubscriptionStatus.Active, null, DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Throws<ArgumentException>(() => new SubscriptionState(SubscriptionStatus.Expired, null, null));
    }

    [Fact]
    public void FeatureAccessDecision_factories_enforce_invariants()
    {
        var allowed = FeatureAccessDecision.Allow();
        var denied = FeatureAccessDecision.Deny(FeatureAccessBlockReason.SubscriptionRequired);

        Assert.True(allowed.Allowed);
        Assert.Null(allowed.BlockReason);
        Assert.False(denied.Allowed);
        Assert.Equal(FeatureAccessBlockReason.SubscriptionRequired, denied.BlockReason);
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
    public void BatchJob_requires_items_and_terminal_completion_status()
    {
        Assert.Throws<ArgumentException>(() => new BatchJob(BatchJobId.New(), ImageJobType.Compress, Array.Empty<ImageJob>(), DateTimeOffset.UtcNow));

        var job = new ImageJob(ImageJobId.New(), ImageJobType.Compress, new LocalPath("a.jpg"), DateTimeOffset.UtcNow);
        var batch = new BatchJob(BatchJobId.New(), ImageJobType.Compress, [job], DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => batch.Complete(BatchJobStatus.Running, DateTimeOffset.UtcNow.AddSeconds(1)));
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
    public void BatchResult_requires_terminal_status_and_items()
    {
        Assert.Throws<ArgumentException>(() => new BatchResult(BatchJobId.New(), ImageJobType.Compress, BatchJobStatus.Running, Array.Empty<ImageJobResult>()));
        Assert.Throws<ArgumentException>(() => new BatchResult(BatchJobId.New(), ImageJobType.Compress, BatchJobStatus.Succeeded, Array.Empty<ImageJobResult>()));
    }


    [Fact]
    public void Strategy_models_reject_unknown_enum_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompressionProfile((CompressionMode)999, null, ResizePolicy.None, MetadataPolicy.Remove));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), ResizePolicy.None, (MetadataPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionProfile((OutputImageFormat)999, new ImageQuality(80), ResizePolicy.None, MetadataPolicy.Remove));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionProfile(OutputImageFormat.WebP, new ImageQuality(80), ResizePolicy.None, (MetadataPolicy)999));
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
            OutputPolicy.Default,
            ThemeMode.System,
            null,
            new RecentItemsSettings(true, 20),
            schemaVersion: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings(
            CompressionProfile.SmartDefault(),
            ConversionProfile.WebPDefault(),
            OutputPolicy.Default,
            ThemeMode.System,
            null,
            new RecentItemsSettings(true, 20),
            schemaVersion: AppSettings.CurrentSchemaVersion + 1));
    }
}
