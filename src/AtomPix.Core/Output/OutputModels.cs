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
    public OutputNamingPolicy(OutputNamingMode mode, string? suffix, string? pattern = null)
    {
        switch (mode)
        {
            case OutputNamingMode.KeepOriginalName:
                if (suffix is not null || pattern is not null)
                {
                    throw new ArgumentException("KeepOriginalName cannot carry a suffix or pattern.");
                }
                break;
            case OutputNamingMode.AppendSuffix:
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    throw new ArgumentException("AppendSuffix requires a suffix.", nameof(suffix));
                }
                if (pattern is not null)
                {
                    throw new ArgumentException("AppendSuffix cannot carry a pattern.", nameof(pattern));
                }
                ValidateFileNameText(suffix, nameof(suffix), allowPlaceholders: false);
                break;
            case OutputNamingMode.CustomPattern:
                if (suffix is not null)
                {
                    throw new ArgumentException("CustomPattern cannot carry a suffix.", nameof(suffix));
                }
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    throw new ArgumentException("CustomPattern requires a pattern.", nameof(pattern));
                }
                ValidatePattern(pattern);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported output naming mode.");
        }

        Mode = mode;
        Suffix = suffix;
        Pattern = pattern;
    }

    public OutputNamingMode Mode { get; }

    public string? Suffix { get; }

    public string? Pattern { get; }

    public string GetBasePattern() => Mode switch
    {
        OutputNamingMode.KeepOriginalName => "{name}",
        OutputNamingMode.AppendSuffix => "{name}" + Suffix,
        OutputNamingMode.CustomPattern => Pattern!,
        _ => throw new InvalidOperationException("Unsupported output naming mode.")
    };

    private static void ValidatePattern(string pattern)
    {
        ValidateFileNameText(pattern, nameof(pattern), allowPlaceholders: true);

        var indexCount = 0;
        for (var index = 0; index < pattern.Length;)
        {
            var opening = pattern.IndexOf('{', index);
            var closingWithoutOpening = pattern.IndexOf('}', index);
            if (closingWithoutOpening >= 0 && (opening < 0 || closingWithoutOpening < opening))
            {
                throw new ArgumentException("Output naming pattern contains an unmatched closing brace.", nameof(pattern));
            }
            if (opening < 0) break;

            var closing = pattern.IndexOf('}', opening + 1);
            if (closing < 0)
            {
                throw new ArgumentException("Output naming pattern contains an unclosed placeholder.", nameof(pattern));
            }

            var placeholder = pattern[(opening + 1)..closing];
            if (placeholder is not ("name" or "index"))
            {
                throw new ArgumentException("Output naming pattern contains an unsupported placeholder.", nameof(pattern));
            }
            if (placeholder == "index" && ++indexCount > 1)
            {
                throw new ArgumentException("Output naming pattern can contain {index} at most once.", nameof(pattern));
            }

            index = closing + 1;
        }
    }

    private static void ValidateFileNameText(string value, string parameterName, bool allowPlaceholders)
    {
        if (value.Contains('/') || value.Contains('\\') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Output naming text cannot contain directory separators or invalid file name characters.", parameterName);
        }

        if (!allowPlaceholders && (value.Contains('{') || value.Contains('}')))
        {
            throw new ArgumentException("Output suffix cannot contain placeholders.", parameterName);
        }
    }
}

public enum OutputNamingMode
{
    KeepOriginalName,
    AppendSuffix,
    CustomPattern
}

public enum OverwritePolicy
{
    Skip,
    Overwrite,
    AutoRename
}

/// <summary>
/// Describes what the output planner actually decided for a single image.
/// This is an execution result, not merely the overwrite policy requested by the user.
/// </summary>
public enum OutputWriteDisposition
{
    Created,
    AutoRenamed,
    Overwritten,
    SkippedExisting
}
