using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedEditor.Core.Models;

public sealed class FixedFileSchema
{
    public string? Name { get; set; }
    public string? FileName { get; set; }
    public string? FilePattern { get; set; }
    public int RecordLength { get; set; }
    public string Encoding { get; set; } = "shift_jis";
    public string RecordSeparator { get; set; } = "";
    public bool TrimTrailingNewLine { get; set; } = true;
    public List<FixedFieldDefinition> Fields { get; set; } = [];

    public static FixedFileSchema Load(string path)
    {
        var json = File.ReadAllText(path);
        var schema = JsonSerializer.Deserialize<FixedFileSchema>(json, JsonOptions)
            ?? throw new InvalidDataException("Schema file is empty.");
        schema.Validate();
        return schema;
    }

    public static IReadOnlyList<FixedFileSchema> LoadAll(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (TryLoadSchemaCollection(root, "files", out var fileSchemas) ||
            TryLoadSchemaCollection(root, "schemas", out fileSchemas))
        {
            foreach (var fileSchema in fileSchemas)
            {
                fileSchema.Validate();
            }

            return fileSchemas;
        }

        var singleSchema = JsonSerializer.Deserialize<FixedFileSchema>(json, JsonOptions)
            ?? throw new InvalidDataException("Schema file is empty.");
        singleSchema.Validate();
        return [singleSchema];
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void Validate()
    {
        if (RecordLength <= 0)
        {
            throw new InvalidDataException("RecordLength must be greater than zero.");
        }

        if (Fields.Count == 0)
        {
            throw new InvalidDataException("At least one field is required.");
        }

        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                throw new InvalidDataException("Field name is required.");
            }

            if (field.Start < 1 || field.Length < 1)
            {
                throw new InvalidDataException($"Field '{field.Name}' has an invalid start or length.");
            }

            var end = field.Start + field.Length - 1;
            if (end > RecordLength)
            {
                throw new InvalidDataException($"Field '{field.Name}' exceeds the record length.");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static bool TryLoadSchemaCollection(JsonElement root, string propertyName, out List<FixedFileSchema> schemas)
    {
        schemas = [];
        if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        schemas = JsonSerializer.Deserialize<List<FixedFileSchema>>(items.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException($"Schema collection '{propertyName}' is empty.");
        if (schemas.Count == 0)
        {
            throw new InvalidDataException($"Schema collection '{propertyName}' must contain at least one schema.");
        }

        return true;
    }
}
