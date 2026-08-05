namespace AtomPix.Imaging.Abstractions.Tests;

using AtomPix.Imaging.Abstractions.Processing;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Imaging_abstractions_do_not_reference_implementations_or_ui_libraries()
    {
        var references = typeof(IImageProcessor).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(references, IsForbiddenReference);
    }

    private static bool IsForbiddenReference(string name) =>
        name.StartsWith("AtomPix.Infrastructure", StringComparison.Ordinal)
        || name.StartsWith("AtomPix.Workflows", StringComparison.Ordinal)
        || name.StartsWith("AtomPix.Imaging.Magick", StringComparison.Ordinal)
        || name.StartsWith("Avalonia", StringComparison.Ordinal)
        || name.StartsWith("AtomUI", StringComparison.Ordinal)
        || name.StartsWith("ImageMagick", StringComparison.Ordinal)
        || name.StartsWith("Magick", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal);
}
