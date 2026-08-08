namespace AtomPix.Workflows.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;
using AtomPix.Workflows.Settings;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ImageWorkflowServices>();
        services.AddTransient<OpenImageWorkflow>();
        services.AddTransient<OpenFolderWorkflow>();
        services.AddTransient<AppendBatchInputsWorkflow>();
        services.AddTransient<CreatePreviewWorkflow>();
        services.AddTransient<CompressImageWorkflow>();
        services.AddTransient<ConvertImageWorkflow>();
        services.AddTransient<ResizeImageWorkflow>();
        services.AddTransient<CropImageWorkflow>();
        services.AddTransient<BatchCompressWorkflow>();
        services.AddTransient<BatchConvertWorkflow>();
        services.AddTransient<BatchResizeWorkflow>();
        services.AddTransient<CompressWithDefaultSettingsWorkflow>();
        services.AddTransient<ConvertWithDefaultSettingsWorkflow>();
        services.AddTransient<ResizeWithDefaultSettingsWorkflow>();
        services.AddTransient<CropWithDefaultSettingsWorkflow>();
        services.AddTransient<BatchCompressWithDefaultSettingsWorkflow>();
        services.AddTransient<BatchConvertWithDefaultSettingsWorkflow>();
        services.AddTransient<BatchResizeWithDefaultSettingsWorkflow>();
        services.AddTransient<LoadSettingsWorkflow>();
        services.AddTransient<SaveSettingsWorkflow>();
        services.AddTransient<AddRecentItemWorkflow>();
        services.AddTransient<LoadRecentItemsWorkflow>();
        services.AddTransient<RemoveRecentItemWorkflow>();
        services.AddTransient<ClearRecentItemsWorkflow>();
        return services;
    }
}
