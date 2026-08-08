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

public sealed class DesktopInteractionTests
{
    [Fact]
    public async Task Home_open_image_uses_workflow_then_navigates_to_browser()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "home-open.jpg"));
        var processor = new TestImageProcessor(path);
        var picker = new TestPicker(DesktopSelectionResult.Selected(path.Value));
        var navigation = new DesktopNavigationCoordinator();
        DesktopNavigationRequest? requested = null;
        navigation.NavigationRequested += (_, request) => requested = request;
        var viewModel = CreateHome(picker, processor, navigation);

        await viewModel.OpenImageCommand.ExecuteAsync();

        Assert.Equal(1, processor.ProbeCallCount);
        Assert.NotNull(requested);
        Assert.Equal(DesktopRoute.Browse, requested!.Route);
        var context = Assert.IsType<BrowserNavigationContext>(requested.Context);
        Assert.Equal(path, context.PreferredPath);
        Assert.Single(context.Items);
        Assert.Equal(DesktopContentState.Ready, viewModel.State);
        Assert.False(viewModel.HasError);
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
    public async Task Browser_loads_probe_and_preview_without_framework_image_types_in_view_model()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "browser.jpg"));
        var processor = new TestImageProcessor(path);
        var navigation = new DesktopNavigationCoordinator();
        var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
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
        Assert.Equal(TestImageProcessor.PreviewPayload, browser.PreviewBytes);
        Assert.Equal(1, processor.ProbeCallCount);
        Assert.Equal(1, processor.PreviewCallCount);
    }

    [Fact]
    public async Task Browser_bounds_preview_and_thumbnail_memory_during_large_navigation_session()
    {
        var firstPath = new LocalPath(Path.Combine("C:\\images", "image-0.jpg"));
        var processor = new TestImageProcessor(firstPath);
        var navigation = new DesktopNavigationCoordinator();
        using var browser = new ImageBrowserViewModel(
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
            processor,
            navigation,
            new TestLauncher(),
            new TestClipboard(),
            new BrowserCacheOptions(
                previewByteBudget: 8,
                previewEntryLimit: 2,
                thumbnailByteBudget: 8,
                thumbnailEntryLimit: 2,
                probeEntryLimit: 2));
        var candidates = Enumerable.Range(0, 6)
            .Select(index => new BrowserImageCandidate(
                new LocalPath(Path.Combine("C:\\images", $"image-{index}.jpg")),
                $"image-{index}.jpg"))
            .ToArray();

        await browser.LoadAsync(new BrowserNavigationContext(null, candidates));
        foreach (var item in browser.Items)
        {
            await item.EnsureThumbnailCommand.ExecuteAsync();
            await browser.SelectItemCommand.ExecuteAsync(item);
        }

        var snapshot = browser.CacheSnapshot;
        Assert.InRange(snapshot.PreviewEntryCount, 0, 2);
        Assert.InRange(snapshot.PreviewBytes, 0, 8);
        Assert.Equal(2, snapshot.RetainedThumbnailCount);
        Assert.Equal(8, snapshot.RetainedThumbnailBytes);
        Assert.Equal(2, browser.Items.Count(item => item.ThumbnailBytes is not null));
        Assert.Null(browser.Items[0].ThumbnailBytes);
        Assert.InRange(snapshot.ProbeEntryCount, 0, 2);
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
        var appearance = new TestAppearance();
        var viewModel = CreateSettings(store, appearance);
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
        Assert.Equal(store.LastSaved.ThemeMode, appearance.LastApplied);
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
        var viewModel = CreateSettings(store, new TestAppearance(), dialogs);
        await viewModel.LoadAsync();

        await viewModel.RestoreDefaultsCommand.ExecuteAsync();

        Assert.True(viewModel.IsDirty);
        Assert.Equal(CompressionMode.Smart, viewModel.SelectedCompressionMode.Value);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Settings_discard_choice_restores_original_snapshot_before_leaving()
    {
        var store = new MutableSettingsStore(AppSettings.Default);
        var dialogs = new TestDialogs { UnsavedChoice = UnsavedChangesChoice.Discard };
        var viewModel = CreateSettings(store, new TestAppearance(), dialogs);
        await viewModel.LoadAsync();
        viewModel.RecentMaxCount = 7;

        var canLeave = await viewModel.TryLeaveAsync();

        Assert.True(canLeave);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(20, viewModel.RecentMaxCount);
        Assert.Equal(0, store.SaveCount);
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
    public async Task Resize_editor_uses_smaller_pixel_constraint_and_percentage_rules()
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
    public async Task Resize_preview_failure_keeps_valid_source_ready_for_formal_processing()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "resize-preview-failure.jpg"));
        var processor = new TestImageProcessor(path) { FailPreview = true };
        var viewModel = CreateResizeEditor(
            new TestPicker(DesktopSelectionResult.Canceled()),
            processor,
            new TestFileSystem([path]),
            new DesktopNavigationCoordinator());

        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));

        Assert.Equal(DesktopContentState.Ready, viewModel.ContentState);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.CanStart);
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
        Assert.False(navigation.IsNavigationLocked);
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
    public async Task Browser_previous_next_zoom_and_fit_commands_follow_selection_state()
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
        await browser.NextCommand.ExecuteAsync();
        Assert.Equal(second, browser.CurrentItem!.Path);
        Assert.True(browser.CanGoPrevious);
        Assert.False(browser.CanGoNext);

        await browser.ActualSizeCommand.ExecuteAsync();
        Assert.False(browser.IsFitMode);
        Assert.Equal(100, browser.ZoomPercent);
        browser.ZoomInCommand.Execute(null);
        Assert.Equal(125, browser.ZoomPercent);
        browser.FitCommand.Execute(null);
        Assert.True(browser.IsFitMode);
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
    public async Task Invalid_visible_output_policy_disables_single_editor_start()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "invalid-output.jpg"));
        var processor = new TestImageProcessor(path);
        var viewModel = CreateCompressionEditor(processor, new TestFileSystem([path]), new DesktopNavigationCoordinator());
        await viewModel.LoadAsync(new SingleImageNavigationContext(path, processor.Probe));
        viewModel.Output.FileNamePattern = string.Empty;

        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Contains("文件名格式", viewModel.DraftError);
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
    public async Task Browser_thumbnail_is_not_loaded_until_realized_item_requests_it()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "thumb-1.jpg"));
        var second = new LocalPath(Path.Combine(Path.GetTempPath(), "thumb-2.jpg"));
        var processor = new TestImageProcessor(first);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(first, "thumb-1.jpg"), new BrowserImageCandidate(second, "thumb-2.jpg")]));
        var secondItem = browser.Items[1];

        Assert.Null(secondItem.ThumbnailBytes);
        Assert.Equal(1, processor.PreviewCallCount);
        await secondItem.EnsureThumbnailCommand.ExecuteAsync();

        Assert.Equal(TestImageProcessor.PreviewPayload, secondItem.ThumbnailBytes);
        Assert.Equal(2, processor.PreviewCallCount);
    }

    [Fact]
    public async Task Browser_actual_size_requests_original_pixel_extent_only_on_demand()
    {
        var path = new LocalPath(Path.Combine(Path.GetTempPath(), "large-browser.jpg"));
        var processor = new TestImageProcessor(path, width: 4000, height: 3000);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(null, [new BrowserImageCandidate(path, "large-browser.jpg")]));

        Assert.Equal(1600, processor.LastPreviewRequest!.MaxPixelSize);
        await browser.ActualSizeCommand.ExecuteAsync();

        Assert.Equal(2, processor.PreviewCallCount);
        Assert.Equal(4000, processor.LastPreviewRequest!.MaxPixelSize);
        Assert.False(browser.IsFitMode);
        Assert.Equal(100, browser.ZoomPercent);
    }

    [Fact]
    public async Task Browser_rapid_selection_is_latest_wins_and_canceled_preview_cannot_overwrite_current()
    {
        var first = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-1.jpg"));
        var delayed = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-2.jpg"));
        var latest = new LocalPath(Path.Combine(Path.GetTempPath(), "latest-3.jpg"));
        var processor = new TestImageProcessor(first);
        processor.PreviewGates[delayed.Value] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = CreateBrowser(processor);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [
                new BrowserImageCandidate(first, "latest-1.jpg"),
                new BrowserImageCandidate(delayed, "latest-2.jpg"),
                new BrowserImageCandidate(latest, "latest-3.jpg")
            ]));

        browser.CurrentItem = browser.Items[1];
        await WaitUntilAsync(() => processor.PreviewCallCount >= 2);
        browser.CurrentItem = browser.Items[2];
        await WaitUntilAsync(() => browser.State == DesktopContentState.Ready && browser.CurrentItem?.Path == latest);

        processor.PreviewGates[delayed.Value].TrySetResult();
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
    public async Task Batch_invalid_visible_output_location_disables_start_without_calling_workflow()
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
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Contains("自定义输出目录", viewModel.DraftError);
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
        var viewModel = CreateSettings(store, new TestAppearance());
        await viewModel.LoadAsync();
        viewModel.RecentMaxCount = 7;

        await viewModel.SaveCommand.ExecuteAsync();

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.HasError);
        Assert.Contains("保存失败", viewModel.ErrorMessage);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Browser_reuses_probe_and_preview_cache_then_releases_the_whole_session_on_leave()
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
        Assert.Equal(2, processor.PreviewCallCount);
        browser.BackCommand.Execute(null);
        Assert.Empty(browser.Items);
        Assert.Null(browser.CurrentItem);
        Assert.Null(browser.PreviewBytes);
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
        new CreatePreviewWorkflow(processor),
        processor,
        new DesktopNavigationCoordinator(),
        new TestLauncher(),
        new TestClipboard());

    private static BatchTaskViewModel CreateBatch(
        IDesktopPickerService picker,
        IImageProcessor processor,
        IFileSystemService fileSystem,
        TestDialogs? dialogs = null,
        TestClipboard? clipboard = null)
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
            new DesktopNavigationCoordinator());
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
        DesktopNavigationCoordinator navigation)
    {
        var services = new ImageWorkflowServices(
            processor,
            fileSystem);
        var settings = new TestSettingsStore();
        return new ResizeEditorViewModel(
            picker,
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
            new LoadSettingsWorkflow(settings),
            new ResizeImageWorkflow(services),
            processor,
            navigation);
    }

    private static SettingsPageViewModel CreateSettings(
        IAppSettingsStore store,
        IDesktopAppearanceService appearance,
        TestDialogs? dialogs = null) =>
        new(
            new LoadSettingsWorkflow(store),
            new SaveSettingsWorkflow(store),
            dialogs ?? new TestDialogs(),
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            appearance,
            new TestPathProvider(),
            new TestClipboard());

    private static CompressionEditorViewModel CreateCompressionEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new CompressionEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
            new LoadSettingsWorkflow(new TestSettingsStore()),
            new CompressImageWorkflow(services),
            navigation);
    }

    private static ConversionEditorViewModel CreateConversionEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new ConversionEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
            new LoadSettingsWorkflow(new TestSettingsStore()),
            new ConvertImageWorkflow(services),
            navigation);
    }

    private static CropEditorViewModel CreateCropEditor(
        IImageProcessor processor,
        IFileSystemService fileSystem,
        DesktopNavigationCoordinator navigation)
    {
        var services = CreateImageWorkflowServices(processor, fileSystem);
        return new CropEditorViewModel(
            new TestPicker(DesktopSelectionResult.Canceled()),
            new TestLauncher(),
            new TestDialogs(),
            new TestClipboard(),
            new ResultOutputGuard(fileSystem),
            new OpenImageWorkflow(processor),
            new CreatePreviewWorkflow(processor),
            new LoadSettingsWorkflow(new TestSettingsStore()),
            new CropImageWorkflow(services),
            navigation);
    }

    private static ImageWorkflowServices CreateImageWorkflowServices(
        IImageProcessor processor,
        IFileSystemService fileSystem) =>
        new(processor, fileSystem);

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
        public UnsavedChangesChoice UnsavedChoice { get; init; } = UnsavedChangesChoice.Stay;
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

        public Task<UnsavedChangesChoice> ChooseUnsavedChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(UnsavedChoice);
    }

    private sealed class TestAppearance : IDesktopAppearanceService
    {
        public ThemeMode? LastApplied { get; private set; }
        public void Apply(ThemeMode themeMode) => LastApplied = themeMode;
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
