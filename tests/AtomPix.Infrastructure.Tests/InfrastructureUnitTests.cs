namespace AtomPix.Infrastructure.Tests;

using AtomPix.Infrastructure.DependencyInjection;

public sealed class InfrastructureUnitTests
{
    [Fact]
    public void Dependency_injection_extension_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => InfrastructureServiceCollectionExtensions.AddAtomPixInfrastructure(null!));
        Assert.Throws<ArgumentNullException>(() => InfrastructureServiceCollectionExtensions.AddAtomPixInfrastructure(null!, "appdata", "temp"));
    }
}
