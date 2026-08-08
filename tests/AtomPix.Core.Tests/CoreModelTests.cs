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

public sealed class CoreModelTests
{
    [Fact]
    public void OperationResult_success_has_no_error()
    {
        var result = OperationResult.Success();

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
    }

    [Fact]
    public void OperationResult_failure_has_error()
    {
        var error = new AtomPixError(AtomPixErrorCode.Unknown, AtomPixErrorCategory.Unexpected, "failed");
        var result = OperationResult.Failure(error);

        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ImageQuality_rejects_out_of_range_values(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageQuality(value));
    }

    [Fact]
    public void Smart_compression_default_removes_metadata_and_has_no_quality()
    {
        var profile = CompressionProfile.SmartDefault();

        Assert.Equal(CompressionMode.Smart, profile.Mode);
        Assert.Null(profile.Quality);
        Assert.Equal(MetadataPolicy.Remove, profile.MetadataPolicy);
    }

    [Fact]
    public void Default_conversion_outputs_webp_at_quality_80()
    {
        var profile = ConversionProfile.WebPDefault();

        Assert.Equal(OutputImageFormat.WebP, profile.OutputFormat);
        Assert.Equal(80, profile.Quality?.Value);
        Assert.Equal(MetadataPolicy.Remove, profile.MetadataPolicy);
        Assert.Equal(RgbColor.White, profile.TransparencyPolicy.OpaqueBackgroundColor);
    }

    [Fact]
    public void Default_output_policy_uses_subfolder_suffix_and_auto_rename()
    {
        var policy = OutputPolicy.Default;

        Assert.Equal(OutputLocationMode.Subfolder, policy.LocationPolicy.Mode);
        Assert.Equal("AtomPix_Output", policy.LocationPolicy.SubfolderName);
        Assert.Equal(OutputNamingMode.AppendSuffix, policy.NamingPolicy.Mode);
        Assert.Equal("_atompix", policy.NamingPolicy.Suffix);
        Assert.Equal(OverwritePolicy.AutoRename, policy.OverwritePolicy);
    }

    [Fact]
    public void AppSettings_default_matches_product_defaults()
    {
        var settings = AppSettings.Default;

        Assert.Equal(ThemeMode.System, settings.ThemeMode);
        Assert.Null(settings.Language);
        Assert.True(settings.RecentItems.Enabled);
        Assert.Equal(20, settings.RecentItems.MaxCount);
        Assert.Equal(OutputImageFormat.WebP, settings.DefaultConversionProfile.OutputFormat);
        Assert.Equal(90, settings.DefaultSameFormatEncodingPolicy.LossyQuality.Value);
        Assert.Equal(MetadataPolicy.Remove, settings.DefaultSameFormatEncodingPolicy.MetadataPolicy);
    }


    [Fact]
    public void RecentItemsPolicy_adds_moves_deduplicates_and_trims()
    {
        var old = DateTimeOffset.UtcNow.AddMinutes(-10);
        var now = DateTimeOffset.UtcNow;
        var existing = new[]
        {
            new RecentItem(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, old),
            new RecentItem(new LocalPath("C:\\img\\b.jpg"), RecentItemKind.File, old.AddMinutes(1))
        };

        var updated = RecentItemsPolicy.AddOrMoveToTop(
            existing,
            new RecentItem(new LocalPath("C:\\img\\a.jpg"), RecentItemKind.File, now),
            maxCount: 2);

        Assert.Equal(2, updated.Count);
        Assert.Equal("C:\\img\\a.jpg", updated[0].Path.Value);
        Assert.Equal(now, updated[0].OpenedAt);
    }

