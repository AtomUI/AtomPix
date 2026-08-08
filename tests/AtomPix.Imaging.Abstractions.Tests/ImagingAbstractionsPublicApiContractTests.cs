namespace AtomPix.Imaging.Abstractions.Tests;

using System.Reflection;
using AtomPix.Core.Results;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;

public sealed class ImagingAbstractionsPublicApiContractTests
{
    [Fact]
    public void Imaging_abstractions_public_type_surface_is_explicitly_owned()
    {
        var publicTypes = typeof(IImageProcessor).Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "AtomPix.Imaging.Abstractions.Formats.ImageFormatKind",
            "AtomPix.Imaging.Abstractions.Processing.IImageProcessor",
            "AtomPix.Imaging.Abstractions.Processing.ImageCompressRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImageCompressResult",
            "AtomPix.Imaging.Abstractions.Processing.ImageConvertRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImageConvertResult",
            "AtomPix.Imaging.Abstractions.Processing.ImageCropCapabilities",
            "AtomPix.Imaging.Abstractions.Processing.ImageCropRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImageCropResult",
            "AtomPix.Imaging.Abstractions.Processing.ImagePreviewRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImagePreviewResult",
            "AtomPix.Imaging.Abstractions.Processing.ImageProbeRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImageProbeResult",
            "AtomPix.Imaging.Abstractions.Processing.ImageProcessingDetails",
            "AtomPix.Imaging.Abstractions.Processing.ImageProcessorCapabilities",
            "AtomPix.Imaging.Abstractions.Processing.ImageResizeCapabilities",
            "AtomPix.Imaging.Abstractions.Processing.ImageResizeRequest",
            "AtomPix.Imaging.Abstractions.Processing.ImageResizeResult",
            "AtomPix.Imaging.Abstractions.Processing.ImageResourceCapabilities",
            "AtomPix.Imaging.Abstractions.Processing.TransparencyOutcome",
            "AtomPix.Imaging.Abstractions.Processing.TransparencyProcessingResult"
        }.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, publicTypes);
    }

    [Fact]
    public void Image_processor_contract_exposes_six_atomic_async_operations()
    {
        var methods = typeof(IImageProcessor).GetMethods()
            .Where(method => method.Name != "get_Capabilities")
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CompressAsync", "ConvertAsync", "CreatePreviewAsync", "CropAsync", "ProbeAsync", "ResizeAsync"], methods.Select(method => method.Name).ToArray());
        AssertMethod<ImageCompressRequest, ImageCompressResult>(methods.Single(method => method.Name == "CompressAsync"));
        AssertMethod<ImageConvertRequest, ImageConvertResult>(methods.Single(method => method.Name == "ConvertAsync"));
        AssertMethod<ImagePreviewRequest, ImagePreviewResult>(methods.Single(method => method.Name == "CreatePreviewAsync"));
        AssertMethod<ImageCropRequest, ImageCropResult>(methods.Single(method => method.Name == "CropAsync"));
        AssertMethod<ImageProbeRequest, ImageProbeResult>(methods.Single(method => method.Name == "ProbeAsync"));
        AssertMethod<ImageResizeRequest, ImageResizeResult>(methods.Single(method => method.Name == "ResizeAsync"));
    }

    [Fact]
    public void Image_format_kind_declares_only_contract_formats()
    {
        var names = Enum.GetNames<ImageFormatKind>();

        Assert.Equal(["Unknown", "Jpeg", "Png", "WebP", "Bmp", "Gif", "Tiff"], names);
    }

    private static void AssertMethod<TRequest, TResult>(MethodInfo method)
    {
        Assert.Equal(typeof(Task<OperationResult<TResult>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(TRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }
}

