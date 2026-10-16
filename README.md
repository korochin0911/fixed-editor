# FixedEditor

FixedEditor is a WinUI 3 desktop app for viewing and editing fixed-length record files in a spreadsheet-like table.

## Projects

- `FixedEditor.App`: WinUI 3 app.
- `FixedEditor.Core`: schema model, fixed-length reader/writer, and validation logic.
- `samples`: sample schema and fixed-length data.

## Run

```powershell
dotnet build FixedEditor.sln -c Debug
dotnet run --project FixedEditor.App\FixedEditor.App.csproj
```

Run the displayed-field persistence regression checks with `dotnet run --project tests/FixedEditor.RegressionTests`.

## Choose displayed fields

After loading a schema (or opening a data file when multiple schemas are loaded), click **表示項目** in the toolbar. Check the fields to display and click **適用**. Use **すべて選択** to select or clear all fields; at least one field must be selected to apply. **キャンセル** leaves the displayed fields unchanged.

Use **項目名を検索** to narrow the list by a partial display name or schema field name. Matching ignores case, character width, and hiragana/katakana differences, and leading/trailing whitespace is trimmed. Clearing the search shows all fields again. Check states are retained while searching, and **検索結果をすべて選択** selects or clears only the matching fields. The dialog shows the matching and total selected counts, and indicates when no fields match.

Columns keep their schema order. Hidden fields retain their data and are included when saving or validating. Filters on fields you hide are cleared.

Clicking **適用** saves the displayed-field selection separately for each schema file and definition in `%LOCALAPPDATA%\FixedEditor\displayed-fields`. The selection is restored when that schema is loaded on the next launch, manually reloaded, or selected after opening a data file. **キャンセル** does not change the saved selection. Fields are matched by their schema names, so reordering fields or changing display labels preserves the selection; newly added fields are displayed by default. If no saved selection is available, it cannot be read, or restoring it would leave no visible fields, all fields are displayed. If saving fails, the current selection still applies and the status bar reports the failure.

## Schema

The app reads a JSON schema before opening a data file.

The last successfully loaded schema file is remembered in `%LOCALAPPDATA%\FixedEditor\last-schema-path.txt` and automatically loaded on the next launch, including all definitions in a multi-schema file. Canceling the picker or selecting an invalid schema leaves the remembered path unchanged. If the remembered file cannot be loaded (for example, it was moved or deleted), the app starts normally and asks you to select a schema using **Load schema** in the status bar.

```json
{
  "recordLength": 38,
  "encoding": "shift_jis",
  "recordSeparator": "lf",
  "fields": [
    { "name": "customerCode", "label": "Customer Code", "start": 1, "length": 10, "type": "string", "required": true },
    { "name": "amount", "label": "Amount", "start": 27, "length": 6, "type": "number" }
  ]
}
```

`recordSeparator` can be empty, `none`, `lf`, or `crlf`.

One schema JSON can also contain multiple file definitions:

```json
{
  "files": [
    {
      "name": "Customers",
      "filePattern": "customer.*",
      "recordLength": 38,
      "encoding": "shift_jis",
      "recordSeparator": "lf",
      "fields": []
    },
    {
      "name": "Products",
      "fileName": "products.dat",
      "recordLength": 80,
      "fields": []
    }
  ]
}
```

When multiple definitions are loaded, FixedEditor picks a matching `fileName` or `filePattern`; otherwise it asks which schema to use.
