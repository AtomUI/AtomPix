namespace AtomPix.Workflows.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using AtomPix.Core.Licensing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;
using AtomPix.Workflows.Settings;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFeatureAccessPolicy, DefaultFeatureAccessPolicy>();
        services.AddSingleton<ImageWorkflowServices>();
        services.AddTransient<OpenImageWorkflow>();
        services.AddTransient<CreatePreviewWorkflow>();
        services.AddTransient<CompressImageWorkflow>();
        services.AddTransient<ConvertImageWorkflow>();
        services.AddTransient<BatchCompressWorkflow>();
        services.AddTransient<BatchConvertWorkflow>();
        services.AddTransient<CompressWithDefaultSettingsWorkflow>();
        services.AddTransient<ConvertWithDefaultSettingsWorkflow>();
        services.AddTransient<BatchCompressWithDefaultSettingsWorkflow>();
        services.AddTransient<BatchConvertWithDefaultSettingsWorkflow>();
        services.AddTransient<LoadSettingsWorkflow>();
        services.AddTransient<SaveSettingsWorkflow>();
        services.AddTransient<AddRecentItemWorkflow>();
        return services;
    }
}
