namespace AtomPix.Core.Errors;

public sealed record AtomPixError
{
    public AtomPixError(
        AtomPixErrorCode code,
        AtomPixErrorCategory category,
        string message,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message cannot be empty.", nameof(message));
        }

        Code = code;
        Category = category;
        Message = message;
        Details = details is null ? null : new Dictionary<string, string>(details);
    }

    public AtomPixErrorCode Code { get; }

    public AtomPixErrorCategory Category { get; }

    public string Message { get; }

    public IReadOnlyDictionary<string, string>? Details { get; }
}

public enum AtomPixErrorCategory
{
    Validation,
    FileSystem,
    ImageProcessing,
    UnsupportedFormat,
    Permission,
    Configuration,
    Cancellation,
    Unexpected
}

public enum AtomPixErrorCode
{
    Unknown,
    OperationCanceled,
    InputFileNotFound,
    InputDirectoryNotFound,
    OutputDirectoryNotFound,
    OutputFileAlreadyExists,
    OutputPathConflictsWithInput,
    InvalidInputPath,
    InvalidOutputPath,
    InvalidOutputNamingPattern,
    UnsupportedInputFormat,
    UnsupportedOutputFormat,
    InvalidImageFile,
    InvalidCompressionQuality,
    InvalidResizeOptions,
    InvalidCropOptions,
    InvalidConversionOptions,
    InvalidMetadataOptions,
    ImageReadFailed,
    ImageWriteFailed,
    ImageCompressFailed,
    ImageConvertFailed,
    ImageResizeFailed,
    ImageCropFailed,
    ImagePreviewFailed,
    InputFileTooLarge,
    ImageDimensionsExceedLimit,
    ImageResourceLimitExceeded,
    InsufficientDiskSpace,
    SettingsLoadFailed,
    SettingsSaveFailed,
    RecentItemsSaveFailed
}

