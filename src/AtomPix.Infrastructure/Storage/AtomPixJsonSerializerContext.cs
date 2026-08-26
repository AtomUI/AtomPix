namespace AtomPix.Infrastructure.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;
using AtomPix.Core.Settings;
using AtomPix.Infrastructure.Configuration;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PersistedAppSettings))]
[JsonSerializable(typeof(List<RecentItem>))]
internal sealed partial class AtomPixJsonSerializerContext : JsonSerializerContext;
