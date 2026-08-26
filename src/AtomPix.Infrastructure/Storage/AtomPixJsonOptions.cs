namespace AtomPix.Infrastructure.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;
using AtomPix.Core.Compression;
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

internal sealed class ImageQualityJsonConverter : JsonConverter<ImageQuality>
{
    public override ImageQuality Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("ImageQuality must be represented as an object.");
        }

        int? value = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (value is null)
                {
                    throw new JsonException("ImageQuality.value is required.");
                }

                try
                {
                    return new ImageQuality(value.Value);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new JsonException("ImageQuality.value must be between 1 and 100.", exception);
                }
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Invalid ImageQuality payload.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Incomplete ImageQuality payload.");
            }

            if (string.Equals(propertyName, "value", StringComparison.OrdinalIgnoreCase))
            {
                value = reader.GetInt32();
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Incomplete ImageQuality payload.");
    }

    public override void Write(Utf8JsonWriter writer, ImageQuality value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("value", value.Value);
        writer.WriteEndObject();
    }
}

internal static class AtomPixJsonOptions
{
    private static readonly Lazy<AtomPixJsonSerializerContext> ContextFactory = new(
        () => new AtomPixJsonSerializerContext(CreateIndented()));

    public static AtomPixJsonSerializerContext Context => ContextFactory.Value;

    public static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new LocalPathJsonConverter());
        options.Converters.Add(new ImageQualityJsonConverter());
        return options;
    }
}
