namespace AtomPix.Workflows.Tests;

using AtomPix.Workflows.DependencyInjection;

public sealed class WorkflowUnitTests
{
    [Fact]
    public void Dependency_injection_extension_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => WorkflowServiceCollectionExtensions.AddAtomPixWorkflows(null!));
    }
}
