using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FixedEditor.Core.Models;

namespace FixedEditor.App.Services;

internal sealed class VisibleFieldSelectionStore(string settingsDirectory)
{
    public IReadOnlyList<int> GetVisibleFieldIndexes(string schemaPath, int schemaIndex, FixedFileSchema schema)
    {
        var allIndexes = Enumerable.Range(0, schema.Fields.Count).ToArray();
        FieldSelection[]? selections;
        try
        {
            var json = File.ReadAllText(GetSettingsPath(schemaPath, schemaIndex, schema));
            selections = JsonSerializer.Deserialize<FieldSelection[]>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return allIndexes;
        }

        if (selections is null || selections.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name)))
        {
            return allIndexes;
        }

        // Keep occurrences separate even when a schema contains duplicate field names.
        var savedFields = selections
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<bool>(group.Select(item => item.IsVisible)),
                StringComparer.Ordinal);
        var visibleIndexes = new List<int>();
        for (var fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
        {
            var fieldName = schema.Fields[fieldIndex].Name;
            if (!savedFields.TryGetValue(fieldName, out var states) || states.Count == 0 || states.Dequeue())
            {
                visibleIndexes.Add(fieldIndex);
            }
        }

        return visibleIndexes.Count > 0 ? visibleIndexes : allIndexes;
    }

    public void RememberVisibleFields(string schemaPath, int schemaIndex, FixedFileSchema schema,
        IEnumerable<int> visibleFieldIndexes)
    {
        var visibleIndexes = visibleFieldIndexes.ToHashSet();
        if (visibleIndexes.Count == 0 || visibleIndexes.Any(index => index < 0 || index >= schema.Fields.Count))
        {
            throw new ArgumentException("At least one valid field must be selected.", nameof(visibleFieldIndexes));
        }

        var selections = schema.Fields
            .Select((field, index) => new FieldSelection(field.Name, visibleIndexes.Contains(index)))
            .ToArray();
        var settingsPath = GetSettingsPath(schemaPath, schemaIndex, schema);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(selections));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }

    private string GetSettingsPath(string schemaPath, int schemaIndex, FixedFileSchema schema)
    {
        var identity = JsonSerializer.Serialize(new[]
        {
            Path.GetFullPath(schemaPath).ToUpperInvariant(),
            schemaIndex.ToString(CultureInfo.InvariantCulture),
            schema.Name, schema.FileName, schema.FilePattern
        });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(settingsDirectory, "displayed-fields", key + ".json");
    }

    private sealed record FieldSelection(
        [property: JsonRequired] string Name,
        [property: JsonRequired] bool IsVisible);
}
