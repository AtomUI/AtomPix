namespace AtomPix.StressTests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;
using AtomPix.Infrastructure.Diagnostics;
using AtomPix.Workflows.Images;
using ImageMagick;
using Microsoft.Extensions.Logging;

public sealed class ProductionStressTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomPixStressTests", Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Batch_pipeline_processes_two_thousand_items_with_monotonic_live_progress_and_unique_outputs()
    {
        const int itemCount = 2_000;
        var inputDirectory = new LocalPath(Path.Combine(_root, "inputs"));
        var outputDirectory = Path.Combine(_root, "outputs");
        var inputs = Enumerable.Range(1, itemCount)
            .Select(index => new LocalPath(Path.Combine(inputDirectory.Value, $"image-{index:D4}.jpg")))
            .ToArray();
        var fileSystem = new StressFileSystem(inputs);
        var processor = new StressImageProcessor();
        var services = new ImageWorkflowServices(
            processor,
            fileSystem);
        var workflow = new BatchCompressWorkflow(services);
        var sequences = new List<long>(itemCount * 2 + 1);
        var progress = new InlineProgress<BatchExecutionProgress<BatchCompressItemResult>>(value => sequences.Add(value.Sequence));
        var outputPolicy = new OutputPolicy(
            new OutputLocationPolicy(OutputLocationMode.CustomDirectory, outputDirectory, null),
            new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "stress_{name}_{index}"),
            OverwritePolicy.AutoRename);

        var result = await workflow.ExecuteAsync(
            new BatchCompressRequest(inputs, CompressionProfile.BalancedDefault(), outputPolicy),
            progress,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(itemCount, result.Value!.BatchResult.SucceededCount);
        Assert.Equal(itemCount, processor.CompressCount);
        Assert.Equal(itemCount, result.Value.BatchResult.Items.Select(item => item.OutputPath!.Value.Value).Distinct(fileSystem.PathComparer).Count());
        Assert.True(sequences.Count >= itemCount * 2 + 1);
        Assert.True(sequences.Zip(sequences.Skip(1), (left, right) => right > left).All(value => value));
        Assert.Equal(1d, result.Value.FinalProgress.CompletionRatio);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Structured_logging_rolls_under_sustained_volume_and_never_exceeds_its_total_budget()
    {
        var logDirectory = Path.Combine(_root, "logs");
        const long totalBudget = 2L * 1024 * 1024;
        using (var provider = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(
                   logDirectory,
                   maxFileSizeBytes: 128 * 1024,
                   maxTotalSizeBytes: totalBudget,
                   retentionDays: 7)))
        {
            var logger = provider.CreateLogger("AtomPix.Stress");
            Parallel.For(0, 4_000, index =>
            {
                logger.LogInformation(
                    new EventId(7000, "StressEvent"),
                    "Processed item {Index} with payload {Payload}",
                    index,
                    new string('x', 180));
            });
        }

        var files = Directory.GetFiles(logDirectory, "*.jsonl");
        Assert.NotEmpty(files);
        Assert.True(files.Sum(path => new FileInfo(path).Length) <= totalBudget);
        Assert.All(files, path => Assert.True(new FileInfo(path).Length <= 128 * 1024));
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Magick_creates_parallel_previews_from_a_sixteen_megapixel_image_within_declared_bounds()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "large.png");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 4096, 4096))
        {
            image.Format = MagickFormat.Png;
            image.Write(input);
        }

        var processor = new MagickImageProcessor(MagickImageProcessorOptions.CreateDefault(Path.Combine(_root, "magick-cache")));
        var probe = await processor.ProbeAsync(new ImageProbeRequest(new LocalPath(input)), CancellationToken.None);
        Assert.True(probe.Succeeded, probe.Error?.Message);
        Assert.Equal(4096, probe.Value!.Width);
        Assert.Equal(4096, probe.Value.Height);

        var previews = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            processor.CreatePreviewAsync(new ImagePreviewRequest(new LocalPath(input), 1024), CancellationToken.None)));

        Assert.All(previews, preview =>
        {
            Assert.True(preview.Succeeded, preview.Error?.Message);
            Assert.True(preview.Value!.Width <= 1024);
            Assert.True(preview.Value.Height <= 1024);
            Assert.NotEmpty(preview.Value.EncodedBytes);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StressFileSystem(IReadOnlyList<LocalPath> inputs) : IFileSystemService
    {
        private readonly HashSet<string> _inputs = inputs.Select(path => Path.GetFullPath(path.Value)).ToHashSet(PathComparerForCurrentOs());

        public StringComparer PathComparer { get; } = PathComparerForCurrentOs();

        public bool FileExists(LocalPath path) => _inputs.Contains(Path.GetFullPath(path.Value));
        public bool DirectoryExists(LocalPath path) => true;
        public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken) => Task.FromResult(OperationResult<long>.Success(8192));
        public Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesAsync(LocalPath directory, CancellationToken cancellationToken) => Task.FromResult(OperationResult<IReadOnlyList<LocalPath>>.Success(inputs));
        public OperationResult<LocalPath> NormalizePath(LocalPath path) => OperationResult<LocalPath>.Success(new LocalPath(Path.GetFullPath(path.Value)));
        public bool PathsEqual(LocalPath left, LocalPath right) => PathComparer.Equals(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));
        public int ComparePaths(LocalPath left, LocalPath right) => PathComparer.Compare(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));
        public LocalPath Combine(LocalPath directory, string fileName) => new(Path.Combine(directory.Value, fileName));
        public string GetFileName(LocalPath path) => Path.GetFileName(path.Value);
        public string GetFileNameWithoutExtension(LocalPath path) => Path.GetFileNameWithoutExtension(path.Value);
        public string GetExtension(LocalPath path) => Path.GetExtension(path.Value);
        public LocalPath ChangeExtension(LocalPath path, string extension) => new(Path.ChangeExtension(path.Value, extension));
        public LocalPath BuildIndexedPath(LocalPath basePath, int index) => new(Path.Combine(
            Path.GetDirectoryName(basePath.Value)!,
            $"{Path.GetFileNameWithoutExtension(basePath.Value)}_{index}{Path.GetExtension(basePath.Value)}"));

        private static StringComparer PathComparerForCurrentOs() =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private sealed class StressImageProcessor : IImageProcessor
    {
        private int _compressCount;

        public int CompressCount => Volatile.Read(ref _compressCount);

        public ImageProcessorCapabilities Capabilities { get; } = new(
            new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg },
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false,
            new ImageResourceCapabilities(512L * 1024 * 1024, 32768, 32768, 128_000_000, 32768, 32768, 128_000_000),
            new ImageResizeCapabilities(new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg }, 32768, 32768, 128_000_000),
            new ImageCropCapabilities(new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg }, 32768, 32768, 128_000_000));

        public Task<OperationResult<ImageProbeResult>> ProbeAsync(ImageProbeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<ImageProbeResult>.Success(new ImageProbeResult(
                request.InputPath,
                ImageFormatKind.Jpeg,
                1920,
                1080,
                8192,
                false,
                false,
                false,
                1,
                true,
                true)));

        public Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<ImagePreviewResult>.Success(new ImagePreviewResult([1], "image/jpeg", 1, 1)));

        public Task<OperationResult<ImageCompressResult>> CompressAsync(ImageCompressRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _compressCount);
            return Task.FromResult(OperationResult<ImageCompressResult>.Success(new ImageCompressResult(
                request.InputPath,
                request.OutputPath,
                ImageFormatKind.Jpeg,
                ImageFormatKind.Jpeg,
                8192,
                4096,
                request.Profile.Quality)));
        }

        public Task<OperationResult<ImageConvertResult>> ConvertAsync(ImageConvertRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<ImageConvertResult>.Failure(new AtomPix.Core.Errors.AtomPixError(
                AtomPix.Core.Errors.AtomPixErrorCode.UnsupportedOutputFormat,
                AtomPix.Core.Errors.AtomPixErrorCategory.UnsupportedFormat,
                "Stress double supports compression only.")));

        public Task<OperationResult<ImageResizeResult>> ResizeAsync(ImageResizeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<ImageResizeResult>.Failure(new AtomPix.Core.Errors.AtomPixError(
                AtomPix.Core.Errors.AtomPixErrorCode.UnsupportedInputFormat,
                AtomPix.Core.Errors.AtomPixErrorCategory.UnsupportedFormat,
                "Stress double supports compression only.")));

        public Task<OperationResult<ImageCropResult>> CropAsync(ImageCropRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<ImageCropResult>.Failure(new AtomPix.Core.Errors.AtomPixError(
                AtomPix.Core.Errors.AtomPixErrorCode.UnsupportedInputFormat,
                AtomPix.Core.Errors.AtomPixErrorCategory.UnsupportedFormat,
                "Stress double supports compression only.")));
    }
}
