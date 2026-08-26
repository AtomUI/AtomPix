namespace AtomPix.Core.Tests;

using System.Reflection;
using AtomPix.Core.Compression;

public sealed class CorePublicApiContractTests
{
    [Fact]
    public void Core_public_type_surface_is_explicitly_owned()
    {
        var publicTypes = typeof(CompressionProfile).Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "AtomPix.Core.Compression.CompressionMode",
            "AtomPix.Core.Compression.CompressionProfile",
            "AtomPix.Core.Compression.ImageQuality",
            "AtomPix.Core.Compression.MetadataPolicy",
            "AtomPix.Core.Conversion.ConversionProfile",
            "AtomPix.Core.Conversion.OutputImageFormat",
            "AtomPix.Core.Conversion.RgbColor",
            "AtomPix.Core.Conversion.TransparencyPolicy",
            "AtomPix.Core.Crop.CropAspectRatio",
            "AtomPix.Core.Crop.CropRectangle",
            "AtomPix.Core.Crop.CropRules",
            "AtomPix.Core.Errors.AtomPixError",
            "AtomPix.Core.Errors.AtomPixErrorCategory",
            "AtomPix.Core.Errors.AtomPixErrorCode",
            "AtomPix.Core.Jobs.BatchJob",
            "AtomPix.Core.Jobs.BatchJobId",
            "AtomPix.Core.Jobs.BatchJobStatus",
            "AtomPix.Core.Jobs.BatchProgressSnapshot",
            "AtomPix.Core.Jobs.BatchResult",
            "AtomPix.Core.Jobs.FileSizeChangeKind",
            "AtomPix.Core.Jobs.ImageJob",
            "AtomPix.Core.Jobs.ImageJobId",
            "AtomPix.Core.Jobs.ImageJobResult",
            "AtomPix.Core.Jobs.ImageJobStatus",
            "AtomPix.Core.Jobs.ImageJobType",
            "AtomPix.Core.Output.OutputLocationMode",
            "AtomPix.Core.Output.OutputLocationPolicy",
            "AtomPix.Core.Output.OutputNamingMode",
            "AtomPix.Core.Output.OutputNamingPolicy",
            "AtomPix.Core.Output.OutputPolicy",
            "AtomPix.Core.Output.OutputWriteDisposition",
            "AtomPix.Core.Output.OverwritePolicy",
            "AtomPix.Core.Ports.IAppPathProvider",
            "AtomPix.Core.Ports.IAppSettingsStore",
            "AtomPix.Core.Ports.IFileSystemService",
            "AtomPix.Core.Ports.IRecentItemsStore",
            "AtomPix.Core.Results.OperationResult",
            "AtomPix.Core.Results.OperationResult`1",
            "AtomPix.Core.Resize.ImageSize",
            "AtomPix.Core.Resize.PercentageResizePolicy",
            "AtomPix.Core.Resize.PixelResizePolicy",
            "AtomPix.Core.Resize.ResolvedResizeSize",
            "AtomPix.Core.Resize.ResizePolicy",
            "AtomPix.Core.Resize.SameFormatEncodingPolicy",
            "AtomPix.Core.Settings.AppSettings",
            "AtomPix.Core.Settings.RecentItem",
            "AtomPix.Core.Settings.RecentItemKind",
            "AtomPix.Core.Settings.RecentItemsPolicy",
            "AtomPix.Core.Settings.RecentItemsSettings",
            "AtomPix.Core.Settings.ThemeMode",
            "AtomPix.Core.ValueObjects.LocalPath"
        }.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, publicTypes);
    }

    [Fact]
    public void Core_public_members_do_not_expose_outer_module_types()
    {
        var coreAssembly = typeof(CompressionProfile).Assembly;
        var publicTypes = coreAssembly.GetExportedTypes();

        foreach (var type in publicTypes)
        {
            foreach (var memberType in PublicApiTypes(type))
            {
                Assert.False(IsOuterType(memberType), $"{type.FullName} exposes {memberType.FullName}.");
            }
        }
    }

    private static IEnumerable<Type> PublicApiTypes(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

        foreach (var property in type.GetProperties(Flags))
        {
            yield return Unwrap(property.PropertyType);
        }

        foreach (var method in type.GetMethods(Flags).Where(method => !method.IsSpecialName))
        {
            yield return Unwrap(method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            if (genericDefinition == typeof(Nullable<>))
            {
                return Nullable.GetUnderlyingType(type)!;
            }
        }

        if (type.HasElementType)
        {
            return type.GetElementType()!;
        }

        return type;
    }

    private static bool IsOuterType(Type type)
    {
        var fullName = type.FullName ?? string.Empty;
        return fullName.StartsWith("AtomPix.Infrastructure", StringComparison.Ordinal)
            || fullName.StartsWith("AtomPix.Workflows", StringComparison.Ordinal)
            || fullName.StartsWith("AtomPix.Imaging", StringComparison.Ordinal)
            || fullName.StartsWith("Avalonia", StringComparison.Ordinal)
            || fullName.StartsWith("AtomUI", StringComparison.Ordinal)
            || fullName.StartsWith("ImageMagick", StringComparison.Ordinal)
            || fullName.StartsWith("Magick", StringComparison.Ordinal)
            || fullName.StartsWith("SkiaSharp", StringComparison.Ordinal);
    }
}
