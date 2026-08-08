namespace AtomPix.Desktop.UiTests;

using AtomPix.Desktop.Controls;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.ViewModels;
using AtomPix.Desktop.Views;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

public sealed class DesktopUiAutomationTests
{
    [Fact]
    public Task Main_window_and_every_production_page_load_and_render() => RunAsync(() =>
    {
        Control[] controls =
        [
            new HomePageView(),
            new ImageBrowserView(),
            new CompressionEditorView(),
            new ConversionEditorView(),
            new ResizeEditorView(),
            new CropEditorView(),
            new BatchTaskView(),
            new SettingsPageView(),
            new OutputPolicyEditorView(),
            new DiagnosticIdView()
        ];

        foreach (var control in controls)
        {
            var window = Show(control);
            try
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.True(frame is not null, $"{control.GetType().Name} did not render a frame.");
                Assert.True(frame!.PixelSize.Width > 0);
                Assert.True(frame.PixelSize.Height > 0);
            }
            finally
            {
                window.Close();
            }
        }

        var mainWindow = new MainWindow();
        mainWindow.Show();
        using var mainFrame = mainWindow.CaptureRenderedFrame();
        Assert.NotNull(mainFrame);
        mainWindow.Close();
    });

    [Fact]
    public Task Output_policy_token_button_replaces_current_text_selection_and_restores_focus() => RunAsync(() =>
    {
        var viewModel = new OutputPolicyEditorViewModel(new CanceledPicker())
        {
            FileNamePattern = "photo-old-copy"
        };
        var view = new OutputPolicyEditorView { DataContext = viewModel };
        var window = Show(view);
        try
        {
        var textBox = view.FindControl<AtomUI.Desktop.Controls.TextBox>("FileNamePatternTextBox");
        Assert.NotNull(textBox);
        textBox!.SelectionStart = 6;
        textBox.SelectionEnd = 9;

        var insertName = view.GetVisualDescendants()
            .OfType<AtomUI.Desktop.Controls.Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "插入原文件名", StringComparison.Ordinal));
        insertName.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.Equal("photo-{name}-copy", viewModel.FileNamePattern);
        Assert.Equal(12, textBox.SelectionStart);
        Assert.Equal(textBox.SelectionStart, textBox.SelectionEnd);
            Assert.True(textBox.IsFocused);
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
    public Task Every_production_data_entry_surface_has_a_stable_automation_name() => RunAsync(() =>
    {
        Control[] views =
        [
            new HomePageView(),
            new ImageBrowserView(),
            new CompressionEditorView(),
            new ConversionEditorView(),
            new ResizeEditorView(),
            new CropEditorView(),
            new BatchTaskView(),
            new SettingsPageView(),
            new OutputPolicyEditorView()
        ];
        var auditedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ColorPicker",
            "ListView",
            "NumericUpDown",
            "ProgressBar",
            "Segmented",
            "Slider",
            "TextBox"
        };

        foreach (var view in views)
        {
            var unnamed = view.GetLogicalDescendants()
                .OfType<Control>()
                .Where(control => auditedTypes.Contains(control.GetType().Name))
                .Where(control => string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
                .Select(control => control.GetType().FullName ?? control.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                unnamed.Length == 0,
                $"{view.GetType().Name} contains unnamed interactive controls: {string.Join(", ", unnamed)}");
        }
    });

    [Fact]
    public Task Output_policy_index_button_is_disabled_after_index_token_exists() => RunAsync(() =>
    {
        var viewModel = new OutputPolicyEditorViewModel(new CanceledPicker())
        {
            FileNamePattern = "{name}_{index}"
        };
        var view = new OutputPolicyEditorView { DataContext = viewModel };
        var window = Show(view);
        try
        {
            var insertIndex = view.GetVisualDescendants()
                .OfType<AtomUI.Desktop.Controls.Button>()
                .Single(button => string.Equals(button.Content?.ToString(), "插入序号", StringComparison.Ordinal));

            Assert.False(insertIndex.IsEnabled);
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
            var realizedVisualCount = list.GetVisualDescendants().Count();
            Assert.InRange(realizedVisualCount, 1, 1_000);
        }
        finally
        {
            window.Close();
        }
    });

    private static async Task RunAsync(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(UiTestAppBuilder),
            AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(action, CancellationToken.None);
    }

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 1280,
            Height = 820,
            Content = content
        };
        window.Show();
        return window;
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
