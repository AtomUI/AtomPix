namespace AtomPix.Workflows.DependencyInjection;

using AtomPix.Workflows.Settings;
using AtomUI.City.Core.Modularity;
using AtomUI.City.State;
using Microsoft.Extensions.DependencyInjection;

[Module(
    AppSettingsStateCoordinator.OwnerModule,
    Version = "0.1.0",
    Description = "Registers AtomPix workflows and the application settings state projection.")]
public sealed class AtomPixWorkflowsModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.AddState();
        context.Services.AddAtomPixWorkflows();
    }

    public override void OnPreApplicationInitialization(
        ApplicationInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services
            .GetRequiredService<AppSettingsStateCoordinator>()
            .EnsureRegistered();
    }
}
