using FixedEditor.App.Services;
using FixedEditor.Core.Models;

var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
var testDirectory = Directory.CreateTempSubdirectory("FixedEditor-selection-tests-").FullName;
var assertions = 0;
try
{
    var store = new VisibleFieldSelectionStore(testDirectory);
    var schemaPath = Path.Combine(testDirectory, "customer.schema.json");
    var schema = CreateSchema("customerCode", "amount", "memo");
    AssertIndexes("No saved settings", store.GetVisibleFieldIndexes(schemaPath, 0, schema), 0, 1, 2);

    store.RememberVisibleFields(schemaPath, 0, schema, [0, 2]);
    var restartedStore = new VisibleFieldSelectionStore(testDirectory);
    var reloadedSchema = CreateSchema("customerCode", "amount", "memo");
    AssertIndexes("Restore from disk with fresh store and schema",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, reloadedSchema), 0, 2);
    AssertIndexes("Windows path case and relative segments",
        restartedStore.GetVisibleFieldIndexes(Path.Combine(testDirectory, ".", "CUSTOMER.schema.json"), 0, reloadedSchema), 0, 2);

    store.RememberVisibleFields(schemaPath, 0, schema, [1]);
    AssertIndexes("Replace previous selection", restartedStore.GetVisibleFieldIndexes(schemaPath, 0, reloadedSchema), 1);
    AssertIndexes("Different schema file starts with all fields",
        restartedStore.GetVisibleFieldIndexes(Path.Combine(testDirectory, "other.schema.json"), 0, reloadedSchema), 0, 1, 2);

    store.RememberVisibleFields(schemaPath, 1, schema, [2]);
    AssertIndexes("First definition stays independent", restartedStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 1);
    AssertIndexes("Second definition stays independent", restartedStore.GetVisibleFieldIndexes(schemaPath, 1, schema), 2);
    var namedSchema = CreateSchema("customerCode", "amount", "memo");
    namedSchema.Name = "Another definition";
    AssertIndexes("Replaced definition does not inherit settings",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, namedSchema), 0, 1, 2);

    var reorderedSchema = CreateSchema("memo", "customerCode", "amount");
    reorderedSchema.Fields[2].Label = "金額（名称変更）";
    AssertIndexes("Reorder fields and rename labels",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, reorderedSchema), 2);
    AssertIndexes("New field is visible; existing hidden fields stay hidden",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, CreateSchema("amount", "customerCode", "memo", "newField")), 0, 3);
    AssertIndexes("Removing the only selected field falls back to all fields",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, CreateSchema("customerCode", "memo")), 0, 1);

    var duplicateSchema = CreateSchema("sameName", "sameName", "other");
    duplicateSchema.Name = "Duplicate fields";
    store.RememberVisibleFields(schemaPath, 0, duplicateSchema, [1]);
    AssertIndexes("Duplicate names preserve separate selections",
        restartedStore.GetVisibleFieldIndexes(schemaPath, 0, duplicateSchema), 1);

    foreach (var invalidIndexes in new int[][] { [], [-1], [schema.Fields.Count] })
    {
        AssertThrows<ArgumentException>("Reject invalid selection",
            () => store.RememberVisibleFields(schemaPath, 0, schema, invalidIndexes));
        AssertIndexes("Invalid save leaves existing settings intact",
            restartedStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 1);
    }

    var corruptDirectory = Path.Combine(testDirectory, "corrupt");
    var corruptStore = new VisibleFieldSelectionStore(corruptDirectory);
    corruptStore.RememberVisibleFields(schemaPath, 0, schema, [1]);
    var settingsFile = Directory.GetFiles(Path.Combine(corruptDirectory, "displayed-fields"), "*.json").Single();
    foreach (var contents in new[]
    {
        "{", "null", "[null]", "[]",
        "[{\"Name\":\"amount\"}]",
        "[{\"Name\":\"\",\"IsVisible\":true}]",
        "[{\"Name\":\"customerCode\",\"IsVisible\":false},{\"Name\":\"amount\",\"IsVisible\":false},{\"Name\":\"memo\",\"IsVisible\":false}]"
    })
    {
        File.WriteAllText(settingsFile, contents);
        AssertIndexes("Unreadable or unusable settings fall back to all fields",
            corruptStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 0, 1, 2);
    }
    corruptStore.RememberVisibleFields(schemaPath, 0, schema, [2]);
    AssertIndexes("Saving recovers corrupt settings", corruptStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 2);

    using (File.Open(settingsFile + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
    {
        AssertThrows<IOException>("Report write failure",
            () => corruptStore.RememberVisibleFields(schemaPath, 0, schema, [0]));
        AssertIndexes("Failed save preserves last complete selection",
            corruptStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 2);
    }

    var blockedDirectory = Path.Combine(testDirectory, "blocked");
    File.WriteAllText(blockedDirectory, "A file blocks the settings directory.");
    var blockedStore = new VisibleFieldSelectionStore(blockedDirectory);
    AssertIndexes("Read failure falls back to all fields", blockedStore.GetVisibleFieldIndexes(schemaPath, 0, schema), 0, 1, 2);
    AssertThrows<IOException>("Report settings-directory failure",
        () => blockedStore.RememberVisibleFields(schemaPath, 0, schema, [0]));

    Console.WriteLine($"Passed {assertions} displayed-field persistence checks.");
}
finally
{
    var cleanupPath = Path.GetFullPath(testDirectory);
    if (!cleanupPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(cleanupPath).StartsWith("FixedEditor-selection-tests-", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Refusing to clean up a directory outside the test temporary root.");
    }
    Directory.Delete(cleanupPath, recursive: true);
}

static FixedFileSchema CreateSchema(params string[] names) => new()
{
    RecordLength = names.Length,
    Fields = names.Select((name, index) => new FixedFieldDefinition
    {
        Name = name, Label = name, Start = index + 1, Length = 1
    }).ToList()
};

void AssertIndexes(string scenario, IReadOnlyList<int> actual, params int[] expected)
{
    if (!actual.SequenceEqual(expected))
    {
        throw new Exception($"{scenario}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }
    assertions++;
}

void AssertThrows<TException>(string scenario, Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        assertions++;
        return;
    }
    throw new Exception($"{scenario}: expected {typeof(TException).Name}.");
}
