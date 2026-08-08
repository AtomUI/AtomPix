namespace AtomPix.Infrastructure.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Infrastructure.Configuration;
using AtomPix.Infrastructure.FileSystem;
using AtomPix.Infrastructure.Paths;
using AtomPix.Infrastructure.RecentItems;

public sealed class InfrastructureContractTests : IDisposable
{
    private readonly string _root;
    private readonly IAppPathProvider _paths;

    public InfrastructureContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AtomPixTests", Guid.NewGuid().ToString("N"));
        _paths = new AppPathProvider(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
    }

    [Fact]
    public async Task Settings_missing_file_returns_defaults()
    {
        var store = new JsonAppSettingsStore(_paths);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AppSettings.Default, result.Value);
    }

    [Fact]
    public async Task Settings_corrupt_file_returns_failure()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        await File.WriteAllTextAsync(Path.Combine(_paths.AppDataDirectory.Value, "settings.json"), "{bad json");
        var store = new JsonAppSettingsStore(_paths);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, result.Error!.Code);
    }


    [Fact]
    public async Task Settings_higher_schema_version_returns_failure()
    {
        var store = new JsonAppSettingsStore(_paths);
        var save = await store.SaveAsync(AppSettings.Default, CancellationToken.None);
        var settingsPath = Path.Combine(_paths.AppDataDirectory.Value, "settings.json");
        var json = await File.ReadAllTextAsync(settingsPath);
        await File.WriteAllTextAsync(settingsPath, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999"));

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsLoadFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Settings_v1_missing_new_same_format_policy_uses_compatible_default()
    {
        var store = new JsonAppSettingsStore(_paths);
        Assert.True((await store.SaveAsync(AppSettings.Default, CancellationToken.None)).Succeeded);
        var settingsPath = SettingsPath();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.True(json.Remove("defaultSameFormatEncodingPolicy"));
        json.Remove("schemaVersion");
        await File.WriteAllTextAsync(settingsPath, json.ToJsonString());

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AppSettings.CurrentSchemaVersion, result.Value!.SchemaVersion);
        Assert.Equal(SameFormatEncodingPolicy.Default, result.Value.DefaultSameFormatEncodingPolicy);
    }

    [Fact]
    public async Task Settings_corrupt_file_is_not_overwritten_by_load_failure()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        var settingsPath = Path.Combine(_paths.AppDataDirectory.Value, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{bad json");
        var store = new JsonAppSettingsStore(_paths);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("{bad json", await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task Settings_save_failure_cleans_temporary_file()
    {
        Directory.CreateDirectory(Path.Combine(_paths.AppDataDirectory.Value, "settings.json"));
        var store = new JsonAppSettingsStore(_paths);

        var result = await store.SaveAsync(AppSettings.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsSaveFailed, result.Error!.Code);
        Assert.Empty(Directory.EnumerateFiles(_paths.AppDataDirectory.Value, ".*.tmp"));
    }

    [Fact]
    public async Task Recent_items_corrupt_file_can_be_recovered_by_save()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        await File.WriteAllTextAsync(Path.Combine(_paths.AppDataDirectory.Value, "recent-items.json"), "{bad json");
        var store = new JsonRecentItemsStore(_paths);
        var item = new RecentItem(new LocalPath("C:\\tmp\\recovered.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow);

        var load = await store.LoadAsync(CancellationToken.None);
        var save = await store.SaveAsync([item], CancellationToken.None);
        var recovered = await store.LoadAsync(CancellationToken.None);

        Assert.True(load.Succeeded);
        Assert.Empty(load.Value!);
        Assert.True(save.Succeeded);
        Assert.True(recovered.Succeeded);
        Assert.Single(recovered.Value!);
        Assert.Equal(item.Path, recovered.Value![0].Path);
    }
    [Fact]
    public async Task Settings_save_then_load_roundtrips()
    {
        var store = new JsonAppSettingsStore(_paths);

        var save = await store.SaveAsync(AppSettings.Default, CancellationToken.None);
        var load = await store.LoadAsync(CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(load.Succeeded);
        Assert.Equal(AppSettings.Default.ThemeMode, load.Value!.ThemeMode);
        Assert.Equal(AppSettings.CurrentSchemaVersion, load.Value.SchemaVersion);
    }

    [Fact]
    public async Task Recent_items_corrupt_file_returns_empty_success()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        await File.WriteAllTextAsync(Path.Combine(_paths.AppDataDirectory.Value, "recent-items.json"), "{bad json");
        var store = new JsonRecentItemsStore(_paths);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Recent_items_save_then_load_roundtrips()
    {
        var store = new JsonRecentItemsStore(_paths);
        var items = new[] { new RecentItem(new LocalPath("C:\\tmp\\a.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow) };

        var save = await store.SaveAsync(items, CancellationToken.None);
        var load = await store.LoadAsync(CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(load.Succeeded);
        Assert.Single(load.Value!);
    }

    [Fact]
    public async Task File_system_creates_directory_and_gets_file_size()
    {
        var fs = new LocalFileSystemService();
        var directory = new LocalPath(Path.Combine(_root, "output"));
        var create = await fs.CreateDirectoryAsync(directory, CancellationToken.None);
        var file = fs.Combine(directory, "sample.txt");
        await File.WriteAllTextAsync(file.Value, "12345");

        var size = await fs.GetFileSizeAsync(file, CancellationToken.None);

        Assert.True(create.Succeeded);
        Assert.True(size.Succeeded);
        Assert.Equal(5, size.Value);
    }


    [Fact]
    public async Task File_system_missing_file_size_returns_input_file_not_found()
    {
        var fs = new LocalFileSystemService();

        var result = await fs.GetFileSizeAsync(new LocalPath(Path.Combine(_root, "missing.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }
    [Fact]
    public void File_system_builds_indexed_path()
    {
        var fs = new LocalFileSystemService();
        var path = new LocalPath(Path.Combine(_root, "photo_atompix.jpg"));

        var indexed = fs.BuildIndexedPath(path, 2);

        Assert.EndsWith("photo_atompix_2.jpg", indexed.Value);
    }



    [Fact]
    public async Task Settings_save_with_canceled_token_returns_canceled_and_leaves_no_temporary_file()
    {
        Directory.CreateDirectory(_paths.AppDataDirectory.Value);
        var store = new JsonAppSettingsStore(_paths);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await store.SaveAsync(AppSettings.Default, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, result.Error!.Code);
        Assert.Empty(Directory.EnumerateFiles(_paths.AppDataDirectory.Value, ".*.tmp"));
    }
    [Fact]
    public async Task Settings_load_with_canceled_token_returns_canceled_failure()
    {
        var store = new JsonAppSettingsStore(_paths);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await store.LoadAsync(cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPix.Core.Errors.AtomPixErrorCode.OperationCanceled, result.Error!.Code);
    }

    [Fact]
    public async Task Stores_write_readable_json_with_stable_top_level_schema()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var recentStore = new JsonRecentItemsStore(_paths);
        var recentItem = new RecentItem(new LocalPath("C:\\tmp\\stable.jpg"), RecentItemKind.File, DateTimeOffset.Parse("2026-06-26T10:00:00+08:00"));

        var settingsSave = await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None);
        var recentSave = await recentStore.SaveAsync([recentItem], CancellationToken.None);

        Assert.True(settingsSave.Succeeded);
        Assert.True(recentSave.Succeeded);
        using var settingsJson = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath()));
        using var recentJson = JsonDocument.Parse(await File.ReadAllTextAsync(RecentItemsPath()));
        Assert.Equal(AppSettings.CurrentSchemaVersion, settingsJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(settingsJson.RootElement.TryGetProperty("defaultCompressionProfile", out _));
        Assert.True(settingsJson.RootElement.TryGetProperty("defaultConversionProfile", out _));
        Assert.True(settingsJson.RootElement.TryGetProperty("defaultSameFormatEncodingPolicy", out _));
        Assert.True(settingsJson.RootElement.TryGetProperty("defaultOutputPolicy", out _));
        Assert.Equal(JsonValueKind.Array, recentJson.RootElement.ValueKind);
        Assert.Equal("C:\\tmp\\stable.jpg", recentJson.RootElement[0].GetProperty("path").GetString());
        Assert.Equal((int)RecentItemKind.File, recentJson.RootElement[0].GetProperty("kind").GetInt32());
    }

    [Fact]
    public async Task Store_save_failure_preserves_existing_files_and_cleans_temporary_files()
    {
        var settingsStore = new JsonAppSettingsStore(_paths);
        var recentStore = new JsonRecentItemsStore(_paths);
        var recent = new RecentItem(new LocalPath("C:\\tmp\\old.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow);
        Assert.True((await settingsStore.SaveAsync(AppSettings.Default, CancellationToken.None)).Succeeded);
        Assert.True((await recentStore.SaveAsync([recent], CancellationToken.None)).Succeeded);
        var originalSettings = await File.ReadAllTextAsync(SettingsPath());
        var originalRecent = await File.ReadAllTextAsync(RecentItemsPath());

        var settingsLock = new FileStream(SettingsPath(), FileMode.Open, FileAccess.Read, FileShare.None);
        var recentLock = new FileStream(RecentItemsPath(), FileMode.Open, FileAccess.Read, FileShare.None);
        var settingsSave = await settingsStore.SaveAsync(new AppSettings(
            AppSettings.Default.DefaultCompressionProfile,
            AppSettings.Default.DefaultConversionProfile,
            AppSettings.Default.DefaultSameFormatEncodingPolicy,
            AppSettings.Default.DefaultOutputPolicy,
            ThemeMode.Dark,
            AppSettings.Default.Language,
            AppSettings.Default.RecentItems), CancellationToken.None);
        var recentSave = await recentStore.SaveAsync([new RecentItem(new LocalPath("C:\\tmp\\new.jpg"), RecentItemKind.File, DateTimeOffset.UtcNow)], CancellationToken.None);

        Assert.False(settingsSave.Succeeded);
        Assert.Equal(AtomPixErrorCode.SettingsSaveFailed, settingsSave.Error!.Code);
        Assert.False(recentSave.Succeeded);
        Assert.Equal(AtomPixErrorCode.RecentItemsSaveFailed, recentSave.Error!.Code);
        settingsLock.Dispose();
        recentLock.Dispose();
        Assert.Equal(originalSettings, await File.ReadAllTextAsync(SettingsPath()));
        Assert.Equal(originalRecent, await File.ReadAllTextAsync(RecentItemsPath()));
        Assert.Empty(Directory.EnumerateFiles(_paths.AppDataDirectory.Value, ".*.tmp"));
    }

    [Fact]
    public void Stores_reject_null_path_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonAppSettingsStore(null!));
        Assert.Throws<ArgumentNullException>(() => new JsonRecentItemsStore(null!));
    }

    [Fact]
    public async Task Stores_reject_null_save_payloads()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new JsonAppSettingsStore(_paths).SaveAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new JsonRecentItemsStore(_paths).SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void File_system_rejects_invalid_file_name_segments()
    {
        var fs = new LocalFileSystemService();
        var directory = new LocalPath(Path.Combine(_root, "output"));

        Assert.Throws<ArgumentException>(() => fs.Combine(directory, ""));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, "   "));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, Path.Combine("nested", "file.jpg")));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, Path.GetFullPath(Path.Combine(_root, "absolute.jpg"))));
    }


    [Fact]
    public void File_system_rejects_cross_platform_directory_separators_and_traversal_segments()
    {
        var fs = new LocalFileSystemService();
        var directory = new LocalPath(Path.Combine(_root, "output"));

        Assert.Throws<ArgumentException>(() => fs.Combine(directory, "nested/file.jpg"));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, "nested\\file.jpg"));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, "."));
        Assert.Throws<ArgumentException>(() => fs.Combine(directory, ".."));
    }

    [Fact]
    public void File_system_builds_indexed_path_for_no_extension_and_multi_dot_names()
    {
        var fs = new LocalFileSystemService();
        var noExtension = new LocalPath(Path.Combine(_root, "photo"));
        var multiDot = new LocalPath(Path.Combine(_root, "archive.photo.final.jpg"));

        Assert.EndsWith("photo_1", fs.BuildIndexedPath(noExtension, 1).Value);
        Assert.EndsWith("archive.photo.final_2.jpg", fs.BuildIndexedPath(multiDot, 2).Value);
        Assert.EndsWith("archive.photo.final_99.jpg", fs.BuildIndexedPath(multiDot, 99).Value);
    }

    [Fact]
    public void File_system_change_extension_preserves_multi_dot_file_name()
    {
        var fs = new LocalFileSystemService();
        var path = new LocalPath(Path.Combine(_root, "archive.photo.final.jpeg"));

        var changed = fs.ChangeExtension(path, ".webp");

        Assert.EndsWith("archive.photo.final.webp", changed.Value);
    }

    [Fact]
    public void App_path_provider_uses_injected_paths()
    {
        var appData = Path.Combine(_root, "custom-appdata");
        var temp = Path.Combine(_root, "custom-temp");

        var provider = new AppPathProvider(appData, temp);

        Assert.Equal(appData, provider.AppDataDirectory.Value);
        Assert.Equal(temp, provider.TempDirectory.Value);
        Assert.False(Directory.Exists(appData));
        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void App_path_provider_default_paths_end_with_atompix()
    {
        var provider = new AppPathProvider();

        Assert.EndsWith("AtomPix", provider.AppDataDirectory.Value);
        Assert.EndsWith("AtomPix", provider.TempDirectory.Value);
    }
    [Fact]
    public void File_system_normalizes_extension()
    {
        var fs = new LocalFileSystemService();
        var path = new LocalPath(Path.Combine(_root, "photo.jpeg"));
        var noExtension = new LocalPath(Path.Combine(_root, "photo"));

        var changed = fs.ChangeExtension(path, "webp");
        var changedFromNoExtension = fs.ChangeExtension(noExtension, "png");

        Assert.EndsWith("photo.webp", changed.Value);
        Assert.EndsWith("photo.png", changedFromNoExtension.Value);
    }

    [Fact]
    public void File_system_rejects_empty_extension()
    {
        var fs = new LocalFileSystemService();
        var path = new LocalPath(Path.Combine(_root, "photo.jpeg"));

        Assert.Throws<ArgumentException>(() => fs.ChangeExtension(path, ""));
    }

    [Fact]
    public void File_system_rejects_non_positive_index()
    {
        var fs = new LocalFileSystemService();
        var path = new LocalPath(Path.Combine(_root, "photo.jpg"));

        Assert.Throws<ArgumentOutOfRangeException>(() => fs.BuildIndexedPath(path, 0));
    }

    [Fact]
    public void File_system_build_indexed_path_requires_file_name()
    {
        var fs = new LocalFileSystemService();

        Assert.Throws<ArgumentException>(() => fs.BuildIndexedPath(new LocalPath(Path.Combine(_root, ".jpg")), 1));
    }

    [Fact]
    public async Task File_system_operations_map_canceled_token_to_canceled_failure()
    {
        var fs = new LocalFileSystemService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var create = await fs.CreateDirectoryAsync(new LocalPath(Path.Combine(_root, "output")), cts.Token);
        var size = await fs.GetFileSizeAsync(new LocalPath(Path.Combine(_root, "photo.jpg")), cts.Token);
        var enumerate = await fs.EnumerateFilesAsync(new LocalPath(Path.Combine(_root, "input")), cts.Token);

        Assert.False(create.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, create.Error!.Code);
        Assert.False(size.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, size.Error!.Code);
        Assert.False(enumerate.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, enumerate.Error!.Code);
    }

    [Fact]
    public async Task File_system_enumerates_only_current_directory_as_normalized_snapshot()
    {
        var fs = new LocalFileSystemService();
        var directoryPath = Path.Combine(_root, "input");
        var nestedPath = Path.Combine(directoryPath, "nested");
        Directory.CreateDirectory(nestedPath);
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "a.jpg"), "a");
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "b.txt"), "b");
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "c.png"), "c");

        var result = await fs.EnumerateFilesAsync(new LocalPath(directoryPath), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value, path => Assert.True(Path.IsPathFullyQualified(path.Value)));
        Assert.DoesNotContain(result.Value, path => path.Value.EndsWith("c.png", StringComparison.Ordinal));
    }

    [Fact]
    public void File_system_normalizes_and_compares_paths_with_platform_semantics()
    {
        var fs = new LocalFileSystemService();
        var relative = new LocalPath(Path.Combine(".", "folder", "..", "photo.jpg"));
        var absolute = new LocalPath(Path.GetFullPath("photo.jpg"));

        var normalized = fs.NormalizePath(relative);

        Assert.True(normalized.Succeeded);
        Assert.Equal(absolute, normalized.Value);
        Assert.True(fs.PathsEqual(relative, absolute));
        Assert.Equal(0, fs.ComparePaths(relative, absolute));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(fs.PathsEqual(absolute, new LocalPath(absolute.Value.ToUpperInvariant())));
        }
    }

    [Fact]
    public void File_system_reports_existing_files_and_directories()
    {
        var fs = new LocalFileSystemService();
        var directory = new LocalPath(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(directory.Value);
        var file = fs.Combine(directory, "photo.jpg");
        File.WriteAllText(file.Value, "x");

        Assert.True(fs.DirectoryExists(directory));
        Assert.True(fs.FileExists(file));
        Assert.False(fs.FileExists(new LocalPath(Path.Combine(_root, "missing.jpg"))));
        Assert.False(fs.DirectoryExists(new LocalPath(Path.Combine(_root, "missing"))));
    }

    private string SettingsPath() => Path.Combine(_paths.AppDataDirectory.Value, "settings.json");

    private string RecentItemsPath() => Path.Combine(_paths.AppDataDirectory.Value, "recent-items.json");
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}