    [Fact]
    public void BatchProgressSnapshot_calculates_ratio_and_rejects_inconsistent_counts()
    {
        var batchId = BatchJobId.New();
        var input = new LocalPath("input.jpg");
        var output = new LocalPath("output.jpg");
        var completed = new[]
        {
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, output, ImageJobStatus.Succeeded, 100, 70, null),
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, null, ImageJobStatus.Failed, 100, null,
                new AtomPixError(AtomPixErrorCode.ImageCompressFailed, AtomPixErrorCategory.ImageProcessing, "failed"))
        };

        var snapshot = BatchProgressSnapshot.FromResults(batchId, ImageJobType.Compress, 4, completed, input);

        Assert.Equal(2, snapshot.CompletedCount);
        Assert.Equal(0.5, snapshot.CompletionRatio);
        Assert.False(snapshot.IsCompleted);
        Assert.Throws<ArgumentException>(() => new BatchProgressSnapshot(batchId, ImageJobType.Compress, 3, 2, 2, 1, 0, 0, null));
    }

    [Fact]
    public void LocalPath_preserves_original_path_text()
    {
        var value = Path.Combine("folder", "archive.photo.final.jpg");

        var path = new LocalPath(value);

        Assert.Equal(value, path.Value);
        Assert.Equal(value, path.ToString());
    }
    [Fact]
    public void LocalPath_rejects_empty_values()
    {
        Assert.Throws<ArgumentException>(() => new LocalPath(" "));
    }


    [Fact]
    public void ImageJobResult_requires_error_for_canceled_status()
    {
        var input = new LocalPath("input.jpg");

        Assert.Throws<ArgumentNullException>(() =>
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, null, ImageJobStatus.Canceled, null, null, null));
    }

    [Fact]
    public void BatchResult_supports_planned_total_count_larger_than_completed_items()
    {
        var input = new LocalPath("input.jpg");
        var output = new LocalPath("output.jpg");
        var canceled = new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "canceled");
        var items = new[]
        {
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, output, ImageJobStatus.Succeeded, 100, 70, null),
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, null, ImageJobStatus.Canceled, null, null, canceled)
        };

        var result = new BatchResult(BatchJobId.New(), ImageJobType.Compress, BatchJobStatus.Canceled, 4, items, canceled);
        var progress = BatchProgressSnapshot.FromResults(result.BatchId, result.Type, result.TotalCount, result.Items, null);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(1, result.CanceledCount);
        Assert.Equal(2, progress.CompletedCount);
        Assert.Equal(4, progress.TotalCount);
        Assert.False(progress.IsCompleted);
        Assert.Equal(0.5, progress.CompletionRatio);
    }
    [Fact]
    public void BatchResult_calculates_counts_and_neutral_size_changes_from_comparable_successes_only()
    {
        var input = new LocalPath("input.jpg");
        var output = new LocalPath("output.jpg");
        var items = new[]
        {
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, output, ImageJobStatus.Succeeded, 100, 70, null),
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, null, ImageJobStatus.Failed, 100, null,
                new AtomPixError(AtomPixErrorCode.ImageCompressFailed, AtomPixErrorCategory.ImageProcessing, "failed")),
            new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, input, output, ImageJobStatus.Skipped, 100, null,
                new AtomPixError(AtomPixErrorCode.OutputFileAlreadyExists, AtomPixErrorCategory.FileSystem, "exists"))
        };

        var result = new BatchResult(BatchJobId.New(), ImageJobType.Compress, BatchJobStatus.PartiallySucceeded, 3, items, null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.SizeComparedItemCount);
        Assert.Equal(100, result.ProcessedInputSizeBytes);
        Assert.Equal(70, result.ProcessedOutputSizeBytes);
        Assert.Equal(-30, result.TotalSizeDeltaBytes);
        Assert.Equal(-0.3, result.TotalSizeDeltaRatio);
        Assert.Equal(FileSizeChangeKind.Reduced, result.TotalSizeChangeKind);
        Assert.Equal(1, result.ReducedItemCount);
        Assert.Equal(0, result.UnchangedItemCount);
        Assert.Equal(0, result.IncreasedItemCount);
    }

    [Theory]
    [InlineData(100, 50, -50, FileSizeChangeKind.Reduced)]
    [InlineData(100, 100, 0, FileSizeChangeKind.Unchanged)]
    [InlineData(100, 125, 25, FileSizeChangeKind.Increased)]
    public void ImageJobResult_uses_neutral_size_delta_semantics(long inputBytes, long outputBytes, long delta, FileSizeChangeKind kind)
    {
        var result = new ImageJobResult(
            ImageJobId.New(),
            ImageJobType.Resize,
            new LocalPath("input.jpg"),
            new LocalPath("output.jpg"),
            ImageJobStatus.Succeeded,
            inputBytes,
            outputBytes,
            null);

        Assert.Equal(delta, result.SizeDeltaBytes);
        Assert.Equal(delta / (double)inputBytes, result.SizeDeltaRatio);
        Assert.Equal(kind, result.SizeChangeKind);
    }

    [Fact]
    public void Resize_policies_resolve_pixel_and_percentage_requests_deterministically()
    {
        var input = new ImageSize(4000, 3000);

        Assert.Equal(new ResolvedResizeSize(1000, 750), new PixelResizePolicy(1000, 2500, true).Resolve(input));
        Assert.Equal(new ResolvedResizeSize(1000, 750), new PixelResizePolicy(1000, null, true).Resolve(input));
        Assert.Equal(new ResolvedResizeSize(1000, 500), new PixelResizePolicy(1000, 500, false).Resolve(input));
        Assert.Equal(new ResolvedResizeSize(500, 375), new PercentageResizePolicy(12.5m).Resolve(input));
    }

    [Fact]
    public void Crop_rules_validate_logical_image_bounds_without_changing_the_rectangle()
    {
        var crop = new CropRectangle(10, 20, 100, 80);

        var valid = CropRules.ValidateCropRectangle(new ImageSize(200, 200), crop);
        var invalid = CropRules.ValidateCropRectangle(new ImageSize(100, 100), crop);

        Assert.True(valid.Succeeded);
        Assert.Same(crop, valid.Value);
        Assert.False(invalid.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidCropOptions, invalid.Error?.Code);
    }
}


