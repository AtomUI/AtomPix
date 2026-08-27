namespace AtomPix.Desktop.UiTests;

using AtomPix.Core.Ports;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Composition;
using AtomPix.Desktop.Controls;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.ViewModels;
using AtomPix.Desktop.Views;
using AtomPix.Workflows.Settings;
using AtomPix.Workflows.Images;
using AtomUI.Labs.Controls.ImageGallery;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

public sealed class DesktopUiAutomationTests
{
    private static readonly Lazy<HeadlessUnitTestSession> SharedSession = new(() =>
        HeadlessUnitTestSession.StartNew(
            typeof(UiTestAppBuilder),
            AvaloniaTestIsolationLevel.PerAssembly));

    [Fact]
    public Task Shell_uses_a_non_immersive_primary_workspace_and_normal_right_tool_panel() => RunAsync(async () =>
    {
        using var services = DesktopCompositionRoot.Build();
        var shell = services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell);
        window.Show();
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(1180, window.Width);
            Assert.Equal(760, window.Height);
            Assert.Equal(960, window.MinWidth);
            Assert.Equal(640, window.MinHeight);

            var root = Assert.IsType<Grid>(window.FindControl<Grid>("AppLayoutRoot"));
            var workspace = Assert.IsType<ContentControl>(window.FindControl<ContentControl>("PrimaryWorkspace"));
            var panel = Assert.IsType<Border>(window.FindControl<Border>("ToolPanel"));
            var rail = Assert.IsType<Border>(window.FindControl<Border>("NavigationRail"));
            Assert.Equal(Color.Parse("#F5F7FA"), Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color);
            Assert.True(workspace.Bounds.Width > 0);
            Assert.Equal(380, panel.Width);
            Assert.False(panel.IsVisible);
            Assert.Equal(new Thickness(1, 0, 0, 0), panel.BorderThickness);
            Assert.Equal(54, rail.Width);
            Assert.Equal(new CornerRadius(0, 12, 12, 0), rail.CornerRadius);
            Assert.True(rail.BoxShadow.Count > 0);
            Assert.Null(window.FindControl<AtomUI.Desktop.Controls.Drawer>("ToolDrawer"));
            Assert.Null(window.FindControl<Border>("ImmersiveTitleBarScrim"));
            Assert.Null(window.FindControl<Control>("BrowserBackdropViewport"));

            var settingsButton = rail.GetLogicalDescendants()
                .OfType<AtomUI.Desktop.Controls.Button>()
                .Single(button => AutomationProperties.GetName(button) == "设置");
            Assert.Equal(DesktopRoute.Settings, Assert.IsType<DesktopRoute>(settingsButton.CommandParameter));
            Assert.NotNull(settingsButton.Command);
            Assert.True(settingsButton.Command.CanExecute(settingsButton.CommandParameter));
            await shell.Settings.LoadAsync();
            var clickPoint = settingsButton.TranslatePoint(
                new Point(settingsButton.Bounds.Width / 2, settingsButton.Bounds.Height / 2),
                window);
            Assert.True(clickPoint.HasValue);
            window.MouseMove(clickPoint.Value, RawInputModifiers.None);
            window.MouseDown(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsSettingsOpen);
            Assert.Same(shell.Settings, shell.CurrentPage);
            Assert.False(rail.IsVisible);
            Assert.False(panel.IsVisible);
            var settingsSeparator = Assert.IsType<Border>(window.FindControl<Border>("SettingsTitleBarSeparator"));
            Assert.True(settingsSeparator.IsVisible);
            Assert.Equal(1, settingsSeparator.Height);
            Assert.False(settingsSeparator.IsHitTestVisible);
            Assert.Null(window.FindControl<AtomUI.Desktop.Controls.Dialog>("SettingsDialog"));
            var settingsView = Assert.Single(workspace.GetVisualDescendants().OfType<SettingsPageView>());
            Assert.Same(shell.Settings, settingsView.DataContext);
            Assert.True(Assert.IsType<StackPanel>(settingsView.FindControl<StackPanel>("CompressionSettingsSection")).IsVisible);
            Assert.True(Assert.IsType<StackPanel>(settingsView.FindControl<StackPanel>("ConversionSettingsSection")).IsVisible);
            Assert.True(Assert.IsType<StackPanel>(settingsView.FindControl<StackPanel>("OutputSettingsSection")).IsVisible);
            Assert.True(Assert.IsType<StackPanel>(settingsView.FindControl<StackPanel>("AboutSettingsSection")).IsVisible);

            shell.Settings.CloseCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IsSettingsOpen);
            Assert.Same(shell.Home, shell.CurrentPage);
            Assert.True(rail.IsVisible);
            Assert.False(settingsSeparator.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Desktop_message_text_ignores_host_data_context_reparenting() => RunAsync(() =>
    {
        var view = new DesktopMessageTextView { DataContext = "面向用户的诊断文案" };
        Assert.Equal("面向用户的诊断文案", view.Text);

        view.DataContext = new object();

        Assert.Equal("面向用户的诊断文案", view.Text);
    });

    [Fact]
    public Task Navigation_rail_preserves_the_six_icon_only_actions_without_hover_chrome() => RunAsync(() =>
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            using var frame = window.CaptureRenderedFrame();
            var rail = Assert.IsType<Border>(window.FindControl<Border>("NavigationRail"));
            var buttons = rail.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Button>().ToArray();
            Assert.Equal(
                new[] { "返回首页", "压缩体积", "转换格式", "调整尺寸", "剪裁尺寸", "设置" },
                buttons.Select(AutomationProperties.GetName).ToArray());
            Assert.All(buttons, button =>
            {
                Assert.Equal(new Thickness(0), button.BorderThickness);
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
            });
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Image_browser_hosts_the_official_labs_gallery_and_crop_resource_surface() => RunAsync(() =>
    {
        var view = new ImageBrowserView();
        var window = Show(view);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            var layout = Assert.IsType<Grid>(view.FindControl<Grid>("BrowserContentLayout"));
            var gallery = Assert.IsType<ImageGallery>(view.FindControl<ImageGallery>("ImageGalleryViewer"));
            var crop = Assert.IsType<CropCanvas>(view.FindControl<CropCanvas>("BrowserCropCanvas"));
            Assert.Equal(layout.Bounds.Width, gallery.Bounds.Width);
            Assert.Equal(layout.Bounds.Height, gallery.Bounds.Height);
            Assert.Equal(ImageGalleryZoomMode.Fit, gallery.ZoomMode);
            Assert.Equal(1.2, gallery.ZoomStep);
            Assert.False(gallery.IsFitUpscalingEnabled);
            Assert.False(gallery.IsLoopNavigationEnabled);
            Assert.False(gallery.IsViewportNavigationEnabled);
            Assert.False(gallery.IsToolbarTitleVisible);
            Assert.Equal(68, gallery.ThumbnailFilmstripExtent);
            Assert.Equal(new CornerRadius(0), gallery.ThumbnailItemAppearance?.CornerRadius);
            Assert.NotNull(crop);
            Assert.Null(view.FindControl<Control>("LegacyImageGalleryViewer"));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Crop_workspace_uses_the_light_surface_and_reflows_inside_overlay_safe_area() => RunAsync(() =>
    {
        using var services = DesktopCompositionRoot.Build();
        var browser = services.GetRequiredService<ImageBrowserViewModel>();
        browser.SetCropMode(true);
        var view = new ImageBrowserView { DataContext = browser };
        var window = Show(view);
        try
        {
            using var initialFrame = window.CaptureRenderedFrame();
            var layout = Assert.IsType<Grid>(view.FindControl<Grid>("BrowserContentLayout"));
            var gallery = Assert.IsType<ImageGallery>(view.FindControl<ImageGallery>("ImageGalleryViewer"));
            var crop = Assert.IsType<CropCanvas>(view.FindControl<CropCanvas>("BrowserCropCanvas"));

            Assert.True(crop.IsVisible);
            Assert.Equal(72, crop.Bounds.X);
            Assert.Equal(18, crop.Bounds.Y);
            Assert.Equal(18, layout.Bounds.Width - crop.Bounds.Right);
            Assert.Equal(94, layout.Bounds.Height - crop.Bounds.Bottom);
            Assert.Equal(Color.Parse("#F5F7FA"), Assert.IsAssignableFrom<ISolidColorBrush>(crop.Background).Color);
            Assert.Equal(Color.Parse("#D7DDE6"), Assert.IsAssignableFrom<ISolidColorBrush>(crop.ImageBorderBrush).Color);
            Assert.Equal(layout.Bounds.Size, gallery.Bounds.Size);

            var originalWidth = crop.Bounds.Width;
            var originalHeight = crop.Bounds.Height;
            window.Width = 1040;
            window.Height = 700;
            using var resizedFrame = window.CaptureRenderedFrame();

            Assert.True(crop.Bounds.Width < originalWidth);
            Assert.True(crop.Bounds.Height < originalHeight);
            Assert.Equal(72, crop.Bounds.X);
            Assert.Equal(18, crop.Bounds.Y);
            Assert.Equal(18, layout.Bounds.Width - crop.Bounds.Right);
            Assert.Equal(94, layout.Bounds.Height - crop.Bounds.Bottom);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Entering_crop_restores_the_browser_current_item_without_a_thumbnail_click() => RunAsync(async () =>
    {
        using var services = DesktopCompositionRoot.Build();
        var browser = services.GetRequiredService<ImageBrowserViewModel>();
        var cropEditor = services.GetRequiredService<CropEditorViewModel>();
        var imagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "jpeg-detailed.jpg");
        var localPath = new LocalPath(imagePath);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(localPath, Path.GetFileName(imagePath))]));

        var view = new ImageBrowserView { DataContext = browser };
        var window = Show(view);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            var gallery = Assert.IsType<ImageGallery>(view.FindControl<ImageGallery>("ImageGalleryViewer"));
            var expected = Assert.IsType<ImageGalleryItemAdapter>(browser.SelectedGalleryItem);

            gallery.SelectedItem = null;
            Assert.Null(gallery.SelectedItem);
            Assert.Same(expected, browser.SelectedGalleryItem);

            browser.SetCropMode(true, cropEditor);

            Assert.Same(expected, gallery.SelectedItem);
            Assert.Equal(ImageGalleryMainImageMode.ResourceOnly, gallery.MainImageMode);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Entering_crop_reuses_the_ready_browser_image_without_a_thumbnail_click() => RunAsync(async () =>
    {
        using var services = DesktopCompositionRoot.Build();
        var browser = services.GetRequiredService<ImageBrowserViewModel>();
        var cropEditor = services.GetRequiredService<CropEditorViewModel>();
        var imagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "jpeg-detailed.jpg");
        var localPath = new LocalPath(imagePath);
        await browser.LoadAsync(new BrowserNavigationContext(
            null,
            [new BrowserImageCandidate(localPath, Path.GetFileName(imagePath))]));
        var view = new ImageBrowserView { DataContext = browser };
        var window = Show(view);
        try
        {
            using var browserFrame = window.CaptureRenderedFrame();
            var gallery = Assert.IsType<ImageGallery>(view.FindControl<ImageGallery>("ImageGalleryViewer"));
            var canvas = Assert.IsType<CropCanvas>(view.FindControl<CropCanvas>("BrowserCropCanvas"));
            var expected = Assert.IsType<ImageGalleryItemAdapter>(browser.SelectedGalleryItem);
            await WaitForCurrentGalleryImageAsync(gallery, expected);
            Assert.True(browser.TryCreateCurrentContext(out var context));

            var loadTask = cropEditor.LoadAsync(context!);
            browser.SetCropMode(true, cropEditor);
            await loadTask;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var cropFrame = window.CaptureRenderedFrame();

            Assert.Same(expected, gallery.SelectedItem);
            Assert.NotNull(canvas.ImageSource);

            window.Width = 900;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var narrowedCropFrame = window.CaptureRenderedFrame();
            Assert.NotNull(canvas.ImageSource);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Folder_browser_enters_crop_through_the_shell_with_its_first_image_visible() => RunAsync(async () =>
    {
        using var services = DesktopCompositionRoot.Build();
        var shell = services.GetRequiredService<ShellViewModel>();
        var navigation = services.GetRequiredService<DesktopNavigationCoordinator>();
        var firstPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "jpeg-detailed.jpg");
        var secondPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "jpeg-basic.jpg");
        var context = new BrowserNavigationContext(
            new LocalPath(Path.GetDirectoryName(firstPath)!),
            [
                new BrowserImageCandidate(new LocalPath(firstPath), Path.GetFileName(firstPath)),
                new BrowserImageCandidate(new LocalPath(secondPath), Path.GetFileName(secondPath))
            ]);
        var window = new MainWindow(shell);
        window.Show();
        try
        {
            Assert.True(navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Browse, context)));
            await WaitUntilAsync(() => shell.Browser.State == DesktopContentState.Ready && shell.IsBrowserVisible);
            using var browserFrame = window.CaptureRenderedFrame();

            var browserView = Assert.Single(window.GetVisualDescendants().OfType<ImageBrowserView>());
            var gallery = Assert.IsType<ImageGallery>(browserView.FindControl<ImageGallery>("ImageGalleryViewer"));
            var expected = Assert.IsType<ImageGalleryItemAdapter>(shell.Browser.SelectedGalleryItem);
            Assert.Same(shell.Browser.Items[0].GalleryItem, expected);

            shell.NavigateCommand.Execute(DesktopRoute.Crop);
            await WaitUntilAsync(() => shell.IsCropActive && shell.Crop.ContentState == DesktopContentState.Ready);
            using var cropFrame = window.CaptureRenderedFrame();
            var cropCanvas = Assert.IsType<CropCanvas>(browserView.FindControl<CropCanvas>("BrowserCropCanvas"));
            try
            {
                await WaitUntilAsync(() => cropCanvas.ImageSource is not null);
            }
            catch (TimeoutException)
            {
                var canAcquire = gallery.TryAcquireCurrentImage(expected, out var diagnosticLease);
                diagnosticLease?.Dispose();
                Assert.Fail(
                    $"Crop image stayed empty. GalleryState={gallery.ImageState}; " +
                    $"CanAcquire={canAcquire}; " +
                    $"SelectedIndex={gallery.SelectedIndex}; SelectedMatches={ReferenceEquals(expected, gallery.SelectedItem)}; " +
                    $"MainMode={gallery.MainImageMode}; CropVisible={cropCanvas.IsVisible}; " +
                    $"CropBounds={cropCanvas.Bounds}; Input={shell.Crop.InputPath}; " +
                    $"Pixels={shell.Crop.ImagePixelWidth}x{shell.Crop.ImagePixelHeight}.");
            }

            Assert.Same(expected, gallery.SelectedItem);
            Assert.NotNull(cropCanvas.ImageSource);

            gallery.SelectedIndex = 1;
            await WaitUntilAsync(() => ReferenceEquals(shell.Browser.CurrentItem, shell.Browser.Items[1]));
            await WaitUntilAsync(() => shell.Browser.State == DesktopContentState.Ready);
            await WaitUntilAsync(() => string.Equals(
                Path.GetFullPath(shell.Crop.InputPath),
                Path.GetFullPath(secondPath),
                StringComparison.OrdinalIgnoreCase));
            using var secondCropFrame = window.CaptureRenderedFrame();
            await WaitUntilAsync(() => cropCanvas.ImageSource is not null);
            Assert.Same(shell.Browser.Items[1].GalleryItem, gallery.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Processing_editors_use_cardless_inspectors_with_consistent_primary_actions() => RunAsync(() =>
    {
        (Control Editor, int ExpectedActions)[] editors =
        [
            (new CompressionEditorView(), 0),
            (new ConversionEditorView(), 0),
            (new ResizeEditorView(), 0),
            (new CropEditorView(), 2),
            (new ToolDrawerSessionView(), 3)
        ];

        foreach (var (editor, expectedActions) in editors)
        {
            var window = Show(editor);
            try
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Empty(editor.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Card>());
                if (editor is not ToolDrawerSessionView)
                {
                    Assert.Empty(editor.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.MessageCard>());
                    Assert.DoesNotContain(
                        editor.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Button>(),
                        button => string.Equals(button.Content as string, "打开输出目录", StringComparison.Ordinal));
                }
                else
                {
                    Assert.DoesNotContain(
                        editor.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Button>(),
                        button => string.Equals(button.Content as string, "返回单张配置", StringComparison.Ordinal));
                    var progress = Assert.IsType<AtomUI.Desktop.Controls.ProgressBar>(
                        editor.FindControl<Control>("BatchProgressBar"));
                    Assert.False(progress.IsProgressInfoVisible);
                }

                Assert.All(
                    editor.GetLogicalDescendants()
                        .OfType<AtomUI.Desktop.Controls.Slider>()
                        .Where(slider => slider.Classes.Contains("AtomPixInspectorAlignedSlider")),
                    slider => Assert.Equal(new Thickness(-16, 0, 0, 0), slider.Margin));

                var primaryActions = editor.GetLogicalDescendants()
                    .OfType<AtomUI.Desktop.Controls.Button>()
                    .Where(button => button.Classes.Contains("AtomPixInspectorPrimaryAction"))
                    .ToArray();
                Assert.Equal(expectedActions, primaryActions.Length);
                Assert.All(primaryActions, button =>
                {
                    Assert.Equal(44, button.Height);
                    Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, button.HorizontalAlignment);
                });
            }
            finally
            {
                window.Close();
            }
        }
    });

    [Fact]
    public Task Resize_editor_uses_atomui_integer_pixel_guards_and_a_single_percentage_editor() => RunAsync(() =>
    {
        using var services = DesktopCompositionRoot.Build();
        var viewModel = services.GetRequiredService<ResizeEditorViewModel>();
        var view = new ResizeEditorView { DataContext = viewModel };
        var window = Show(view);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(view.FindControl<StackPanel>("PixelResizeOptions")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("PercentageResizeOptions")!.IsVisible);
            var widthInput = Assert.IsType<AtomUI.Desktop.Controls.NumericUpDown>(view.FindControl<Control>("PixelWidthInput"));
            var heightInput = Assert.IsType<AtomUI.Desktop.Controls.NumericUpDown>(view.FindControl<Control>("PixelHeightInput"));
            Assert.Equal("0", widthInput.FormatString);
            Assert.Equal("0", heightInput.FormatString);
            var aspect = Assert.IsType<AtomUI.Desktop.Controls.CheckBox>(view.FindControl<Control>("MaintainAspectRatioCheckBox"));
            var prevent = Assert.IsType<AtomUI.Desktop.Controls.CheckBox>(view.FindControl<Control>("PreventUpscalingCheckBox"));
            Assert.Equal("保持宽高比", aspect.Content);
            Assert.Equal("小于目标尺寸时不放大", prevent.Content);

            viewModel.SelectedMode = viewModel.ResizeModes.Single(option => option.Value == ResizeDraftMode.Percentage);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(view.FindControl<StackPanel>("PixelResizeOptions")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("PercentageResizeOptions")!.IsVisible);
            Assert.Null(view.FindControl<AtomUI.Desktop.Controls.Button>("Use25PercentButton"));
            Assert.Null(view.FindControl<AtomUI.Desktop.Controls.Button>("Use50PercentButton"));
            Assert.Null(view.FindControl<AtomUI.Desktop.Controls.Button>("Use75PercentButton"));
            Assert.Equal(92, Assert.IsType<AtomUI.Desktop.Controls.NumericUpDown>(view.FindControl<Control>("PercentageInput")).Bounds.Width, 3);
            Assert.Equal(1, Assert.IsType<AtomUI.Desktop.Controls.Slider>(view.FindControl<Control>("PercentageSlider")).Minimum);
            Assert.Equal(1000, Assert.IsType<AtomUI.Desktop.Controls.Slider>(view.FindControl<Control>("PercentageSlider")).Maximum);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Crop_editor_shows_single_column_integer_geometry_only_for_custom_ratio() => RunAsync(() =>
    {
        using var services = DesktopCompositionRoot.Build();
        var viewModel = services.GetRequiredService<CropEditorViewModel>();
        var view = new CropEditorView { DataContext = viewModel };
        var window = Show(view);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(["自定义", "3:2", "4:3", "5:4", "1:1"], viewModel.Ratios.Select(option => option.Label));
            Assert.True(view.FindControl<StackPanel>("CustomCropOptions")!.IsVisible);

            var inputs = new[] { "CropWidthInput", "CropHeightInput", "CropXInput", "CropYInput" }
                .Select(name => Assert.IsType<AtomUI.Desktop.Controls.NumericUpDown>(view.FindControl<Control>(name)))
                .ToArray();
            Assert.All(inputs, input =>
            {
                Assert.Equal("0", input.FormatString);
                Assert.Equal(112, input.Bounds.Width, 3);
            });
            Assert.DoesNotContain(
                view.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Button>(),
                button => string.Equals(button.Content as string, "重置为完整图片区域", StringComparison.Ordinal));
            Assert.Contains(
                view.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Button>(),
                button => string.Equals(button.Content as string, "开始剪裁", StringComparison.Ordinal));

            viewModel.SelectedRatio = viewModel.Ratios.Single(option => option.Label == "3:2");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<StackPanel>("CustomCropOptions")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Window_feedback_uses_official_atomui_message_and_notification_managers() => RunAsync(() =>
    {
        using var services = DesktopCompositionRoot.Build();
        var feedback = services.GetRequiredService<AvaloniaDesktopFeedbackService>();
        var window = new MainWindow(services.GetRequiredService<ShellViewModel>(), feedback);
        window.Show();
        try
        {
            feedback.ShowMessage("单张压缩完成", DesktopFeedbackSeverity.Success);
            feedback.ShowNotification(new DesktopNotificationRequest(
                "批量压缩完成",
                "成功 20 · 失败 0",
                DesktopFeedbackSeverity.Success,
                TimeSpan.FromSeconds(6)));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.Contains(
                window.GetVisualDescendants().OfType<AtomUI.Desktop.Controls.MessageCard>(),
                card => card.Message == "单张压缩完成");
            Assert.Contains(
                window.GetVisualDescendants().OfType<AtomUI.Desktop.Controls.NotificationCard>(),
                card => card.Title == "批量压缩完成");
            var resultDialog = Assert.IsType<AtomUI.Desktop.Controls.Dialog>(
                window.FindControl<AtomUI.Desktop.Controls.Dialog>("BatchResultDialog"));
            Assert.Equal("Center", resultDialog.HorizontalStartupLocation.ToString());
            Assert.Equal("Center", resultDialog.VerticalStartupLocation.ToString());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Output_directory_uses_atomui_inputs_and_aligns_custom_input_with_picker_button() => RunAsync(() =>
    {
        var viewModel = new OutputPolicyEditorViewModel(new CanceledPicker());
        viewModel.SelectedLocation = viewModel.Locations.Single(
            option => option.Value == AtomPix.Core.Output.OutputLocationMode.CustomDirectory);
        viewModel.CustomDirectory = @"D:\Pictures\AtomPix";
        var view = new OutputPolicyEditorView { DataContext = viewModel };
        var window = Show(view);
        try
        {
            var subfolderInput = Assert.IsType<AtomUI.Desktop.Controls.TextBox>(view.FindControl<Control>("SubfolderNameInput"));
            var input = Assert.IsType<AtomUI.Desktop.Controls.TextBox>(view.FindControl<Control>("CustomDirectoryInput"));
            var button = Assert.IsType<AtomUI.Desktop.Controls.Button>(view.FindControl<Control>("ChooseDirectoryButton"));
            var selector = Assert.IsType<AtomUI.Desktop.Controls.Segmented>(view.FindControl<Control>("OutputLocationSelector"));
            var namingSelector = Assert.IsType<AtomUI.Desktop.Controls.Segmented>(view.FindControl<Control>("OutputNamingSelector"));
            var suffixInput = Assert.IsType<AtomUI.Desktop.Controls.TextBox>(view.FindControl<Control>("FileNameSuffixInput"));
            var directorySurface = Assert.IsType<Border>(view.FindControl<Control>("CustomDirectoryContextSurface"));
            var namingSurface = Assert.IsType<Border>(view.FindControl<Control>("AppendSuffixContextSurface"));
            Assert.True(input.IsVisible);
            Assert.True(button.IsVisible);
            Assert.Equal(new Thickness(1), subfolderInput.BorderThickness);
            Assert.Equal(new Thickness(1), input.BorderThickness);
            Assert.NotNull(input.BorderBrush);
            Assert.Equal(input.Bounds.Height, button.Bounds.Height, 3);
            Assert.Equal(selector.Bounds.Height, input.Bounds.Height, 3);
            Assert.Equal(input.Bounds.Y, button.Bounds.Y, 3);
            Assert.Equal(selector.Background, input.Background);
            Assert.Equal(selector.Background, directorySurface.Background);
            Assert.Equal(namingSelector.Background, namingSurface.Background);
            Assert.Equal(new Thickness(12, 0), input.Padding);
            Assert.True(suffixInput.IsVisible);
            Assert.Equal(new Thickness(1), suffixInput.BorderThickness);
            Assert.Equal(namingSelector.Bounds.Height, suffixInput.Bounds.Height, 3);
            Assert.Equal(namingSelector.Background, suffixInput.Background);
            Assert.Equal(new Thickness(12, 0), suffixInput.Padding);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Labs_gallery_owns_selection_zoom_and_virtualized_filmstrip_layout() => RunAsync(() =>
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "png-alpha.png");
        var items = Enumerable.Range(0, 2_000)
            .Select(index => new ImageGalleryItem
            {
                Key = $"image-{index}",
                Title = $"image-{index}.png",
                MainImageSource = ImageGallerySources.FromFile(imagePath, $"source-{index}")
            })
            .ToArray();
        var gallery = new ImageGallery
        {
            Width = 900,
            Height = 620,
            ItemsSource = items,
            SelectedItem = items[1_000],
            ZoomMode = ImageGalleryZoomMode.Fit,
            IsFitUpscalingEnabled = false,
            ThumbnailFilmstripExtent = 82,
            ThumbnailItemExtent = 92,
            ThumbnailItemSpacing = 4
        };
        var window = Show(gallery);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(1_000, gallery.SelectedIndex);
            Assert.Same(items[1_000], gallery.SelectedItem);
            Assert.Equal(ImageGalleryZoomMode.Fit, gallery.ZoomMode);
            Assert.InRange(gallery.GetVisualDescendants().Count(), 1, 1_000);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Crop_canvas_receives_real_keyboard_input_with_one_and_ten_pixel_steps() => RunAsync(() =>
    {
        var canvas = new CropCanvas
        {
            Width = 480,
            Height = 320,
            PreviewBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "png-alpha.png")),
            ImagePixelWidth = 100,
            ImagePixelHeight = 80,
            CropX = 10,
            CropY = 10,
            CropWidth = 40,
            CropHeight = 30
        };
        var window = Show(canvas);
        try
        {
            canvas.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(11, canvas.CropX);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Assert.Equal(20, canvas.CropY);
            Assert.Equal("裁剪选区画布", AutomationProperties.GetName(canvas));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(canvas)));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Settings_exposes_only_defaults_and_about_without_privacy_or_license_entries() => RunAsync(async () =>
    {
        using var services = DesktopCompositionRoot.Build();
        using var settings = new SettingsPageViewModel(
            services.GetRequiredService<LoadSettingsWorkflow>(),
            services.GetRequiredService<SaveSettingsWorkflow>(),
            services.GetRequiredService<IDesktopDialogService>(),
            services.GetRequiredService<IDesktopPickerService>(),
            services.GetRequiredService<IDesktopLauncherService>(),
            services.GetRequiredService<IAppPathProvider>(),
            services.GetRequiredService<IDesktopClipboardService>());
        var view = new SettingsPageView { DataContext = settings };
        var window = new AtomUI.Desktop.Controls.Window
        {
            Width = 1280,
            Height = 820,
            Content = view
        };
        window.Show();
        try
        {
            await settings.LoadAsync();
            using var frame = window.CaptureRenderedFrame();
            Assert.Same(settings, view.DataContext);
            Assert.True(view.FindControl<Grid>("SettingsReadyContent")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("CompressionSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("ConversionSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("OutputSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("AboutSettingsSection")!.IsVisible);
            var labels = view.GetLogicalDescendants()
                .OfType<AtomUI.Desktop.Controls.Button>()
                .Select(button => button.Content?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            Assert.Contains("压缩配置", labels);
            Assert.Contains("转换配置", labels);
            Assert.Contains("输出配置", labels);
            Assert.Contains("关于 AtomPix", labels);
            Assert.Contains("恢复默认", labels);
            Assert.Contains("返回", labels);
            Assert.Contains("保存设置", labels);
            Assert.DoesNotContain("查看隐私说明", labels);
            Assert.DoesNotContain("查看开源许可证", labels);
            Assert.Empty(view.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.Card>());
            Assert.Single(view.GetLogicalDescendants().OfType<RgbColorPickerBridge>());
            Assert.DoesNotContain(
                view.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.TextBox>(),
                textBox => AutomationProperties.GetName(textBox) == "默认透明区域背景色");
            Assert.True(view.FindControl<Border>("SettingsSubfolderContextSurface")!.IsVisible);
            Assert.False(view.FindControl<Border>("SettingsSameAsInputContextSurface")!.IsVisible);
            Assert.False(view.FindControl<Border>("SettingsCustomDirectoryContextSurface")!.IsVisible);
            var sectionButtons = view.GetLogicalDescendants()
                .OfType<AtomUI.Desktop.Controls.Button>()
                .Where(button => AutomationProperties.GetName(button) is "压缩配置" or "转换配置" or "输出配置" or "关于 AtomPix")
                .ToArray();
            Assert.Equal(4, sectionButtons.Length);
            Assert.All(sectionButtons.Skip(1), button =>
            {
                Assert.Equal(sectionButtons[0].Bounds.X, button.Bounds.X, 3);
                Assert.Equal(sectionButtons[0].Bounds.Width, button.Bounds.Width, 3);
            });

            var settingsScrollViewer = view.FindControl<AtomUI.Desktop.Controls.ScrollViewer>("SettingsScrollViewer")!;
            settings.SelectSectionCommand.Execute(SettingsSection.About);
            await WaitUntilAsync(() =>
                settings.SelectedSection == SettingsSection.About
                && settingsScrollViewer.Offset.Y > 0);
            Assert.True(view.FindControl<StackPanel>("CompressionSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("ConversionSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("OutputSettingsSection")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("AboutSettingsSection")!.IsVisible);
            Assert.True(settingsScrollViewer.Offset.Y > 0);
            var restoreButton = view.GetLogicalDescendants()
                .OfType<AtomUI.Desktop.Controls.Button>()
                .Single(button => AutomationProperties.GetName(button) == "恢复默认");
            Assert.False(restoreButton.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Category", "Stress")]
    public Task AtomUI_list_view_virtualizes_ten_thousand_rows_in_a_bounded_viewport() => RunAsync(() =>
    {
        var list = new AtomUI.Desktop.Controls.ListView
        {
            Width = 800,
            Height = 500,
            ItemsSource = Enumerable.Range(1, 10_000)
                .Select(index => new AtomUI.Controls.Data.ListItemData { Content = $"row-{index:D5}" })
                .ToArray()
        };
        var window = Show(list);
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.InRange(list.GetVisualDescendants().Count(), 1, 1_000);
        }
        finally
        {
            window.Close();
        }
    });

    private static async Task RunAsync(Action action)
    {
        await SharedSession.Value.Dispatch(action, CancellationToken.None);
    }

    private static async Task RunAsync(Func<Task> action)
    {
        await SharedSession.Value.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 1280, Height = 820, Content = content };
        window.Show();
        return window;
    }

    private static async Task WaitForCurrentGalleryImageAsync(
        ImageGallery gallery,
        ImageGalleryItemAdapter expected)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void TryComplete()
        {
            if (!gallery.TryAcquireCurrentImage(expected, out var lease)) return;
            lease!.Dispose();
            ready.TrySetResult();
        }

        void HandleResourceChanged(object? sender, EventArgs args) => TryComplete();
        gallery.CurrentImageResourceChanged += HandleResourceChanged;
        try
        {
            TryComplete();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gallery.CurrentImageResourceChanged -= HandleResourceChanged;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("The expected desktop UI state was not reached.");
            }

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private sealed class CanceledPicker : IDesktopPickerService
    {
        public Task<DesktopSelectionResult> PickSingleImageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DesktopSelectionResult.Canceled());
        public Task<DesktopSelectionResult> PickImagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DesktopSelectionResult.Canceled());
        public Task<DesktopSelectionResult> PickFolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DesktopSelectionResult.Canceled());
    }

}
