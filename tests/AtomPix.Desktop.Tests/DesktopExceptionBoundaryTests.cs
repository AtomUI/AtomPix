namespace AtomPix.Desktop.Tests;

using AtomPix.Desktop.Platform;

public sealed class DesktopExceptionBoundaryTests
{
    [Fact]
    public void Detached_popup_target_is_a_known_transient_presentation_exception()
    {
        var exception = new InvalidOperationException("Target control is not attached to the visual tree");

        Assert.True(DesktopExceptionBoundary.IsTransientPopupDetachment(exception));
    }

    [Fact]
    public void Aggregate_is_transient_only_when_every_inner_exception_is_a_detached_popup_target()
    {
        var transient = new AggregateException(
            new InvalidOperationException("Target control is not attached to the visual tree"),
            new InvalidOperationException("Target control is not attached to the visual tree"));
        var mixed = new AggregateException(
            new InvalidOperationException("Target control is not attached to the visual tree"),
            new IOException("A real background failure"));

        Assert.True(DesktopExceptionBoundary.IsTransientPopupDetachment(transient));
        Assert.False(DesktopExceptionBoundary.IsTransientPopupDetachment(mixed));
    }

    [Fact]
    public void Other_invalid_operation_exceptions_remain_unexpected()
    {
        var exception = new InvalidOperationException("A different failure");

        Assert.False(DesktopExceptionBoundary.IsTransientPopupDetachment(exception));
    }
}
