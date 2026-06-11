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

## Schema

The app reads a JSON schema before opening a data file.

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
