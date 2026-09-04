using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrouchyFiler.Models;

[JsonConverter(typeof(PatternRuleConverter))]
public class PatternRule
{
    public string Type { get; set; } = "glob"; // glob, regex, literal
    public string Value { get; set; } = "";
}

// Preserve existing string patterns as shorthand for glob patterns.
public sealed class PatternRuleConverter : JsonConverter<PatternRule>
{
    public override PatternRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new PatternRule { Value = reader.GetString()! };

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A pattern must be a string or an object with Type and Value.");
        var pattern = new PatternRule();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new JsonException("Pattern Type and Value must be strings.");
            if (string.Equals(property.Name, "Type", StringComparison.OrdinalIgnoreCase))
                pattern.Type = property.Value.GetString()!;
            else if (string.Equals(property.Name, "Value", StringComparison.OrdinalIgnoreCase))
                pattern.Value = property.Value.GetString()!;
            else throw new JsonException($"Unknown pattern property: {property.Name}");
        }
        return pattern;
    }

    public override void Write(Utf8JsonWriter writer, PatternRule value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Type", value.Type);
        writer.WriteString("Value", value.Value);
        writer.WriteEndObject();
    }
}
