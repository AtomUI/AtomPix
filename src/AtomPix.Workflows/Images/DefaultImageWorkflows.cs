namespace AtomPix.Workflows.Images;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.Crop;
using AtomPix.Core.ValueObjects;

public sealed record CompressWithDefaultSettingsRequest(LocalPath InputPath);
public sealed record CompressWithDefaultSettingsResult(CompressImageResult Result);

public sealed class CompressWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly CompressImageWorkflow _compressWorkflow;

    public CompressWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, CompressImageWorkflow compressWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _compressWorkflow = compressWorkflow ?? throw new ArgumentNullException(nameof(compressWorkflow));
    }

    public async Task<OperationResult<CompressWithDefaultSettingsResult>> ExecuteAsync(CompressWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<CompressWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _compressWorkflow.ExecuteAsync(
            new CompressImageRequest(request.InputPath, settings.Value!.DefaultCompressionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<CompressWithDefaultSettingsResult>.Success(new CompressWithDefaultSettingsResult(result.Value!))
            : OperationResult<CompressWithDefaultSettingsResult>.Failure(result.Error!);
    }
}

public sealed record ConvertWithDefaultSettingsRequest(LocalPath InputPath);
public sealed record ConvertWithDefaultSettingsResult(ConvertImageResult Result);

public sealed class ConvertWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ConvertImageWorkflow _convertWorkflow;

    public ConvertWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, ConvertImageWorkflow convertWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _convertWorkflow = convertWorkflow ?? throw new ArgumentNullException(nameof(convertWorkflow));
    }

    public async Task<OperationResult<ConvertWithDefaultSettingsResult>> ExecuteAsync(ConvertWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<ConvertWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _convertWorkflow.ExecuteAsync(
            new ConvertImageRequest(request.InputPath, settings.Value!.DefaultConversionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<ConvertWithDefaultSettingsResult>.Success(new ConvertWithDefaultSettingsResult(result.Value!))
            : OperationResult<ConvertWithDefaultSettingsResult>.Failure(result.Error!);
    }
}

public sealed record ResizeWithDefaultSettingsRequest(LocalPath InputPath, ResizePolicy ResizePolicy);
public sealed record ResizeWithDefaultSettingsResult(ResizeImageResult Result);

public sealed class ResizeWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ResizeImageWorkflow _resizeWorkflow;

    public ResizeWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, ResizeImageWorkflow resizeWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _resizeWorkflow = resizeWorkflow ?? throw new ArgumentNullException(nameof(resizeWorkflow));
    }

    public async Task<OperationResult<ResizeWithDefaultSettingsResult>> ExecuteAsync(
        ResizeWithDefaultSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ResizePolicy);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<ResizeWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _resizeWorkflow.ExecuteAsync(
            new ResizeImageRequest(
                request.InputPath,
                request.ResizePolicy,
                settings.Value!.DefaultOutputPolicy,
                settings.Value.DefaultSameFormatEncodingPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<ResizeWithDefaultSettingsResult>.Success(new ResizeWithDefaultSettingsResult(result.Value!))
            : OperationResult<ResizeWithDefaultSettingsResult>.Failure(result.Error!);
    }
}

public sealed record CropWithDefaultSettingsRequest(LocalPath InputPath, CropRectangle CropArea);
public sealed record CropWithDefaultSettingsResult(CropImageResult Result);

public sealed class CropWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly CropImageWorkflow _cropWorkflow;

    public CropWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, CropImageWorkflow cropWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _cropWorkflow = cropWorkflow ?? throw new ArgumentNullException(nameof(cropWorkflow));
    }

    public async Task<OperationResult<CropWithDefaultSettingsResult>> ExecuteAsync(
        CropWithDefaultSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CropArea);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<CropWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _cropWorkflow.ExecuteAsync(
            new CropImageRequest(
                request.InputPath,
                request.CropArea,
                settings.Value!.DefaultOutputPolicy,
                settings.Value.DefaultSameFormatEncodingPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<CropWithDefaultSettingsResult>.Success(new CropWithDefaultSettingsResult(result.Value!))
            : OperationResult<CropWithDefaultSettingsResult>.Failure(result.Error!);
    }
}
