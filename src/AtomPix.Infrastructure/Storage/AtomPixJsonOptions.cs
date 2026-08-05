namespace AtomPix.Infrastructure.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;
using AtomPix.Core.ValueObjects;

internal sealed class LocalPathJsonConverter : JsonConverter<LocalPath>
{
    public override LocalPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("LocalPath value cannot be empty.");
        }

        return new LocalPath(value);
    }

    public override void Write(Utf8JsonWriter writer, LocalPath value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

internal static class AtomPixJsonOptions
{
    public static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new LocalPathJsonConverter());
        return options;
    }
}
