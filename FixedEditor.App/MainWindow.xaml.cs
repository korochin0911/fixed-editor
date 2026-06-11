using FixedEditor.Core.Models;
using FixedEditor.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace FixedEditor.App;

public sealed partial class MainWindow : Window
{
    private readonly FixedLengthFileService _fileService = new();
    private readonly List<FixedRecord> _records = [];
    private FixedFileSchema? _schema;
    private string? _currentFilePath;
    private Border? _activeCell;
    private TextBox? _activeEditor;
    private (int RowIndex, int FieldIndex) _activePosition;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
    }

    private async void LoadSchemaButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("JSON schema", ".json");
        if (path is null)
        {
            return;
        }

        try
        {
            _schema = FixedFileSchema.Load(path);
            _records.Clear();
            _currentFilePath = null;
            RenderTable();
            SetStatus($"Loaded schema: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not load schema", ex.Message);
        }
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSchema())
        {
            return;
        }

        var path = await PickOpenFileAsync("Fixed-length file", "*");
        if (path is null)
        {
            return;
        }

        try
        {
            _records.Clear();
            _records.AddRange(_fileService.Read(path, _schema!));
            _currentFilePath = path;
            RenderTable();
            SetStatus($"Loaded {_records.Count:N0} records from {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not open fixed-length file", ex.Message);
        }
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSchema())
        {
            return;
        }

        var path = await PickSaveFileAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            _fileService.Write(path, _schema!, _records);
            _currentFilePath = path;
            SetStatus($"Saved {_records.Count:N0} records to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not save file", ex.Message);
        }
    }

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSchema())
        {
            return;
        }

        var values = Enumerable.Repeat("", _schema!.Fields.Count).ToArray();
        _records.Add(new FixedRecord(_records.Count + 1, values));
        RenderTable();
        SetStatus("Added a blank row.");
    }

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSchema())
        {
            return;
        }

        var errors = _records
            .SelectMany(record => _fileService.ValidateRecord(_schema!, record))
            .Take(50)
            .ToArray();

        if (errors.Length == 0)
        {
            SetStatus($"Validation passed for {_records.Count:N0} records.");
            return;
        }

        await ShowErrorAsync("Validation errors", string.Join(Environment.NewLine, errors));
    }

    private void RenderTable()
    {
        CommitActiveCellEdit(true);
        TableGrid.Children.Clear();
        TableGrid.ColumnDefinitions.Clear();
        TableGrid.RowDefinitions.Clear();

        if (_schema is null)
        {
            return;
        }

        TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        foreach (var field in _schema.Fields)
        {
            TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetColumnWidth(field)) });
        }

        TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddTableCell(CreateHeaderCell("#", HorizontalAlignment.Left), 0, 0);
        for (var fieldIndex = 0; fieldIndex < _schema.Fields.Count; fieldIndex++)
        {
            var field = _schema.Fields[fieldIndex];
            AddTableCell(CreateHeaderCell($"{field.DisplayName} ({field.Length})", HorizontalAlignment.Left), 0, fieldIndex + 1);
        }

        if (_records.Count == 0)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var emptyText = new TextBlock
            {
                Text = "No records loaded.",
                Margin = new Thickness(8, 12, 0, 0),
                Foreground = new SolidColorBrush(Colors.Gray)
            };
            Grid.SetRow(emptyText, 1);
            Grid.SetColumn(emptyText, 0);
            Grid.SetColumnSpan(emptyText, Math.Max(1, _schema.Fields.Count + 1));
            TableGrid.Children.Add(emptyText);
            return;
        }

        for (var rowIndex = 0; rowIndex < _records.Count; rowIndex++)
        {
            var gridRow = rowIndex + 1;
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableCell(CreateRowNumberCell(_records[rowIndex].LineNumber), gridRow, 0);

            for (var fieldIndex = 0; fieldIndex < _schema.Fields.Count; fieldIndex++)
            {
                AddTableCell(CreateValueCell(rowIndex, fieldIndex), gridRow, fieldIndex + 1);
            }
        }
    }

    private void AddTableCell(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        TableGrid.Children.Add(element);
    }

    private Border CreateHeaderCell(string text, HorizontalAlignment alignment)
    {
        var border = CreateTableBorder(new SolidColorBrush(Colors.WhiteSmoke));
        border.Child = new TextBlock
        {
            Text = text,
            Padding = new Thickness(8, 6, 8, 6),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        return border;
    }

    private Border CreateRowNumberCell(int lineNumber)
    {
        var border = CreateTableBorder(new SolidColorBrush(Colors.WhiteSmoke));
        border.Child = new TextBlock
        {
            Text = lineNumber.ToString(),
            Padding = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        return border;
    }

    private Border CreateValueCell(int rowIndex, int fieldIndex)
    {
        var border = CreateTableBorder(new SolidColorBrush(Colors.White));
        border.Tag = (rowIndex, fieldIndex);
        border.Tapped += ValueCell_Tapped;
        border.Child = CreateValueTextBlock(rowIndex, fieldIndex);
        return border;
    }

    private TextBlock CreateValueTextBlock(int rowIndex, int fieldIndex)
    {
        var field = _schema!.Fields[fieldIndex];
        var value = fieldIndex < _records[rowIndex].Values.Length ? _records[rowIndex].Values[fieldIndex] : "";
        return new TextBlock
        {
            Text = value,
            Padding = new Thickness(8, 6, 8, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = field.Type == FixedFieldType.Number ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            TextAlignment = field.Type == FixedFieldType.Number ? TextAlignment.Right : TextAlignment.Left
        };
    }

    private Border CreateTableBorder(Brush background)
    {
        return new Border
        {
            Background = background,
            BorderBrush = new SolidColorBrush(Colors.LightGray),
            BorderThickness = new Thickness(0, 0, 1, 1),
            MinHeight = 34
        };
    }

    private void ValueCell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: ValueTuple<int, int> position } border)
        {
            return;
        }

        StartCellEdit(border, position.Item1, position.Item2);
        e.Handled = true;
    }

    private void StartCellEdit(Border cell, int rowIndex, int fieldIndex)
    {
        CommitActiveCellEdit(true);

        var field = _schema!.Fields[fieldIndex];
        var value = fieldIndex < _records[rowIndex].Values.Length ? _records[rowIndex].Values[fieldIndex] : "";
        var editor = new TextBox
        {
            Text = value,
            Padding = new Thickness(6, 2, 6, 2),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = field.Type == FixedFieldType.Number
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left
        };
        editor.LostFocus += ActiveEditor_LostFocus;
        editor.KeyDown += ActiveEditor_KeyDown;

        _activeCell = cell;
        _activeEditor = editor;
        _activePosition = (rowIndex, fieldIndex);
        cell.Child = editor;
        editor.Focus(FocusState.Programmatic);
        editor.SelectAll();
    }

    private void ActiveEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitActiveCellEdit(true);
    }

    private void ActiveEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitActiveCellEdit(true);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CommitActiveCellEdit(false);
            e.Handled = true;
        }
    }

    private void CommitActiveCellEdit(bool saveValue)
    {
        if (_activeCell is null || _activeEditor is null)
        {
            return;
        }

        var cell = _activeCell;
        var editor = _activeEditor;
        var (rowIndex, fieldIndex) = _activePosition;
        _activeCell = null;
        _activeEditor = null;
        editor.LostFocus -= ActiveEditor_LostFocus;
        editor.KeyDown -= ActiveEditor_KeyDown;

        if (rowIndex >= 0 && rowIndex < _records.Count && fieldIndex >= 0 && fieldIndex < _records[rowIndex].Values.Length)
        {
            if (saveValue)
            {
                _records[rowIndex].Values[fieldIndex] = editor.Text;
            }

            cell.Child = CreateValueTextBlock(rowIndex, fieldIndex);
        }
    }

    private static double GetColumnWidth(FixedFieldDefinition field)
    {
        return Math.Clamp(field.Length * 12.0 + 32.0, 120.0, 320.0);
    }

    private bool EnsureSchema()
    {
        if (_schema is not null)
        {
            return true;
        }

        SetStatus("Load a schema before opening, editing, or saving data.");
        return false;
    }

    private async Task<string?> PickOpenFileAsync(string name, params string[] extensions)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickSaveFileAsync()
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = _currentFilePath is null
            ? "fixed-length-output.txt"
            : Path.GetFileName(_currentFilePath);
        picker.FileTypeChoices.Add("Fixed-length file", [".txt", ".dat"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                MaxHeight = 360
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }
}
