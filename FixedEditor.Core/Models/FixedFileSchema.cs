using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedEditor.Core.Models;

public sealed class FixedFileSchema
{
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
}
