namespace AtomPix.Desktop.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.ViewModels;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;
using AtomUI.Labs.Controls.ImageGallery;

public sealed class DesktopInteractionTests
{
    [Fact]
    public async Task Home_open_image_builds_lightweight_browser_collection_without_eager_probe()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "home-open.jpg"));
        var processor = new TestImageProcessor(path);
        var picker = new TestPicker(DesktopSelectionResult.Selected(path.Value));
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(picker, processor, navigation);

        await viewModel.OpenImageCommand.ExecuteAsync();

        Assert.Equal(0, processor.ProbeCallCount);
        Assert.NotNull(requested);
        Assert.Equal(DesktopRoute.Browse, requested!.Route);
        var context = Assert.IsType<BrowserNavigationContext>(requested.Context);
        Assert.Null(context.PreferredPath);
        Assert.Single(context.Items);
        Assert.Equal(DesktopContentState.Ready, viewModel.State);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Home_tool_entry_preserves_multi_selection_for_browser_batch_collection()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "home-tool-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "home-tool-b.jpg"));
        var processor = new TestImageProcessor(first);
        var picker = new TestPicker(DesktopSelectionResult.Selected(first.Value, second.Value));
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(picker, processor, navigation);

        await viewModel.OpenForCompressCommand.ExecuteAsync();

        Assert.Equal(0, processor.ProbeCallCount);
        Assert.Equal(DesktopRoute.Compress, requested?.Route);
        var context = Assert.IsType<BrowserToolNavigationContext>(requested?.Context);
        Assert.Equal([first, second], context.Browser.Items.Select(item => item.Path));
    }

    [Fact]
    public async Task Home_canceled_picker_does_not_call_workflow_or_navigate()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "not-used.jpg"));
        var processor = new TestImageProcessor(path);
        var picker = new TestPicker(DesktopSelectionResult.Canceled());
        var navigation = new DesktopNavigationCoordinator();
        var navigated = false;
        navigation.NavigationRequested += (_, _) => navigated = true;
        var viewModel = CreateHome(picker, processor, navigation);

        await viewModel.OpenImageCommand.ExecuteAsync();

        Assert.Equal(0, processor.ProbeCallCount);
        Assert.False(navigated);
        Assert.Equal(DesktopContentState.Ready, viewModel.State);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Home_open_folder_uses_folder_workflow_and_does_not_create_batch_input()
    {
        var directory = new LocalPath(Path.Combine(Path.GetTempPath(), "pictures"));
        var image = new LocalPath(Path.Combine(directory.Value, "image10.jpg"));
        var unsupported = new LocalPath(Path.Combine(directory.Value, "notes.txt"));
        var processor = new TestImageProcessor(image);
        var fileSystem = new TestFileSystem([image, unsupported]);
        var picker = new TestPicker(DesktopSelectionResult.Selected(directory.Value));
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(picker, processor, navigation, fileSystem);

        await viewModel.OpenFolderCommand.ExecuteAsync();

        var context = Assert.IsType<BrowserNavigationContext>(requested?.Context);
        Assert.Equal(directory, context.DirectoryPath);
        Assert.Single(context.Items);
        Assert.Equal(image, context.Items[0].Path);
    }

    [Fact]
    public async Task Browser_loads_probe_and_exposes_file_backed_gallery_items_without_decoding_in_view_model()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "browser.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            processor,
            navigation,
            new TestLauncher(),
            new TestClipboard());
        var context = new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(path, "browser.jpg")]);

        await browser.LoadAsync(context);

        Assert.Equal(DesktopContentState.Ready, browser.State);
        Assert.Equal(path.Value, browser.CurrentPath);
        Assert.Equal("1200 × 800", browser.CurrentDimensions);
        Assert.Equal(1, processor.ProbeCallCount);
        Assert.Equal(0, processor.PreviewCallCount);
        var galleryItem = Assert.Single(browser.GalleryItems);
        Assert.Same(browser.CurrentItem, galleryItem.Item);
        Assert.Equal(Path.GetFullPath(path.Value), galleryItem.Key);
        Assert.Equal(galleryItem.Key, galleryItem.MainImageSource.Identity);
    }

    [Fact]
    public async Task Browser_maps_resource_limits_and_crop_mode_to_image_gallery_contract()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "browser-crop-resource.jpg"));
        var processor = new TestImageProcessor(path);
        using var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(path, "browser-crop-resource.jpg")]));

        var resources = processor.Capabilities.Resources;
        Assert.Equal(resources.MaxInputFileSizeBytes, browser.GalleryLoadLimits.MaximumEncodedBytes);
        Assert.Equal(resources.MaxInputPixelCount, browser.GalleryLoadLimits.MaximumSourcePixelCount);
        Assert.Equal(Math.Max(resources.MaxInputWidth, resources.MaxInputHeight), browser.GalleryLoadLimits.MaximumDimension);
        Assert.Equal(resources.MaxInputPixelCount * 4, browser.GalleryLoadLimits.MaximumDecodedBytes);
        Assert.Equal(ImageGalleryMainImageMode.Presented, browser.GalleryMainImageMode);
        Assert.True(browser.CanUseGalleryFilmstripNavigation);
        Assert.True(browser.IsGalleryToolbarVisible);

        browser.SetCropMode(true);

        Assert.Equal(ImageGalleryMainImageMode.ResourceOnly, browser.GalleryMainImageMode);
        Assert.True(browser.CanUseGalleryFilmstripNavigation);
        Assert.False(browser.IsGalleryToolbarVisible);

        browser.SetInteractionLocked(true);
        Assert.False(browser.CanUseGalleryFilmstripNavigation);
    }

    [Fact]
    public async Task Browser_large_collection_exposes_lightweight_gallery_adapters_without_desktop_preview_memory()
    {
        var firstPath = new LocalPath(Path.Combine("C:\\images", "image-0.jpg"));
        var processor = new TestImageProcessor(firstPath);
        var navigation = new DesktopNavigationCoordinator();
        using var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            processor,
            navigation,
            new TestLauncher(),
            new TestClipboard());
        var candidates = Enumerable.Range(0, 6)
            .Select(index => new BrowserImageCandidate(
                new LocalPath(Path.Combine("C:\\images", $"image-{index}.jpg")),
                $"image-{index}.jpg"))
            .ToArray();

        await browser.LoadAsync(new BrowserNavigationContext(null, candidates));
        foreach (var item in browser.Items)
        {
            await browser.SelectItemCommand.ExecuteAsync(item);
        }

        Assert.Equal(candidates.Length, browser.GalleryItems.Count);
        Assert.Equal(candidates.Length, browser.GalleryItems.Select(item => item.Key).Distinct().Count());
        Assert.All(browser.GalleryItems, item => Assert.Equal(item.Key, item.MainImageSource.Identity));
        Assert.Equal(0, processor.PreviewCallCount);
    }

    [Fact]
    public void Navigation_coordinator_blocks_route_changes_until_shell_lock_is_released()
    {
        var navigation = new DesktopNavigationCoordinator();

        Assert.True(navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Compress)));
        navigation.SetNavigationLocked(true);

        Assert.False(navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Batch)));

        navigation.SetNavigationLocked(false);
        Assert.True(navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Batch)));
    }

    [Fact]
    public async Task Settings_save_updates_all_metadata_profiles_as_one_public_preference()
    {
        var store = new MutableSettingsStore(AppSettings.Default);
        var viewModel = CreateSettings(store);
        await viewModel.LoadAsync();

        viewModel.SelectedCompressionMode = viewModel.CompressionModes.Single(option => option.Value == CompressionMode.Custom);
        viewModel.CustomCompressionQuality = 76;
        viewModel.RemoveMetadata = false;
        await viewModel.SaveCommand.ExecuteAsync();

        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(store.LastSaved);
        Assert.Equal(CompressionMode.Custom, store.LastSaved!.DefaultCompressionProfile.Mode);
        Assert.Equal(76, store.LastSaved.DefaultCompressionProfile.Quality!.Value.Value);
        Assert.Equal(MetadataPolicy.Preserve, store.LastSaved.DefaultCompressionProfile.MetadataPolicy);
        Assert.Equal(MetadataPolicy.Preserve, store.LastSaved.DefaultConversionProfile.MetadataPolicy);
        Assert.Equal(MetadataPolicy.Preserve, store.LastSaved.DefaultSameFormatEncodingPolicy.MetadataPolicy);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(AppSettings.Default.ThemeMode, store.LastSaved.ThemeMode);
    }

    [Fact]
    public async Task Settings_invalid_dirty_draft_keeps_save_action_available_and_reports_validation_error()
    {
        var store = new MutableSettingsStore(AppSettings.Default);
        var viewModel = CreateSettings(store);
        await viewModel.LoadAsync();

        viewModel.BackgroundHex = "not-a-color";

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.IsDraftValid);
        Assert.True(viewModel.SaveCommand.CanExecute(null));

        await viewModel.SaveCommand.ExecuteAsync();

        Assert.Equal(0, store.SaveCount);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task Settings_restore_defaults_changes_only_draft_until_explicit_save()
    {
        var custom = new AppSettings(
            new CompressionProfile(CompressionMode.Custom, new ImageQuality(55), MetadataPolicy.Remove),
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultSameFormatEncodingPolicy,
            AppSettings.Default.DefaultOutputPolicy,
            ThemeMode.Dark,
            "zh-CN",
            new RecentItemsSettings(true, 8));
        var store = new MutableSettingsStore(custom);
        var dialogs = new TestDialogs { ConfirmResult = true };
        var viewModel = CreateSettings(store, dialogs);
        await viewModel.LoadAsync();

        await viewModel.RestoreDefaultsCommand.ExecuteAsync();

        Assert.True(viewModel.IsDirty);
        Assert.Equal(CompressionMode.Smart, viewModel.SelectedCompressionMode.Value);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Settings_close_discards_unsaved_draft_without_prompt()
    {
        var store = new MutableSettingsStore(AppSettings.Default);
        var dialogs = new TestDialogs();
        var viewModel = CreateSettings(store, dialogs);
        await viewModel.LoadAsync();
        viewModel.RecentMaxCount = 7;

        var canLeave = await viewModel.TryLeaveAsync();

        Assert.True(canLeave);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(20, viewModel.RecentMaxCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Settings_concurrent_preload_and_open_share_one_load()
    {
        var store = new DelayedSettingsStore();
        using var viewModel = CreateSettings(store);

        var preload = viewModel.LoadAsync();
        await store.LoadStarted.Task;
        var open = viewModel.LoadAsync();

        Assert.Equal(1, store.LoadCount);
        Assert.False(open.IsCompleted);

        store.Complete(AppSettings.Default);
        await Task.WhenAll(preload, open);

        Assert.Equal(1, store.LoadCount);
        Assert.True(viewModel.IsLoaded);
        Assert.True(viewModel.IsReady);
    }

    [Fact]
    public async Task Compression_saved_defaults_apply_only_to_new_draft()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "compression-settings-first.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "compression-settings-second.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var store = new MutableSettingsStore(AppSettings.Default);
        using var editor = CreateCompressionEditor(processor, fileSystem, new DesktopNavigationCoordinator(), store);
        await editor.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        editor.Output.SubfolderName = "CurrentDraft";

        var updated = CreateUpdatedEditorDefaults();
        await store.SaveAsync(updated, CancellationToken.None);
        await editor.SynchronizeInputAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("CurrentDraft", editor.Output.SubfolderName);
        Assert.Equal(CompressionMode.Smart, editor.SelectedMode.Value);

        await editor.LoadAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("NewDefaults", editor.Output.SubfolderName);
        Assert.Equal(CompressionMode.Custom, editor.SelectedMode.Value);
        Assert.Equal(37, editor.CustomQuality);
        Assert.False(editor.RemoveMetadata);
    }

    [Fact]
    public async Task Conversion_saved_defaults_apply_only_to_new_draft()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "conversion-settings-first.png"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "conversion-settings-second.png"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var store = new MutableSettingsStore(AppSettings.Default);
        using var editor = CreateConversionEditor(processor, fileSystem, new DesktopNavigationCoordinator(), store);
        await editor.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        editor.Output.SubfolderName = "CurrentDraft";

        var updated = CreateUpdatedEditorDefaults();
        await store.SaveAsync(updated, CancellationToken.None);
        await editor.SynchronizeInputAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("CurrentDraft", editor.Output.SubfolderName);
        Assert.Equal(OutputImageFormat.WebP, editor.SelectedFormat.Value);

        await editor.LoadAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("NewDefaults", editor.Output.SubfolderName);
        Assert.Equal(OutputImageFormat.Jpeg, editor.SelectedFormat.Value);
        Assert.Equal(41, editor.Quality);
        Assert.False(editor.RemoveMetadata);
    }

    [Fact]
    public async Task Resize_saved_defaults_apply_only_to_new_draft()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-settings-first.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-settings-second.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var store = new MutableSettingsStore(AppSettings.Default);
        using var editor = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            fileSystem,
            new DesktopNavigationCoordinator(),
            store);
        await editor.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        editor.Output.SubfolderName = "CurrentDraft";

        var updated = CreateUpdatedEditorDefaults();
        await store.SaveAsync(updated, CancellationToken.None);
        await editor.SynchronizeInputAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("CurrentDraft", editor.Output.SubfolderName);
        Assert.Equal(SameFormatEncodingPolicy.Default.LossyQuality, editor.EncodingPolicy.LossyQuality);

        await editor.LoadAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("NewDefaults", editor.Output.SubfolderName);
        Assert.Equal(new ImageQuality(77), editor.EncodingPolicy.LossyQuality);
        Assert.Equal(MetadataPolicy.Preserve, editor.EncodingPolicy.MetadataPolicy);
    }

    [Fact]
    public async Task Crop_saved_defaults_apply_only_to_new_draft()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "crop-settings-first.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "crop-settings-second.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var store = new MutableSettingsStore(AppSettings.Default);
        using var editor = CreateCropEditor(processor, fileSystem, new DesktopNavigationCoordinator(), store);
        await editor.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        editor.Output.SubfolderName = "CurrentDraft";

        var updated = CreateUpdatedEditorDefaults();
        await store.SaveAsync(updated, CancellationToken.None);
        await editor.SynchronizeInputAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("CurrentDraft", editor.Output.SubfolderName);
        Assert.Equal(SameFormatEncodingPolicy.Default.LossyQuality, editor.EncodingPolicy.LossyQuality);

        await editor.LoadAsync(new SingleImageNavigationContext(second, processor.Probe));

        Assert.Equal("NewDefaults", editor.Output.SubfolderName);
        Assert.Equal(new ImageQuality(77), editor.EncodingPolicy.LossyQuality);
        Assert.Equal(MetadataPolicy.Preserve, editor.EncodingPolicy.MetadataPolicy);
    }

    [Fact]
    public void Desktop_assembly_does_not_reference_forbidden_atomui_datagrid()
    {
        var references = typeof(App).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null)
            .Cast<string>();

        Assert.DoesNotContain(references, name =>
            name.Equals("AtomUI.Desktop.Controls.DataGrid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resize_editor_uses_last_edited_pixel_dimension_and_percentage_rules()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-draft.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            navigation);
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        viewModel.PixelWidth = 600;
        viewModel.PixelHeight = 100;

        Assert.Equal("150 × 100 px", viewModel.EstimatedSize);
        Assert.True(viewModel.CanStart);

        viewModel.SelectedMode = viewModel.ResizeModes.Single(option => option.Value == ResizeDraftMode.Percentage);
        viewModel.Percentage = 25;

        Assert.Equal("300 × 200 px", viewModel.EstimatedSize);
        Assert.True(viewModel.CanStart);
    }

    [Fact]
    public async Task Resize_editor_links_pixel_dimensions_only_while_aspect_ratio_is_enabled()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-linked-pixels.jpg"));
        var processor = new TestImageProcessor(path, width: 2604, height: 2084);
        using var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        viewModel.PixelWidth = 751;
        Assert.Equal(601, viewModel.PixelHeight);
        Assert.Equal(PixelDimensionAnchor.Width, viewModel.PixelAnchor);
        Assert.Equal("751 × 601 px", viewModel.EstimatedSize);

        viewModel.PixelHeight = 500;
        Assert.Equal(625, viewModel.PixelWidth);
        Assert.Equal(PixelDimensionAnchor.Height, viewModel.PixelAnchor);
        Assert.Equal("625 × 500 px", viewModel.EstimatedSize);

        viewModel.MaintainAspectRatio = false;
        viewModel.PixelWidth = 500;
        Assert.Equal(500, viewModel.PixelHeight);
    }

    [Fact]
    public async Task Resize_editor_executes_the_same_exact_target_that_it_estimates()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-exact-target.jpg"));
        var processor = new TestImageProcessor(path, width: 2604, height: 2084);
        using var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        viewModel.PixelWidth = 751;
        Assert.Equal("751 × 601 px", viewModel.EstimatedSize);

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(new ResolvedResizeSize(751, 601), processor.LastResizeRequest!.TargetSize);
    }

    [Fact]
    public async Task Resize_editor_preserves_height_anchor_when_the_current_gallery_item_changes()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-anchor-first.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-anchor-second.jpg"));
        var processor = new TestImageProcessor(first, width: 2604, height: 2084);
        using var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([first, second]),
            new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        viewModel.PixelHeight = 601;

        var firstProbe = processor.Probe;
        var secondProbe = new ImageProbeResult(
            second,
            firstProbe.Format,
            1000,
            500,
            firstProbe.FileSizeBytes,
            firstProbe.HasAlphaChannel,
            firstProbe.HasTransparency,
            firstProbe.IsAnimated,
            firstProbe.FrameCount,
            firstProbe.HasMetadata,
            firstProbe.HasColorProfile);
        await viewModel.SynchronizeInputAsync(new SingleImageNavigationContext(second, secondProbe));

        Assert.Equal(PixelDimensionAnchor.Height, viewModel.PixelAnchor);
        Assert.Equal(1202, viewModel.PixelWidth);
        Assert.Equal(601, viewModel.PixelHeight);
        Assert.Equal("1202 × 601 px", viewModel.EstimatedSize);
    }

    [Fact]
    public async Task Resize_editor_applies_a_batch_draft_atomically_without_changing_its_anchor()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-apply-batch-draft.jpg"));
        var processor = new TestImageProcessor(path, width: 2604, height: 2084);
        using var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        viewModel.ApplyResizeDraft(
            ResizeDraftMode.Pixel,
            width: 751,
            height: 601,
            anchor: PixelDimensionAnchor.Height,
            maintainAspectRatio: true,
            preventUpscaling: false,
            percentage: 50);

        Assert.Equal(PixelDimensionAnchor.Height, viewModel.PixelAnchor);
        Assert.Equal("751 × 601 px", viewModel.EstimatedSize);
    }

    [Fact]
    public async Task Resize_editor_executes_existing_workflow_and_releases_shell_lock()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-run.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            navigation);
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.SelectedMode = viewModel.ResizeModes.Single(option => option.Value == ResizeDraftMode.Percentage);
        viewModel.Percentage = 50;

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(1, processor.ResizeCallCount);
        Assert.Equal(DesktopExecutionState.Success, viewModel.ExecutionState);
        Assert.True(viewModel.HasResult);
        Assert.Equal("1200 × 800 → 600 × 400", viewModel.ResultDetails);
        Assert.False(navigation.IsNavigationLocked);
    }

    [Fact]
    public async Task Non_crop_editors_reuse_browser_preview_without_duplicate_encoding()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "editor-reuses-browser-preview.jpg"));
        var processor = new TestImageProcessor(path) { FailPreview = true };
        var fileSystem = new TestFileSystem([path]);
        using var resize = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            fileSystem,
            new DesktopNavigationCoordinator());
        using var compression = CreateCompressionEditor(processor, fileSystem, new DesktopNavigationCoordinator());
        using var conversion = CreateConversionEditor(processor, fileSystem, new DesktopNavigationCoordinator());

        var context = new SingleImageNavigationContext(path, processor.Probe);
        await compression.LoadAsync(context);
        await conversion.LoadAsync(context);
        await resize.LoadAsync(context);

        Assert.Equal(0, processor.PreviewCallCount);
        Assert.Equal(DesktopContentState.Ready, compression.ContentState);
        Assert.Equal(DesktopContentState.Ready, conversion.ContentState);
        Assert.Equal(DesktopContentState.Ready, resize.ContentState);
        Assert.False(compression.HasError);
        Assert.False(conversion.HasError);
        Assert.False(resize.HasError);
    }

    [Fact]
    public async Task Compression_editor_executes_custom_quality_and_releases_shell_lock()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "compress-run.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var viewModel = CreateCompressionEditor(processor, new TestFileSystem([path]), navigation);
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.SelectedMode = viewModel.Modes.Single(option => option.Value == CompressionMode.Custom);
        viewModel.CustomQuality = 76;

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(1, processor.CompressCallCount);
        Assert.Equal(76, processor.LastCompressRequest!.Profile.Quality!.Value.Value);
        Assert.Equal(DesktopExecutionState.Success, viewModel.ExecutionState);
        Assert.Equal("实际质量：76", viewModel.AppliedQualityText);
        Assert.True(viewModel.ResultFeedback.IsVisible);
        Assert.True(viewModel.ResultFeedback.IsSuccess);
        Assert.Contains("压缩完成", viewModel.ResultFeedback.Message);
        Assert.False(navigation.IsNavigationLocked);
    }

    [Fact]
    public async Task Compression_quality_slider_quantizes_visual_values_to_valid_integer_quality()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "compress-slider-quality.jpg"));
        var processor = new TestImageProcessor(path);
        using var viewModel = CreateCompressionEditor(
            processor,
            new TestFileSystem([path]),
            new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.SelectedMode = viewModel.Modes.Single(option => option.Value == CompressionMode.Custom);

        viewModel.CustomQualitySlider = 64.6;

        Assert.Equal(65, viewModel.CustomQuality);
        Assert.True(viewModel.CanStart);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Conversion_editor_flattens_transparency_with_selected_background()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "convert-run.png"));
        var processor = new TestImageProcessor(path, ImageFormatKind.Png, hasTransparency: true);
        var navigation = new DesktopNavigationCoordinator();
        var viewModel = CreateConversionEditor(processor, new TestFileSystem([path]), navigation);
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.SelectedFormat = viewModel.Formats.Single(option => option.Value == OutputImageFormat.Jpeg);
        viewModel.BackgroundHex = "#123456";

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(1, processor.ConvertCallCount);
        Assert.Equal(RgbColor.Parse("#123456"), processor.LastConvertRequest!.Profile.TransparencyPolicy.OpaqueBackgroundColor);
        Assert.Equal(DesktopExecutionState.Success, viewModel.ExecutionState);
        Assert.Equal("已使用 #123456 填充透明区域", viewModel.ResultTransparency);
        Assert.False(navigation.IsNavigationLocked);
    }

    [Fact]
    public async Task Crop_editor_applies_ratio_with_flooring_and_executes_exact_rectangle()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "crop-run.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var viewModel = CreateCropEditor(processor, new TestFileSystem([path]), navigation);
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.SelectedRatio = viewModel.Ratios.Single(option => option.Label == "3:2");
        viewModel.CropX = 10;
        viewModel.CropY = 20;
        viewModel.CropWidth = 601;

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(400, viewModel.CropHeight);
        Assert.Equal(1, processor.CropCallCount);
        Assert.Equal(new CropRectangle(10, 20, 601, 400), processor.LastCropRequest!.CropArea);
        Assert.Equal(DesktopExecutionState.Success, viewModel.ExecutionState);
        Assert.Equal("实际输出：601 × 400 px", viewModel.ResultDetails);
        Assert.False(navigation.IsNavigationLocked);
    }

    [Fact]
    public async Task Crop_editor_exposes_only_supported_ratios_and_preserves_selection_when_entering_custom_mode()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "crop-ratios.jpg"));
        var processor = new TestImageProcessor(path, width: 1200, height: 800);
        using var viewModel = CreateCropEditor(processor, new TestFileSystem([path]), new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        Assert.Equal(["自定义", "3:2", "4:3", "5:4", "1:1"], viewModel.Ratios.Select(option => option.Label));
        Assert.True(viewModel.IsCustomRatio);

        viewModel.SelectedRatio = viewModel.Ratios.Single(option => option.Label == "1:1");
        Assert.False(viewModel.IsCustomRatio);
        Assert.Equal(800, viewModel.CropWidth);
        Assert.Equal(800, viewModel.CropHeight);
        Assert.Equal(200, viewModel.CropX);
        Assert.Equal(0, viewModel.CropY);

        viewModel.SelectedRatio = viewModel.Ratios[0];
        Assert.True(viewModel.IsCustomRatio);
        Assert.Equal(800, viewModel.CropWidth);
        Assert.Equal(800, viewModel.CropHeight);
        Assert.Equal(200, viewModel.CropX);
        Assert.Equal(0, viewModel.CropY);
    }

    [Fact]
    public async Task Home_drop_single_file_reuses_open_image_workflow_and_recent_navigation()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "drop-file.jpg"));
        var processor = new TestImageProcessor(path);
        var fileSystem = new TestFileSystem([path]);
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(new TestPicker(DesktopSelectionResult.Canceled()), processor, navigation, fileSystem);

        await viewModel.OpenDroppedSourcesCommand.ExecuteAsync([path.Value]);

        Assert.Equal(1, processor.ProbeCallCount);
        var context = Assert.IsType<BrowserNavigationContext>(requested?.Context);
        Assert.Equal(path, context.PreferredPath);
        Assert.Equal(DesktopContentState.Ready, viewModel.State);
    }

    [Fact]
    public async Task Home_drop_multiple_sources_stays_on_home_and_reports_validation_error()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "drop-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "drop-b.jpg"));
        var processor = new TestImageProcessor(first);
        var navigation = new DesktopNavigationCoordinator();
        var navigated = false;
        navigation.NavigationRequested += (_, _) => navigated = true;
        var viewModel = CreateHome(new TestPicker(DesktopSelectionResult.Canceled()), processor, navigation, new TestFileSystem([first, second]));

        await viewModel.OpenDroppedSourcesCommand.ExecuteAsync([first.Value, second.Value]);

        Assert.False(navigated);
        Assert.Equal(0, processor.ProbeCallCount);
        Assert.Equal(DesktopContentState.Failure, viewModel.State);
        Assert.Contains("一次只能", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Browser_previous_next_commands_follow_selection_while_zoom_is_owned_by_image_gallery()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "browser-1.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "browser-2.jpg"));
        var processor = new TestImageProcessor(first);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(first, "browser-1.jpg"), new BrowserImageCandidate(second, "browser-2.jpg")]));

        Assert.False(browser.CanGoPrevious);
        Assert.True(browser.CanGoNext);
        Assert.True(browser.HasItems);
        Assert.Equal(0, browser.CurrentIndex);
        Assert.Equal("1 / 2", browser.CurrentPositionText);
        await browser.NextCommand.ExecuteAsync();
        Assert.Equal(second, browser.CurrentItem!.Path);
        Assert.Equal(1, browser.CurrentIndex);
        Assert.Equal("2 / 2", browser.CurrentPositionText);
        Assert.True(browser.CanGoPrevious);
        Assert.False(browser.CanGoNext);

        Assert.Same(browser.CurrentItem!.GalleryItem, browser.SelectedGalleryItem);
        Assert.Equal(0, processor.PreviewCallCount);
    }

    [Fact]
    public async Task Browser_keeps_unavailable_item_then_allows_explicit_removal()
    {
        var bad = new LocalPath(Path.Combine(Path.GetTempPath(), "missing.jpg"));
        var good = new LocalPath(Path.Combine(Path.GetTempPath(), "available.jpg"));
        var processor = new TestImageProcessor(good);
        processor.UnavailablePaths.Add(bad.Value);
        var browser = CreateBrowser(processor);

        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(bad, "missing.jpg"), new BrowserImageCandidate(good, "available.jpg")],
            bad));

        var unavailable = Assert.Single(browser.Items, item => item.Path == bad);
        Assert.True(unavailable.IsUnavailable);
        Assert.Equal(good, browser.CurrentItem!.Path);
        unavailable.RemoveCommand.Execute(null);
        Assert.DoesNotContain(browser.Items, item => item.Path == bad);
    }

    [Fact]
    public async Task Browser_feature_commands_use_independent_processor_capabilities()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "single-frame.webp"));
        var processor = new TestImageProcessor(path, ImageFormatKind.WebP);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(null, [new BrowserImageCandidate(path, "single-frame.webp")]));

        Assert.True(browser.CanCompress);
        Assert.True(browser.CanConvert);
        Assert.False(browser.CanResize);
        Assert.False(browser.CanCrop);
        Assert.True(browser.CompressCommand.CanExecute(null));
        Assert.True(browser.ConvertCommand.CanExecute(null));
        Assert.False(browser.ResizeCommand.CanExecute(null));
        Assert.False(browser.CropCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(DesktopRoute.Compress)]
    [InlineData(DesktopRoute.Convert)]
    [InlineData(DesktopRoute.Resize)]
    [InlineData(DesktopRoute.Crop)]
    public async Task Browser_quick_action_commands_capture_current_image_and_navigate(DesktopRoute route)
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), $"browser-{route}.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        using var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            processor,
            navigation,
            new TestLauncher(),
            new TestClipboard());

        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(path, Path.GetFileName(path.Value))]));

        var command = route switch
        {
            DesktopRoute.Compress => browser.CompressCommand,
            DesktopRoute.Convert => browser.ConvertCommand,
            DesktopRoute.Resize => browser.ResizeCommand,
            DesktopRoute.Crop => browser.CropCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };
        command.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(route, requested.Route);
        var context = Assert.IsType<SingleImageNavigationContext>(requested.Context);
        Assert.Equal(path, context.InputPath);
        Assert.Equal(processor.Probe, context.Probe);
    }

    [Fact]
    public void Output_policy_editor_validates_conditional_fields_and_builds_snapshot()
    {
        var editor = new OutputPolicyEditorViewModel(new TestPicker(DesktopSelectionResult.Canceled()));
        editor.SelectedLocation = editor.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        editor.CustomDirectory = string.Empty;
        Assert.False(editor.IsValid);
        Assert.Contains("自定义输出目录", editor.ValidationError);

        editor.CustomDirectory = Path.Combine(Path.GetTempPath(), "AtomPix-Output");
        editor.FileNamePattern = "export_{name}_{index}";
        editor.SelectedOverwrite = editor.OverwritePolicies.Single(option => option.Value == OverwritePolicy.Skip);

        Assert.True(editor.TryBuild(out var policy, out var error));
        Assert.Null(error);
        Assert.Equal(OutputLocationMode.CustomDirectory, policy!.LocationPolicy.Mode);
        Assert.Equal("export_{name}_{index}", policy.NamingPolicy.Pattern);
        Assert.Equal(OverwritePolicy.Skip, policy.OverwritePolicy);
    }

    [Fact]
    public void Output_policy_editor_exposes_one_contextual_location_surface_and_live_subfolder_hint()
    {
        var editor = new OutputPolicyEditorViewModel(new TestPicker(DesktopSelectionResult.Canceled()));

        Assert.True(editor.IsSubfolder);
        Assert.False(editor.IsSameAsInput);
        Assert.False(editor.IsCustomDirectory);
        editor.SubfolderName = "Exports";
        Assert.Equal("图片将保存到：每张原图所在目录 / Exports", editor.SubfolderDestinationHint);

        editor.SelectedLocation = editor.Locations.Single(option => option.Value == OutputLocationMode.SameAsInput);
        Assert.False(editor.IsSubfolder);
        Assert.True(editor.IsSameAsInput);
        Assert.False(editor.IsCustomDirectory);

        editor.SelectedLocation = editor.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        Assert.False(editor.IsSubfolder);
        Assert.False(editor.IsSameAsInput);
        Assert.True(editor.IsCustomDirectory);
    }

    [Fact]
    public void Output_policy_editor_builds_all_three_naming_contracts_and_updates_live_preview()
    {
        var editor = new OutputPolicyEditorViewModel(new TestPicker(DesktopSelectionResult.Canceled()));

        Assert.True(editor.IsAppendSuffix);
        editor.FileNameSuffix = "_export";
        Assert.Equal("示例图片_export", editor.NamingPreview);
        Assert.True(editor.TryBuild(out var suffixPolicy, out var suffixError));
        Assert.Null(suffixError);
        Assert.Equal(OutputNamingMode.AppendSuffix, suffixPolicy!.NamingPolicy.Mode);
        Assert.Equal("_export", suffixPolicy.NamingPolicy.Suffix);

        editor.SelectedNaming = editor.NamingModes.Single(option => option.Value == OutputNamingMode.KeepOriginalName);
        Assert.True(editor.IsKeepOriginalName);
        Assert.Equal("示例图片", editor.NamingPreview);
        Assert.True(editor.TryBuild(out var originalPolicy, out _));
        Assert.Equal(OutputNamingMode.KeepOriginalName, originalPolicy!.NamingPolicy.Mode);

        editor.SelectedNaming = editor.NamingModes.Single(option => option.Value == OutputNamingMode.CustomPattern);
        editor.CustomFileNamePattern = "{index}_{name}_done";
        Assert.True(editor.IsCustomPattern);
        Assert.Equal("001_示例图片_done", editor.NamingPreview);
        Assert.False(editor.CanInsertIndexToken);
        Assert.True(editor.TryBuild(out var customPolicy, out _));
        Assert.Equal(OutputNamingMode.CustomPattern, customPolicy!.NamingPolicy.Mode);
        Assert.Equal("{index}_{name}_done", customPolicy.NamingPolicy.Pattern);

        editor.FileNamePattern = "legacy_{name}";
        Assert.True(editor.IsCustomPattern);
        Assert.Equal("legacy_{name}", editor.CustomFileNamePattern);
    }

    [Fact]
    public async Task Compression_editor_submits_visible_output_policy_instead_of_hidden_default()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "compress-output.jpg"));
        var outputDirectory = Path.Combine(Path.GetTempPath(), "AtomPix-Custom");
        var processor = new TestImageProcessor(path);
        var viewModel = CreateCompressionEditor(processor, new TestFileSystem([path]), new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.Output.SelectedLocation = viewModel.Output.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        viewModel.Output.CustomDirectory = outputDirectory;
        viewModel.Output.FileNamePattern = "manual_{name}";
        viewModel.Output.SelectedOverwrite = viewModel.Output.OverwritePolicies.Single(option => option.Value == OverwritePolicy.Skip);

        await viewModel.StartCommand.ExecuteAsync();

        Assert.NotNull(processor.LastCompressRequest);
        Assert.Equal(Path.Combine(outputDirectory, "manual_compress-output.jpg"), processor.LastCompressRequest!.OutputPath.Value);
    }

    [Fact]
    public async Task Invalid_visible_output_policy_reports_top_level_feedback_on_submission()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "invalid-output.jpg"));
        var processor = new TestImageProcessor(path);
        var viewModel = CreateCompressionEditor(processor, new TestFileSystem([path]), new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.Output.FileNamePattern = string.Empty;

        Assert.False(viewModel.CanStart);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.Null(viewModel.DraftError);
        Assert.Contains("文件名格式", viewModel.Output.ValidationError);

        await viewModel.StartCommand.ExecuteAsync();

        Assert.Null(processor.LastCompressRequest);
        Assert.True(viewModel.ResultFeedback.IsVisible);
        Assert.True(viewModel.ResultFeedback.IsWarning);
        Assert.Contains("文件名格式", viewModel.ResultFeedback.Message);
    }

    [Fact]
    public async Task Batch_editor_freezes_visible_output_policy_and_auto_appends_index_for_multiple_items()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-b.jpg"));
        var outputDirectory = Path.Combine(Path.GetTempPath(), "AtomPix-Batch");
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var viewModel = CreateBatch(
            new TestPicker(DesktopSelectionResult.Selected(first.Value, second.Value)),
            processor,
            fileSystem);
        await viewModel.LoadAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();
        viewModel.Output.SelectedLocation = viewModel.Output.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        viewModel.Output.CustomDirectory = outputDirectory;
        viewModel.Output.FileNamePattern = "export";

        Assert.True(viewModel.PatternWillAppendIndex);
        await viewModel.StartCommand.ExecuteAsync();

        Assert.Equal(2, processor.CompressCallCount);
        Assert.All(viewModel.Items, item => Assert.StartsWith(outputDirectory, item.OutputPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("export_001.jpg", viewModel.Items[0].OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export_002.jpg", viewModel.Items[1].OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.HasResult);
    }

    [Fact]
    public async Task Recent_missing_item_is_retained_and_relocated_only_after_replacement_validates()
    {
        var stale = new LocalPath(Path.Combine(Path.GetTempPath(), "stale.jpg"));
        var replacement = new LocalPath(Path.Combine(Path.GetTempPath(), "replacement.jpg"));
        var processor = new TestImageProcessor(replacement);
        processor.UnavailablePaths.Add(stale.Value);
        var recentStore = new TestRecentItemsStore([
            new RecentItem(stale, RecentItemKind.File, DateTimeOffset.UtcNow.AddMinutes(-1))
        ]);
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(
            new TestPicker(DesktopSelectionResult.Selected(replacement.Value)),
            processor,
            navigation,
            new TestFileSystem([replacement]),
            recentStore);
        await viewModel.LoadRecentAsync();
        var staleItem = Assert.Single(viewModel.RecentItems);

        await viewModel.OpenRecentCommand.ExecuteAsync(staleItem);
        Assert.True(staleItem.IsUnavailable);
        Assert.Contains(recentStore.Items, item => item.Path == stale);

        await viewModel.RelocateRecentCommand.ExecuteAsync(staleItem);
        Assert.DoesNotContain(recentStore.Items, item => item.Path == stale);
        Assert.Contains(recentStore.Items, item => item.Path == replacement);
        Assert.Equal(replacement, Assert.IsType<BrowserNavigationContext>(requested?.Context).PreferredPath);
    }

    [Fact]
    public async Task Home_drop_directory_reuses_folder_workflow_and_keeps_browser_semantics()
    {
        var directory = new LocalPath(Path.Combine(Path.GetTempPath(), "drop-folder"));
        var image = new LocalPath(Path.Combine(directory.Value, "inside.png"));
        var processor = new TestImageProcessor(image, ImageFormatKind.Png);
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            navigation,
            new TestFileSystem([image]));

        await viewModel.OpenDroppedSourcesCommand.ExecuteAsync([directory.Value]);

        var context = Assert.IsType<BrowserNavigationContext>(requested?.Context);
        Assert.Equal(directory, context.DirectoryPath);
        Assert.Single(context.Items);
        Assert.Equal(image, context.Items[0].Path);
    }

    [Fact]
    public async Task Browser_delegates_thumbnail_loading_to_image_gallery_sources()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "thumb-1.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "thumb-2.jpg"));
        var processor = new TestImageProcessor(first);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(first, "thumb-1.jpg"), new BrowserImageCandidate(second, "thumb-2.jpg")]));
        Assert.Equal(2, browser.GalleryItems.Count);
        Assert.All(browser.GalleryItems, item => Assert.Null(item.ThumbnailImageSource));
        Assert.Equal(0, processor.PreviewCallCount);
    }

    [Fact]
    public async Task Browser_add_images_appends_gallery_sources_without_decoding_or_losing_selection()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "browser-append-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "browser-append-b.jpg"));
        var processor = new TestImageProcessor(first);
        var picker = new TestPicker(DesktopSelectionResult.Selected(first.Value, second.Value));
        using var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            processor,
            new DesktopNavigationCoordinator(),
            new TestLauncher(),
            new TestClipboard(),
            picker: picker);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(first, Path.GetFileName(first.Value))]));
        var originalCurrent = browser.CurrentItem;

        await browser.AddImagesCommand.ExecuteAsync();
        await browser.AddImagesCommand.ExecuteAsync();

        Assert.Equal(2, browser.Items.Count);
        Assert.Same(originalCurrent, browser.CurrentItem);
        Assert.Equal(0, processor.PreviewCallCount);
        Assert.Equal(second, browser.Items[1].Path);
        Assert.Equal(2, browser.GalleryItems.Count);
    }

    [Fact]
    public async Task Tool_drawer_session_updates_batch_action_when_browser_collection_changes()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "tool-session.jpg"));
        var processor = new TestImageProcessor(path);
        var batch = CreateBatch(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]));
        var navigation = new DesktopNavigationCoordinator();
        using var editor = CreateCompressionEditor(processor, new TestFileSystem([path]), navigation);
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        using var session = new ToolDrawerSessionViewModel(
            editor,
            batch,
            1,
            _ => Task.FromResult(true),
            static () => { },
            navigation);

        Assert.False(session.HasBatchOption);
        session.UpdateItemCount(3);

        Assert.True(session.HasBatchOption);
        Assert.Equal("批量处理", session.BatchActionLabel);
        Assert.Equal("单张处理", session.SingleActionLabel);
        Assert.True(session.StartBatchCommand.CanExecute(null));
        Assert.Same(editor, session.SingleContent);
        Assert.True(session.IsIdle);
        Assert.Null(typeof(ToolDrawerSessionViewModel).GetProperty("ActiveContent"));
        Assert.Null(typeof(ToolDrawerSessionViewModel).GetProperty("IsBatchMode"));
    }

    [Fact]
    public async Task Tool_drawer_allows_batch_submission_attempt_so_shell_can_report_invalid_draft()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "invalid-session.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        using var editor = CreateCompressionEditor(processor, new TestFileSystem([path]), navigation);
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        editor.Output.SelectedLocation = editor.Output.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        editor.Output.CustomDirectory = string.Empty;
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, new TestFileSystem([path]));
        var prepareCallCount = 0;
        using var session = new ToolDrawerSessionViewModel(
            editor,
            batch,
            2,
            _ =>
            {
                prepareCallCount++;
                return Task.FromResult(false);
            },
            static () => { },
            navigation);

        Assert.True(session.CanStartBatch);
        Assert.True(session.StartBatchCommand.CanExecute(null));
        Assert.False(ShellViewModel.TryCaptureBatchDraft(editor, batch, out var rejectedDraft));
        Assert.Null(rejectedDraft);
        await session.StartBatchCommand.ExecuteAsync();
        Assert.Equal(1, prepareCallCount);
    }

    [Fact]
    public async Task Tool_drawer_batch_command_prepares_and_awaits_the_real_batch_execution()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "session-batch-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "session-batch-b.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var navigation = new DesktopNavigationCoordinator();
        using var editor = CreateCompressionEditor(processor, fileSystem, navigation);
        await editor.LoadAsync(new SingleImageNavigationContext(first, processor.Probe));
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, fileSystem);
        using var session = new ToolDrawerSessionViewModel(
            editor,
            batch,
            2,
            async cancellationToken =>
            {
                if (!ShellViewModel.TryCaptureBatchDraft(editor, batch, out var applyDraft)) return false;
                await batch.PrepareAsync(BatchTaskKind.Compress, [first, second], cancellationToken);
                applyDraft!();
                return batch.DraftError is null && batch.CanAttemptStart;
            },
            static () => { },
            navigation);

        await session.StartBatchCommand.ExecuteAsync();

        Assert.Equal(2, processor.CompressCallCount);
        Assert.True(batch.HasResult);
        Assert.False(session.IsBatchExecuting);
    }

    [Fact]
    public async Task Tool_drawer_reenables_batch_immediately_after_single_execution_finishes()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "session-single-then-batch.jpg"));
        var processor = new TestImageProcessor(path);
        var fileSystem = new TestFileSystem([path]);
        var navigation = new DesktopNavigationCoordinator();
        using var editor = CreateCompressionEditor(processor, fileSystem, navigation);
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, fileSystem);
        using var session = new ToolDrawerSessionViewModel(
            editor,
            batch,
            2,
            _ => Task.FromResult(true),
            static () => { },
            navigation);

        Assert.True(session.CanStartBatch);
        await editor.StartCommand.ExecuteAsync();

        Assert.Equal(DesktopExecutionState.Success, editor.ExecutionState);
        Assert.True(editor.StartCommand.CanExecute(null));
        Assert.True(session.CanStartBatch);
        Assert.True(session.StartBatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resize_batch_draft_preserves_prevent_upscaling()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-no-upscale.jpg"));
        var processor = new TestImageProcessor(path, width: 800, height: 600);
        var fileSystem = new TestFileSystem([path]);
        using var editor = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            fileSystem,
            new DesktopNavigationCoordinator());
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        editor.PixelWidth = 1600;
        editor.PixelHeight = 1200;
        editor.PreventUpscaling = true;
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, fileSystem);

        Assert.True(ShellViewModel.TryCaptureBatchDraft(editor, batch, out var applyDraft));
        await batch.PrepareAsync(BatchTaskKind.Resize, [path]);
        applyDraft!();

        Assert.True(batch.PreventUpscaling);
        Assert.Equal("800 × 600", batch.Items[0].EstimatedSize);
    }

    [Fact]
    public async Task Resize_batch_draft_preserves_the_last_edited_dimension_anchor()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-height-anchor.jpg"));
        var processor = new TestImageProcessor(path, width: 2604, height: 2084);
        var fileSystem = new TestFileSystem([path]);
        using var editor = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            fileSystem,
            new DesktopNavigationCoordinator());
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        editor.PixelHeight = 601;
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, fileSystem);

        Assert.True(ShellViewModel.TryCaptureBatchDraft(editor, batch, out var applyDraft));
        await batch.PrepareAsync(BatchTaskKind.Resize, [path]);
        applyDraft!();

        Assert.Equal(PixelDimensionAnchor.Height, batch.PixelAnchor);
        Assert.Equal("751 × 601", batch.Items[0].EstimatedSize);
    }

    [Fact]
    public async Task Shell_folder_to_compress_batch_path_captures_the_editor_instead_of_the_session_wrapper()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "shell-folder-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "shell-folder-b.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var navigation = new DesktopNavigationCoordinator();
        var picker = new TestPicker(DesktopSelectionResult.Canceled());
        var dialogs = new TestDialogs();
        var clipboard = new TestClipboard();
        var launcher = new TestLauncher();
        var outputGuard = new ResultOutputGuard(fileSystem);
        var settingsStore = new TestSettingsStore();
        var services = CreateImageWorkflowServices(processor, fileSystem);
        var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor), processor, navigation, launcher, clipboard);
        var compress = CreateCompressionEditor(processor, fileSystem, navigation);
        var convert = CreateConversionEditor(processor, fileSystem, navigation);
        var resize = CreateResizeEditor(picker, processor, fileSystem, navigation);
        var crop = new CropEditorViewModel(
            picker, launcher, dialogs, clipboard, outputGuard,
            new OpenImageWorkflow(processor), new LoadSettingsWorkflow(settingsStore),
            new CropImageWorkflow(services), navigation);
        var batch = CreateBatch(picker, processor, fileSystem, dialogs, clipboard, navigation);
        var settings = CreateSettings(settingsStore, dialogs);
        var feedback = new TestFeedback();
        using var shell = new ShellViewModel(
            navigation,
            CreateHome(picker, processor, navigation, fileSystem),
            browser,
            compress,
            convert,
            resize,
            crop,
            batch,
            settings,
            dialogs,
            feedback);

        navigation.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(
                null,
                [new BrowserImageCandidate(first, "shell-folder-a.jpg"), new BrowserImageCandidate(second, "shell-folder-b.jpg")],
                first,
                processor.Probe)));
        await WaitUntilAsync(() => browser.Items.Count == 2 && browser.State == DesktopContentState.Ready);
        navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Compress));
        await WaitUntilAsync(() => shell.DrawerContent is ToolDrawerSessionViewModel session
                                   && session.SingleContent is CompressionEditorViewModel editor
                                   && editor.IsContentReady);
        var drawer = Assert.IsType<ToolDrawerSessionViewModel>(shell.DrawerContent);

        await drawer.StartBatchCommand.ExecuteAsync();

        Assert.Equal(2, processor.CompressCallCount);
        Assert.True(batch.HasResult);
        Assert.DoesNotContain(feedback.Messages, message => message.Contains("当前批量处理配置无效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shell_gallery_selection_updates_resize_single_input_without_resetting_the_visible_draft()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "shell-resize-first.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "shell-resize-second.jpg"));
        var processor = new TestImageProcessor(first);
        var fileSystem = new TestFileSystem([first, second]);
        var navigation = new DesktopNavigationCoordinator();
        var picker = new TestPicker(DesktopSelectionResult.Canceled());
        var dialogs = new TestDialogs();
        var clipboard = new TestClipboard();
        var launcher = new TestLauncher();
        var outputGuard = new ResultOutputGuard(fileSystem);
        var settingsStore = new TestSettingsStore();
        var services = CreateImageWorkflowServices(processor, fileSystem);
        var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor), processor, navigation, launcher, clipboard);
        var compress = CreateCompressionEditor(processor, fileSystem, navigation);
        var convert = CreateConversionEditor(processor, fileSystem, navigation);
        var resize = CreateResizeEditor(picker, processor, fileSystem, navigation);
        var crop = new CropEditorViewModel(
            picker, launcher, dialogs, clipboard, outputGuard,
            new OpenImageWorkflow(processor), new LoadSettingsWorkflow(settingsStore),
            new CropImageWorkflow(services), navigation);
        var batch = CreateBatch(picker, processor, fileSystem, dialogs, clipboard, navigation);
        var settings = CreateSettings(settingsStore, dialogs);
        using var shell = new ShellViewModel(
            navigation,
            CreateHome(picker, processor, navigation, fileSystem),
            browser,
            compress,
            convert,
            resize,
            crop,
            batch,
            settings,
            dialogs,
            new TestFeedback());

        navigation.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(
                null,
                [new BrowserImageCandidate(first, "first.jpg"), new BrowserImageCandidate(second, "second.jpg")],
                first,
                processor.Probe)));
        await WaitUntilAsync(() => browser.State == DesktopContentState.Ready);
        navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Resize));
        await WaitUntilAsync(() => resize.IsContentReady);
        resize.SelectedMode = resize.ResizeModes.Single(option => option.Value == ResizeDraftMode.Percentage);
        resize.Percentage = 75;

        await browser.SelectItemCommand.ExecuteAsync(browser.Items[1]);
        await WaitUntilAsync(() => string.Equals(resize.InputPath, second.Value, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(75, resize.Percentage);
        await resize.StartCommand.ExecuteAsync();
        Assert.Equal(second, processor.LastResizeRequest?.InputPath);
        Assert.Equal(90, processor.LastResizeRequest?.EncodingPolicy.LossyQuality.Value);
    }

    [Fact]
    public async Task Shell_batch_draft_capture_preserves_custom_directory_and_webp_format()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-webp-source.png"));
        var secondPath = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-webp-second.png"));
        var customDirectory = Path.Combine(Path.GetTempPath(), "AtomPix-WebP-Output");
        var processor = new TestImageProcessor(path, ImageFormatKind.Png);
        var fileSystem = new TestFileSystem([path, secondPath]);
        var navigation = new DesktopNavigationCoordinator();
        using var editor = CreateConversionEditor(processor, fileSystem, navigation);
        await editor.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        editor.SelectedFormat = editor.Formats.Single(option => option.Value == OutputImageFormat.WebP);
        editor.Output.SelectedLocation = editor.Output.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        editor.Output.CustomDirectory = customDirectory;
        var batch = CreateBatch(new TestPicker(DesktopSelectionResult.Canceled()), processor, fileSystem);

        var captured = ShellViewModel.TryCaptureBatchDraft(editor, batch, out var applyDraft);
        await batch.PrepareAsync(BatchTaskKind.Convert, [path, secondPath]);
        applyDraft?.Invoke();
        await batch.StartCommand.ExecuteAsync();

        Assert.True(captured);
        Assert.Equal(OutputImageFormat.WebP, batch.SelectedFormat.Value);
        Assert.True(batch.Output.TryBuild(out var output, out var error), error);
        Assert.Equal(OutputLocationMode.CustomDirectory, output!.LocationPolicy.Mode);
        Assert.Equal(customDirectory, output.LocationPolicy.CustomDirectory);
        Assert.Equal(2, processor.ConvertCallCount);
        Assert.Equal(OutputImageFormat.WebP, processor.LastConvertRequest!.Profile.OutputFormat);
        Assert.All(batch.Items, item =>
        {
            Assert.StartsWith(customDirectory, item.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".webp", item.OutputPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Browser_does_not_decode_large_images_or_own_image_gallery_zoom_policy()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "large-browser.jpg"));
        var processor = new TestImageProcessor(path, width: 4000, height: 3000);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(null, [new BrowserImageCandidate(path, "large-browser.jpg")]));

        Assert.Null(processor.LastPreviewRequest);
        Assert.Equal(0, processor.PreviewCallCount);
        Assert.Equal("large-browser.jpg", Assert.Single(browser.GalleryItems).Title);
    }

    [Fact]
    public async Task Browser_rapid_gallery_selection_is_latest_wins()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-1.jpg"));
        var delayed = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-2.jpg"));
        var latest = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-3.jpg"));
        var processor = new TestImageProcessor(first);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [
                new BrowserImageCandidate(first, "latest-1.jpg"),
                new BrowserImageCandidate(delayed, "latest-2.jpg"),
                new BrowserImageCandidate(latest, "latest-3.jpg")
            ]));

        browser.CurrentItem = browser.Items[1];
        browser.CurrentItem = browser.Items[2];
        await WaitUntilAsync(() => browser.State == DesktopContentState.Ready && browser.CurrentItem?.Path == latest);

        await Task.Yield();
        Assert.Equal(latest, browser.CurrentItem!.Path);
        Assert.Equal(DesktopContentState.Ready, browser.State);
    }

    [Fact]
    public async Task Output_policy_directory_picker_cancel_preserves_existing_draft()
    {
        var editor = new OutputPolicyEditorViewModel(new TestPicker(DesktopSelectionResult.Canceled()));
        editor.CustomDirectory = "D:\\existing";
        var originalMode = editor.SelectedLocation;

        await editor.ChooseDirectoryCommand.ExecuteAsync();

        Assert.Equal("D:\\existing", editor.CustomDirectory);
        Assert.Same(originalMode, editor.SelectedLocation);
        Assert.False(editor.HasPickerError);
    }

    [Fact]
    public async Task Batch_repeated_file_selection_appends_only_new_sources()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "append-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "append-b.jpg"));
        var processor = new TestImageProcessor(first);
        var viewModel = CreateBatch(
            new TestPicker(DesktopSelectionResult.Selected(first.Value, second.Value)),
            processor,
            new TestFileSystem([first, second]));
        await viewModel.LoadAsync();

        await viewModel.AddFilesCommand.ExecuteAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();

        Assert.Equal(2, viewModel.Items.Count);
        Assert.Contains("重复 2", viewModel.NoticeMessage);
    }

    [Fact]
    public async Task Batch_invalid_visible_output_location_rejects_submission_without_calling_workflow()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-invalid.jpg"));
        var processor = new TestImageProcessor(path);
        var viewModel = CreateBatch(
            new TestPicker(DesktopSelectionResult.Selected(path.Value)),
            processor,
            new TestFileSystem([path]));
        await viewModel.LoadAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();
        viewModel.Output.SelectedLocation = viewModel.Output.Locations.Single(option => option.Value == OutputLocationMode.CustomDirectory);
        viewModel.Output.CustomDirectory = string.Empty;

        Assert.False(viewModel.CanStart);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.Contains("自定义输出目录", viewModel.DraftError);
        await viewModel.StartCommand.ExecuteAsync();
        Assert.Contains("自定义输出目录", viewModel.ErrorMessage);
        Assert.Equal(0, processor.CompressCallCount);
    }

    [Fact]
    public async Task Conversion_background_preview_tracks_only_transparent_to_jpeg_case()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "preview-background.png"));
        var processor = new TestImageProcessor(path, ImageFormatKind.Png, hasTransparency: true);
        var viewModel = CreateConversionEditor(processor, new TestFileSystem([path]), new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        Assert.Null(viewModel.PreviewBackgroundHex);
        viewModel.SelectedFormat = viewModel.Formats.Single(option => option.Value == OutputImageFormat.Jpeg);
        viewModel.BackgroundHex = "#ABCDEF";
        Assert.Equal("#ABCDEF", viewModel.PreviewBackgroundHex);
        viewModel.SelectedFormat = viewModel.Formats.Single(option => option.Value == OutputImageFormat.Png);
        Assert.Null(viewModel.PreviewBackgroundHex);
    }

    [Fact]
    public async Task Settings_save_failure_keeps_dirty_draft_and_exposes_recoverable_error()
    {
        var store = new FailingSaveSettingsStore(AppSettings.Default);
        var viewModel = CreateSettings(store);
        await viewModel.LoadAsync();
        viewModel.RecentMaxCount = 7;

        await viewModel.SaveCommand.ExecuteAsync();

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.HasError);
        Assert.Contains("保存失败", viewModel.ErrorMessage);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Browser_reuses_probe_cache_and_releases_the_whole_gallery_session_on_leave()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "cache-a.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "cache-b.jpg"));
        var processor = new TestImageProcessor(first);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(first, "cache-a.jpg"), new BrowserImageCandidate(second, "cache-b.jpg")]));

        await browser.NextCommand.ExecuteAsync();
        await browser.PreviousCommand.ExecuteAsync();

        Assert.Equal(2, processor.ProbeCallCount);
        Assert.Equal(0, processor.PreviewCallCount);
        browser.BackCommand.Execute(null);
        Assert.Empty(browser.Items);
        Assert.Null(browser.CurrentItem);
        Assert.Equal(DesktopContentState.Empty, browser.State);
    }

    [Fact]
    public async Task Single_editor_detects_a_result_removed_after_processing_and_explains_recovery()
    {
        var input = new LocalPath(Path.Combine(Path.GetTempPath(), "removed-result.jpg"));
        var processor = new TestImageProcessor(input);
        var fileSystem = new TestFileSystem([input]);
        var viewModel = CreateCompressionEditor(processor, fileSystem, new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(input, processor.Probe));
        await viewModel.StartCommand.ExecuteAsync();

        Assert.True(viewModel.IsSuccess);
        Assert.True(viewModel.IsResultOutputMissing);
        var output = new LocalPath(viewModel.ResultOutputPath);
        fileSystem.Add(output);
        viewModel.RefreshResultAvailability();
        Assert.True(viewModel.IsResultOutputAvailable);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        fileSystem.Remove(output);
        viewModel.RefreshResultAvailability();

        Assert.True(viewModel.IsResultOutputMissing);
        Assert.Contains(nameof(CompressionEditorViewModel.IsResultOutputMissing), changed);
        Assert.True(viewModel.OpenOutputCommand.CanExecute(null));
        await viewModel.OpenOutputCommand.ExecuteAsync();

        Assert.Contains("输出文件已不存在", viewModel.ErrorMessage);
        Assert.False(viewModel.IsResultOutputAvailable);
    }

    [Fact]
    public async Task Batch_terminal_result_is_read_only_until_an_explicit_recovery_action()
    {
        var input = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-terminal.jpg"));
        var processor = new TestImageProcessor(input);
        var viewModel = CreateBatch(
            new TestPicker(DesktopSelectionResult.Selected(input.Value)),
            processor,
            new TestFileSystem([input]));
        await viewModel.LoadAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();
        await viewModel.StartCommand.ExecuteAsync();

        Assert.True(viewModel.HasResult);
        Assert.False(viewModel.CanEditInputs);
        Assert.False(viewModel.CanEditDraft);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.AddFilesCommand.CanExecute(null));
        Assert.False(viewModel.RemoveInputCommand.CanExecute(viewModel.Items[0]));
        Assert.False(viewModel.Items[0].CanRemove);
    }

    [Fact]
    public async Task Batch_failed_recovery_restores_the_exact_submitted_processing_and_output_snapshot()
    {
        var input = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-recovery.jpg"));
        var processor = new TestImageProcessor(input);
        processor.FailCompressPaths.Add(input.Value);
        var viewModel = CreateBatch(
            new TestPicker(DesktopSelectionResult.Selected(input.Value)),
            processor,
            new TestFileSystem([input]));
        await viewModel.LoadAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();
        viewModel.SelectedCompressionMode = viewModel.CompressionModes.Single(option => option.Value == CompressionMode.Custom);
        viewModel.CustomQuality = 73;
        viewModel.Output.FileNamePattern = "submitted_{name}";
        viewModel.Output.SelectedOverwrite = viewModel.Output.OverwritePolicies.Single(option => option.Value == OverwritePolicy.Skip);
        await viewModel.StartCommand.ExecuteAsync();
        Assert.True(viewModel.HasFailedItems);

        // Programmatic mutations model stale controls or a later implementation mistake;
        // recovery must still use the immutable submission snapshot.
        viewModel.CustomQuality = 21;
        viewModel.Output.FileNamePattern = "mutated";
        viewModel.Output.SelectedOverwrite = viewModel.Output.OverwritePolicies.Single(option => option.Value == OverwritePolicy.AutoRename);
        viewModel.RetryFailedCommand.Execute(null);

        Assert.False(viewModel.HasResult);
        Assert.True(viewModel.HasPreviousResult);
        Assert.Equal(73, viewModel.CustomQuality);
        Assert.Equal("submitted_{name}", viewModel.Output.FileNamePattern);
        Assert.Equal(OverwritePolicy.Skip, viewModel.Output.SelectedOverwrite.Value);
        Assert.Single(viewModel.Items);
    }

    [Fact]
    public async Task Batch_failed_item_exposes_details_diagnostic_copy_and_relocates_into_a_new_draft()
    {
        var missing = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-missing.jpg"));
        var replacement = new LocalPath(Path.Combine(Path.GetTempPath(), "batch-replacement.jpg"));
        var processor = new TestImageProcessor(missing);
        processor.FailCompressPaths.Add(missing.Value);
        var dialogs = new TestDialogs();
        var clipboard = new TestClipboard();
        var picker = new TestPicker(DesktopSelectionResult.Selected(missing.Value));
        var viewModel = CreateBatch(
            picker,
            processor,
            new TestFileSystem([missing, replacement]),
            dialogs,
            clipboard);
        await viewModel.LoadAsync();
        await viewModel.AddFilesCommand.ExecuteAsync();
        await viewModel.StartCommand.ExecuteAsync();
        var failed = Assert.Single(viewModel.Items);

        Assert.True(failed.CanViewDetails);
        Assert.True(failed.CanRelocate);
        Assert.True(failed.HasDiagnosticId);
        await viewModel.ViewItemDetailsCommand.ExecuteAsync(failed);
        await viewModel.CopyItemDiagnosticIdCommand.ExecuteAsync(failed);
        Assert.Equal("批量项目详情", dialogs.LastInformationTitle);
        Assert.Contains("输入：", dialogs.LastInformationMessage);
        Assert.Equal(failed.DiagnosticId, clipboard.LastText);

        picker.Result = DesktopSelectionResult.Selected(replacement.Value);
        await viewModel.RelocateInputCommand.ExecuteAsync(failed);

        Assert.False(viewModel.HasResult);
        Assert.True(viewModel.HasPreviousResult);
        Assert.Equal(replacement, Assert.Single(viewModel.Items).Path);
    }

    private static HomePageViewModel CreateHome(
        IDesktopPickerService picker,
        IImageProcessor processor,
        DesktopNavigationCoordinator navigation,
        IFileSystemService? fileSystem = null,
        TestRecentItemsStore? recentStore = null)
    {
        var settings = new TestSettingsStore();
        recentStore ??= new TestRecentItemsStore();
        var effectiveFileSystem = fileSystem ?? new TestFileSystem([]);
        return new HomePageViewModel(
            picker,
            effectiveFileSystem,
            new OpenImageWorkflow(processor),
            new OpenFolderWorkflow(effectiveFileSystem, processor),
            navigation,
            new LoadSettingsWorkflow(settings),
            new AtomPix.Workflows.RecentItems.LoadRecentItemsWorkflow(recentStore),
            new AtomPix.Workflows.RecentItems.AddRecentItemWorkflow(recentStore),
            new AtomPix.Workflows.RecentItems.RemoveRecentItemWorkflow(recentStore),
            new AtomPix.Workflows.RecentItems.ClearRecentItemsWorkflow(recentStore),
            new TestDialogs(),
            new TestClipboard());
    }

    private static ImageBrowserViewModel CreateBrowser(IImageProcessor processor) => new(
        new OpenImageWorkflow(processor),
        processor,
        new DesktopNavigationCoordinator(),
        new TestLauncher(),
        new TestClipboard());

    private static BatchTaskViewModel CreateBatch(
        IDesktopPickerService picker,
        IImageProcessor processor,
        IFileSystemService fileSystem,
        TestDialogs? dialogs = null,
        TestClipboard? clipboard = null,
        DesktopNavigationCoordinator? navigation = null)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new BatchTaskViewModel(
            picker,
            dialogs ?? new TestDialogs(),
            new TestLauncher(),
            clipboard ?? new TestClipboard(),
            new InlineDispatcher(),
            new ResultOutputGuard(fileSystem),
            new AppendBatchInputsWorkflow(fileSystem, processor),
            new OpenImageWorkflow(processor),
            new LoadSettingsWorkflow(new TestSettingsStore()),
            new BatchCompressWorkflow(services),
            new BatchConvertWorkflow(services),
            new BatchResizeWorkflow(services),
            navigation ?? new DesktopNavigationCoordinator());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("Desktop test condition was not reached.");
            await Task.Delay(10);
        }
    }

    private static ResizeEditorViewModel CreateResizeEditor(
        IDesktopPickerService picker,
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation,
        IAppSettingsStore? settingsStore = null)
    {
        var services = new ImageWorkflowServices(
            processor,
            fileSystem);
        var settings = settingsStore ?? new TestSettingsStore();
        return new ResizeEditorViewModel(
            picker,
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new LoadSettingsWorkflow(settings),
            new ResizeImageWorkflow(services),
            processor,
            navigation);
    }

    private static SettingsPageViewModel CreateSettings(
        IAppSettingsStore store,
        TestDialogs? dialogs = null) =>
        new(
            new LoadSettingsWorkflow(store),
            new SaveSettingsWorkflow(store),
            dialogs ?? new TestDialogs(),
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestPathProvider(),
            new TestClipboard());

    private static CompressionEditorViewModel CreateCompressionEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation,
        IAppSettingsStore? settingsStore = null)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new CompressionEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new LoadSettingsWorkflow(settingsStore ?? new TestSettingsStore()),
            new CompressImageWorkflow(services),
            navigation);
    }

    private static ConversionEditorViewModel CreateConversionEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation,
        IAppSettingsStore? settingsStore = null)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new ConversionEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new LoadSettingsWorkflow(settingsStore ?? new TestSettingsStore()),
            new ConvertImageWorkflow(services),
            navigation);
    }

    private static CropEditorViewModel CreateCropEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation,
        IAppSettingsStore? settingsStore = null)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new CropEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new LoadSettingsWorkflow(settingsStore ?? new TestSettingsStore()),
            new CropImageWorkflow(services),
            navigation);
    }

    private static ImageWorkflowServices CreateImageWorkflowServices(
        IImageProcessor processor,
        IFileSystemService fileSystem) =>
        new(processor, fileSystem);

    private static AppSettings CreateUpdatedEditorDefaults()
    {
        const MetadataPolicy metadata = MetadataPolicy.Preserve;
        return new AppSettings(
            new CompressionProfile(CompressionMode.Custom, new ImageQuality(37), metadata),
            new ConversionProfile(
                OutputImageFormat.Jpeg,
                new ImageQuality(41),
                metadata,
                new TransparencyPolicy(RgbColor.Parse("#22AA44"))),
            new SameFormatEncodingPolicy(new ImageQuality(77), metadata),
            new OutputPolicy(
                new OutputLocationPolicy(OutputLocationMode.Subfolder, null, "NewDefaults"),
                new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, "{name}_new"),
                OverwritePolicy.Skip),
            AppSettings.Default.ThemeMode,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems);
    }

    private sealed class TestPicker : IDesktopPickerService
    {
        public TestPicker(DesktopSelectionResult result) => Result = result;

        public DesktopSelectionResult Result { get; set; }

        public Task<DesktopSelectionResult> PickSingleImageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<DesktopSelectionResult> PickImagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<DesktopSelectionResult> PickFolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }

    private sealed class TestLauncher : IDesktopLauncherService
    {
        public Task<bool> OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class TestDialogs : IDesktopDialogService
    {
        public bool ConfirmResult { get; init; }
        public string? LastInformationTitle { get; private set; }
        public string? LastInformationMessage { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken) =>
            Task.FromResult(ConfirmResult);

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken)
        {
            LastInformationTitle = title;
            LastInformationMessage = message;
            return Task.CompletedTask;
        }

    }

    private sealed class TestClipboard : IDesktopClipboardService
    {
        public string? LastText { get; private set; }

        public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken)
        {
            LastText = text;
            return Task.FromResult(true);
        }
    }

    private sealed class InlineDispatcher : IDesktopDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class TestPathProvider : IAppPathProvider
    {
        public LocalPath AppDataDirectory { get; } = new(Path.Combine(Path.GetTempPath(), "AtomPix-Desktop-Tests"));
        public LocalPath TempDirectory { get; } = new(Path.GetTempPath());
    }

    private sealed class TestFeedback : IDesktopFeedbackService
    {
        public List<string> Messages { get; } = [];

        public void ShowMessage(
            string message,
            DesktopFeedbackSeverity severity = DesktopFeedbackSeverity.Information,
            TimeSpan? expiration = null) => Messages.Add(message);

        public void ShowNotification(DesktopNotificationRequest request) => Messages.Add(request.Content);
    }

    private sealed class MutableSettingsStore : IAppSettingsStore
    {
        private AppSettings _settings;

        public MutableSettingsStore(AppSettings settings) => _settings = settings;

        public int SaveCount { get; private set; }
        public AppSettings? LastSaved { get; private set; }

        public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<AppSettings>.Success(_settings));

        public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            LastSaved = settings;
            _settings = settings;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class DelayedSettingsStore : IAppSettingsStore
    {
        private readonly TaskCompletionSource<OperationResult<AppSettings>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> LoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadCount { get; private set; }

        public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            LoadStarted.TrySetResult(true);
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success());

        public void Complete(AppSettings settings) =>
            _completion.TrySetResult(OperationResult<AppSettings>.Success(settings));
    }

    private sealed class TestSettingsStore : IAppSettingsStore
    {
        public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<AppSettings>.Success(AppSettings.Default));

        public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success());
    }

    private sealed class FailingSaveSettingsStore : IAppSettingsStore
    {
        private readonly AppSettings _settings;

        public FailingSaveSettingsStore(AppSettings settings) => _settings = settings;

        public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<AppSettings>.Success(_settings));

        public Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Failure(new AtomPixError(
                AtomPixErrorCode.SettingsSaveFailed,
                AtomPixErrorCategory.FileSystem,
                "Synthetic settings save failure.")));
    }

    private sealed class TestRecentItemsStore : IRecentItemsStore
    {
        private IReadOnlyList<RecentItem> _items;

        public TestRecentItemsStore(IReadOnlyList<RecentItem>? items = null) => _items = items?.ToArray() ?? [];

        public IReadOnlyList<RecentItem> Items => _items;

        public Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<IReadOnlyList<RecentItem>>.Success(_items));

        public Task<OperationResult> SaveAsync(IReadOnlyList<RecentItem> items, CancellationToken cancellationToken)
        {
            _items = items.ToArray();
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class TestFileSystem : IFileSystemService
    {
        private readonly List<LocalPath> _files;

        public TestFileSystem(IReadOnlyList<LocalPath> files) => _files = [.. files];

        public void Add(LocalPath path)
        {
            if (!FileExists(path)) _files.Add(path);
        }

        public void Remove(LocalPath path) => _files.RemoveAll(file => PathsEqual(file, path));

        public bool FileExists(LocalPath path) => _files.Any(file => PathsEqual(file, path));

        public bool DirectoryExists(LocalPath path) => true;

        public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<long>.Success(1024));

        public Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesAsync(
            LocalPath directory,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<IReadOnlyList<LocalPath>>.Success(_files));

        public OperationResult<LocalPath> NormalizePath(LocalPath path) => OperationResult<LocalPath>.Success(path);

        public bool PathsEqual(LocalPath left, LocalPath right) =>
            string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

        public int ComparePaths(LocalPath left, LocalPath right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Value, right.Value);

        public LocalPath Combine(LocalPath directory, string fileName) => new(Path.Combine(directory.Value, fileName));

        public string GetFileName(LocalPath path) => Path.GetFileName(path.Value);

        public string GetFileNameWithoutExtension(LocalPath path) => Path.GetFileNameWithoutExtension(path.Value);

        public string GetExtension(LocalPath path) => Path.GetExtension(path.Value);

        public LocalPath ChangeExtension(LocalPath path, string extension) => new(Path.ChangeExtension(path.Value, extension));

        public LocalPath BuildIndexedPath(LocalPath basePath, int index) =>
            new(Path.Combine(
                Path.GetDirectoryName(basePath.Value)!,
                $"{Path.GetFileNameWithoutExtension(basePath.Value)}_{index}{Path.GetExtension(basePath.Value)}"));
    }

    private sealed class TestImageProcessor : IImageProcessor
    {
        public static readonly byte[] PreviewPayload = [1, 2, 3, 4];
        private readonly ImageProbeResult _probe;

        public TestImageProcessor(
            LocalPath path,
            ImageFormatKind format = ImageFormatKind.Jpeg,
            bool hasTransparency = false,
            int width = 1200,
            int height = 800)
        {
            _probe = new ImageProbeResult(
                path,
                format,
                width,
                height,
                4096,
                hasTransparency,
                hasTransparency,
                false,
                1,
                true,
                true);
        }

        public int ProbeCallCount { get; private set; }

        public int PreviewCallCount { get; private set; }

        public int ResizeCallCount { get; private set; }

        public int CompressCallCount { get; private set; }

        public int ConvertCallCount { get; private set; }

        public int CropCallCount { get; private set; }

        public HashSet<string> UnavailablePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FailPreview { get; init; }

        public HashSet<string> FailCompressPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, TaskCompletionSource> PreviewGates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ImageCompressRequest? LastCompressRequest { get; private set; }

        public ImageConvertRequest? LastConvertRequest { get; private set; }

        public ImageCropRequest? LastCropRequest { get; private set; }

        public ImageResizeRequest? LastResizeRequest { get; private set; }

        public ImagePreviewRequest? LastPreviewRequest { get; private set; }

        public ImageProbeResult Probe => _probe with { };

        public ImageProcessorCapabilities Capabilities { get; } = new(
            new HashSet<ImageFormatKind>
            {
                ImageFormatKind.Jpeg,
                ImageFormatKind.Png,
                ImageFormatKind.WebP,
                ImageFormatKind.Bmp,
                ImageFormatKind.Gif,
                ImageFormatKind.Tiff
            },
            new HashSet<OutputImageFormat>
            {
                OutputImageFormat.Jpeg,
                OutputImageFormat.Png,
                OutputImageFormat.WebP
            },
            true,
            false,
            new ImageResourceCapabilities(1_000_000, 10_000, 10_000, 100_000_000, 10_000, 10_000, 100_000_000),
            new ImageResizeCapabilities(
                new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png },
                10_000,
                10_000,
                100_000_000),
            new ImageCropCapabilities(
                new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png },
                10_000,
                10_000,
                100_000_000));

        public Task<OperationResult<ImageProbeResult>> ProbeAsync(
            ImageProbeRequest request,
            CancellationToken cancellationToken)
        {
            ProbeCallCount++;
            if (UnavailablePaths.Contains(request.InputPath.Value))
            {
                return Task.FromResult(OperationResult<ImageProbeResult>.Failure(new AtomPixError(
                    AtomPixErrorCode.InputFileNotFound,
                    AtomPixErrorCategory.FileSystem,
                    "Missing test image.")));
            }
            return Task.FromResult(OperationResult<ImageProbeResult>.Success(_probe with { }));
        }

        public async Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(
            ImagePreviewRequest request,
            CancellationToken cancellationToken)
        {
            PreviewCallCount++;
            LastPreviewRequest = request;
            if (FailPreview)
            {
                return OperationResult<ImagePreviewResult>.Failure(new AtomPixError(
                    AtomPixErrorCode.InvalidImageFile,
                    AtomPixErrorCategory.ImageProcessing,
                    "Synthetic preview failure."));
            }
            if (PreviewGates.TryGetValue(request.InputPath.Value, out var gate))
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            return OperationResult<ImagePreviewResult>.Success(
                new ImagePreviewResult(PreviewPayload, "image/jpeg", 1200, 800));
        }

        public Task<OperationResult<ImageCompressResult>> CompressAsync(
            ImageCompressRequest request,
            CancellationToken cancellationToken)
        {
            CompressCallCount++;
            LastCompressRequest = request;
            if (FailCompressPaths.Contains(request.InputPath.Value))
            {
                return Task.FromResult(OperationResult<ImageCompressResult>.Failure(new AtomPixError(
                    AtomPixErrorCode.InputFileNotFound,
                    AtomPixErrorCategory.FileSystem,
                    "Synthetic missing batch input.",
                    new Dictionary<string, string> { ["DiagnosticId"] = "APX-TEST-DIAGNOSTIC-001" })));
            }
            return Task.FromResult(OperationResult<ImageCompressResult>.Success(new ImageCompressResult(
                request.InputPath,
                request.OutputPath,
                _probe.Format,
                _probe.Format,
                _probe.FileSizeBytes,
                2048,
                request.Profile.Quality)));
        }

        public Task<OperationResult<ImageConvertResult>> ConvertAsync(
            ImageConvertRequest request,
            CancellationToken cancellationToken)
        {
            ConvertCallCount++;
            LastConvertRequest = request;
            var outputFormat = request.Profile.OutputFormat switch
            {
                OutputImageFormat.Jpeg => ImageFormatKind.Jpeg,
                OutputImageFormat.Png => ImageFormatKind.Png,
                OutputImageFormat.WebP => ImageFormatKind.WebP,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };
            var transparency = !_probe.HasTransparency
                ? new TransparencyProcessingResult(TransparencyOutcome.NotPresent, null)
                : request.Profile.OutputFormat == OutputImageFormat.Jpeg
                    ? new TransparencyProcessingResult(TransparencyOutcome.Flattened, request.Profile.TransparencyPolicy.OpaqueBackgroundColor)
                    : new TransparencyProcessingResult(TransparencyOutcome.Preserved, null);
            return Task.FromResult(OperationResult<ImageConvertResult>.Success(new ImageConvertResult(
                request.InputPath,
                request.OutputPath,
                _probe.Format,
                outputFormat,
                _probe.FileSizeBytes,
                3072,
                transparency)));
        }

        public Task<OperationResult<ImageResizeResult>> ResizeAsync(
            ImageResizeRequest request,
            CancellationToken cancellationToken)
        {
            ResizeCallCount++;
            LastResizeRequest = request;
            return Task.FromResult(OperationResult<ImageResizeResult>.Success(new ImageResizeResult(
                request.InputPath,
                request.OutputPath,
                ImageFormatKind.Jpeg,
                new ImageSize(1200, 800),
                request.TargetSize.ToImageSize(),
                4096,
                2048)));
        }

        public Task<OperationResult<ImageCropResult>> CropAsync(
            ImageCropRequest request,
            CancellationToken cancellationToken)
        {
            CropCallCount++;
            LastCropRequest = request;
            return Task.FromResult(OperationResult<ImageCropResult>.Success(new ImageCropResult(
                request.InputPath,
                request.OutputPath,
                _probe.Format,
                new ImageSize(_probe.Width, _probe.Height),
                new ImageSize(request.CropArea.Width, request.CropArea.Height),
                _probe.FileSizeBytes,
                2048)));
        }
    }
}
