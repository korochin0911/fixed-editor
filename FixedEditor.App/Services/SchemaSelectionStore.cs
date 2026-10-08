namespace FixedEditor.App.Services;

internal sealed class SchemaSelectionStore(string settingsDirectory)
{
    private readonly string _settingsPath = Path.Combine(settingsDirectory, "last-schema-path.txt");

    public string? GetLastSchemaPath()
    {
        try
        {
            return File.ReadAllText(_settingsPath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void RememberSchemaPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(settingsDirectory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, fullPath);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
