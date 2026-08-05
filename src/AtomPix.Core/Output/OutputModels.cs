namespace AtomPix.Core.Output;

public sealed record OutputPolicy
{
    public OutputPolicy(OutputLocationPolicy locationPolicy, OutputNamingPolicy namingPolicy, OverwritePolicy overwritePolicy)
    {
        if (!Enum.IsDefined(overwritePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(overwritePolicy), overwritePolicy, "Unsupported overwrite policy.");
        }

        LocationPolicy = locationPolicy ?? throw new ArgumentNullException(nameof(locationPolicy));
        NamingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
        OverwritePolicy = overwritePolicy;
    }

    public OutputLocationPolicy LocationPolicy { get; }

    public OutputNamingPolicy NamingPolicy { get; }

    public OverwritePolicy OverwritePolicy { get; }

    public static OutputPolicy Default { get; } = new(
        new OutputLocationPolicy(OutputLocationMode.Subfolder, null, "AtomPix_Output"),
        new OutputNamingPolicy(OutputNamingMode.AppendSuffix, "_atompix"),
        OverwritePolicy.AutoRename);
}

public sealed record OutputLocationPolicy
{
    public OutputLocationPolicy(OutputLocationMode mode, string? customDirectory, string? subfolderName)
    {
        switch (mode)
        {
            case OutputLocationMode.SameAsInput:
                if (customDirectory is not null || subfolderName is not null)
                {
                    throw new ArgumentException("SameAsInput cannot carry custom directory or subfolder name.");
                }
                break;
            case OutputLocationMode.Subfolder:
                if (string.IsNullOrWhiteSpace(subfolderName))
                {
                    throw new ArgumentException("Subfolder output requires a subfolder name.", nameof(subfolderName));
                }
                if (customDirectory is not null)
                {
                    throw new ArgumentException("Subfolder output cannot carry custom directory.", nameof(customDirectory));
                }
                break;
            case OutputLocationMode.CustomDirectory:
                if (string.IsNullOrWhiteSpace(customDirectory))
                {
                    throw new ArgumentException("CustomDirectory output requires a custom directory.", nameof(customDirectory));
                }
                if (subfolderName is not null)
                {
                    throw new ArgumentException("CustomDirectory output cannot carry subfolder name.", nameof(subfolderName));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported output location mode.");
        }

        Mode = mode;
        CustomDirectory = customDirectory;
        SubfolderName = subfolderName;
    }

    public OutputLocationMode Mode { get; }

    public string? CustomDirectory { get; }

    public string? SubfolderName { get; }
}

public enum OutputLocationMode
{
    SameAsInput,
    Subfolder,
    CustomDirectory
}

public sealed record OutputNamingPolicy
{
    public OutputNamingPolicy(OutputNamingMode mode, string? suffix)
    {
        switch (mode)
        {
            case OutputNamingMode.KeepOriginalName:
                if (suffix is not null)
                {
                    throw new ArgumentException("KeepOriginalName cannot carry a suffix.", nameof(suffix));
                }
                break;
            case OutputNamingMode.AppendSuffix:
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    throw new ArgumentException("AppendSuffix requires a suffix.", nameof(suffix));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported output naming mode.");
        }

        Mode = mode;
        Suffix = suffix;
    }

    public OutputNamingMode Mode { get; }

    public string? Suffix { get; }
}

public enum OutputNamingMode
{
    KeepOriginalName,
    AppendSuffix
}

public enum OverwritePolicy
{
    Skip,
    Overwrite,
    AutoRename
}
