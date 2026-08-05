namespace AtomPix.Core.Tests;

using AtomPix.Core.Results;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Core_does_not_reference_outer_modules_or_ui_libraries()
    {
        var references = typeof(OperationResult).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(references, IsForbiddenReference);
    }

    private static bool IsForbiddenReference(string name) =>
        name.StartsWith("AtomPix.Infrastructure", StringComparison.Ordinal)
        || name.StartsWith("AtomPix.Workflows", StringComparison.Ordinal)
        || name.StartsWith("AtomPix.Imaging", StringComparison.Ordinal)
        || name.StartsWith("Avalonia", StringComparison.Ordinal)
        || name.StartsWith("AtomUI", StringComparison.Ordinal)
        || name.StartsWith("ImageMagick", StringComparison.Ordinal)
        || name.StartsWith("Magick", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal);
}
