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
    private const double RowHeight = 34.0;
    private const double RowNumberColumnWidth = 64.0;
    private const int RowBuffer = 4;

    private readonly FixedLengthFileService _fileService = new();
    private readonly List<FixedRecord> _records = [];
    private readonly List<int> _visibleRecordIndexes = [];
    private readonly List<double> _columnLefts = [];
    private readonly List<double> _columnWidths = [];
    private readonly Dictionary<int, string> _filters = [];
    private FixedFileSchema? _schema;
    private string? _currentFilePath;
    private double _tableWidth;
    private int _renderedFirstVisibleRow = -1;
    private int _renderedLastVisibleRow = -1;
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
            _visibleRecordIndexes.Clear();
            _filters.Clear();
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
            ApplyFilters();
            _currentFilePath = path;
            RenderTable();
            SetLoadedStatus(path);
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
        ApplyFilters();
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
        ApplyFilters();
        ConfigureTableMetrics();
        RenderHeader();
        RenderVisibleTable(true);
    }

    private void TableScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        HeaderScrollViewer.ChangeView(TableScrollViewer.HorizontalOffset, null, null, true);
        RenderVisibleTable(false);
    }

    private void TableScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ConfigureTableMetrics();
        RenderVisibleTable(true);
    }

    private void ConfigureTableMetrics()
    {
        _columnLefts.Clear();
        _columnWidths.Clear();
        _tableWidth = 0;
        _renderedFirstVisibleRow = -1;
        _renderedLastVisibleRow = -1;

        if (_schema is null)
        {
            TableCanvas.Children.Clear();
            HeaderCanvas.Children.Clear();
            TableCanvas.Width = 0;
            TableCanvas.Height = 0;
            HeaderCanvas.Width = 0;
            return;
        }

        AddColumnMetric(RowNumberColumnWidth);
        foreach (var field in _schema.Fields)
        {
            AddColumnMetric(GetColumnWidth(field));
        }

        TableCanvas.Width = _tableWidth;
        HeaderCanvas.Width = _tableWidth;
        TableCanvas.Height = Math.Max(RowHeight * Math.Max(1, _visibleRecordIndexes.Count), TableScrollViewer.ViewportHeight);
    }

    private void AddColumnMetric(double width)
    {
        _columnLefts.Add(_tableWidth);
        _columnWidths.Add(width);
        _tableWidth += width;
    }

    private void RenderVisibleTable(bool force)
    {
        if (_schema is null)
        {
            TableCanvas.Children.Clear();
            return;
        }

        var (firstVisibleRow, lastVisibleRow) = GetVisibleRecordRange();
        if (!force && firstVisibleRow == _renderedFirstVisibleRow && lastVisibleRow == _renderedLastVisibleRow)
        {
            return;
        }

        CommitActiveCellEdit(true);
        TableCanvas.Children.Clear();
        _renderedFirstVisibleRow = firstVisibleRow;
        _renderedLastVisibleRow = lastVisibleRow;

        if (_visibleRecordIndexes.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = _records.Count == 0 ? "No records loaded." : "No records match the active filters.",
                Margin = new Thickness(8, 12, 0, 0),
                Foreground = new SolidColorBrush(Colors.Gray)
            };
            Canvas.SetLeft(emptyText, 0);
            Canvas.SetTop(emptyText, 12);
            TableCanvas.Children.Add(emptyText);
            return;
        }

        for (var rowIndex = firstVisibleRow; rowIndex <= lastVisibleRow; rowIndex++)
        {
            var gridRow = rowIndex;
            var recordIndex = _visibleRecordIndexes[rowIndex];
            AddCanvasCell(CreateRowNumberCell(_records[recordIndex].LineNumber), gridRow, 0);

            for (var fieldIndex = 0; fieldIndex < _schema.Fields.Count; fieldIndex++)
            {
                AddCanvasCell(CreateValueCell(recordIndex, fieldIndex), gridRow, fieldIndex + 1);
            }
        }
    }

    private (int FirstVisibleRow, int LastVisibleRow) GetVisibleRecordRange()
    {
        if (_visibleRecordIndexes.Count == 0)
        {
            return (-1, -1);
        }

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(TableScrollViewer.VerticalOffset / RowHeight) - RowBuffer);
        var visibleRowCount = (int)Math.Ceiling(TableScrollViewer.ViewportHeight / RowHeight) + RowBuffer * 2 + 1;
        var lastVisibleRow = Math.Min(_visibleRecordIndexes.Count - 1, firstVisibleRow + visibleRowCount - 1);
        return (firstVisibleRow, lastVisibleRow);
    }

    private void RenderHeader()
    {
        HeaderCanvas.Children.Clear();
        if (_schema is null)
        {
            return;
        }

        AddHeaderCell(CreateHeaderCell("#", HorizontalAlignment.Left), 0);
        for (var fieldIndex = 0; fieldIndex < _schema.Fields.Count; fieldIndex++)
        {
            var field = _schema.Fields[fieldIndex];
            AddHeaderCell(CreateHeaderCell($"{field.DisplayName} ({field.Length})", fieldIndex), fieldIndex + 1);
        }
    }

    private void AddHeaderCell(FrameworkElement element, int column)
    {
        element.Width = _columnWidths[column];
        element.Height = RowHeight;
        Canvas.SetLeft(element, _columnLefts[column]);
        Canvas.SetTop(element, 0);
        HeaderCanvas.Children.Add(element);
    }

    private void AddCanvasCell(FrameworkElement element, int row, int column)
    {
        element.Width = _columnWidths[column];
        element.Height = RowHeight;
        Canvas.SetLeft(element, _columnLefts[column]);
        Canvas.SetTop(element, row * RowHeight);
        TableCanvas.Children.Add(element);
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

    private Border CreateHeaderCell(string text, int fieldIndex)
    {
        var border = CreateTableBorder(new SolidColorBrush(Colors.WhiteSmoke));
        var panel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var label = new TextBlock
        {
            Text = text,
            Padding = new Thickness(8, 6, 4, 6),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        panel.Children.Add(label);

        var filterButton = new Button
        {
            Content = _filters.ContainsKey(fieldIndex) ? "Filter*" : "Filter",
            Tag = fieldIndex,
            MinWidth = 54,
            Height = 28,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        filterButton.Click += HeaderFilterButton_Click;
        Grid.SetColumn(filterButton, 1);
        panel.Children.Add(filterButton);

        border.Child = panel;
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
            MinHeight = RowHeight
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
                ApplyFilters();
            }

            cell.Child = CreateValueTextBlock(rowIndex, fieldIndex);
        }
    }

    private async void HeaderFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_schema is null || sender is not Button { Tag: int fieldIndex })
        {
            return;
        }

        var field = _schema.Fields[fieldIndex];
        var filterInput = new TextBox
        {
            Text = _filters.TryGetValue(fieldIndex, out var currentFilter) ? currentFilter : "",
            MinWidth = 320,
            PlaceholderText = "Contains text"
        };

        var dialog = new ContentDialog
        {
            Title = $"Filter: {field.DisplayName}",
            Content = filterInput,
            PrimaryButtonText = "Apply",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var filter = filterInput.Text.Trim();
            if (filter.Length == 0)
            {
                _filters.Remove(fieldIndex);
            }
            else
            {
                _filters[fieldIndex] = filter;
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _filters.Remove(fieldIndex);
        }
        else
        {
            return;
        }

        ApplyFilters();
        TableScrollViewer.ChangeView(TableScrollViewer.HorizontalOffset, 0, null, true);
        RenderTable();
        SetFilterStatus();
    }

    private void ApplyFilters()
    {
        _visibleRecordIndexes.Clear();
        if (_records.Count == 0)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < _records.Count; rowIndex++)
        {
            if (RecordMatchesFilters(_records[rowIndex]))
            {
                _visibleRecordIndexes.Add(rowIndex);
            }
        }
    }

    private bool RecordMatchesFilters(FixedRecord record)
    {
        foreach (var (fieldIndex, filter) in _filters)
        {
            var value = fieldIndex < record.Values.Length ? record.Values[fieldIndex] : "";
            if (value.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private void SetLoadedStatus(string path)
    {
        if (_filters.Count == 0)
        {
            SetStatus($"Loaded {_records.Count:N0} records from {Path.GetFileName(path)}.");
            return;
        }

        SetFilterStatus();
    }

    private void SetFilterStatus()
    {
        SetStatus($"Showing {_visibleRecordIndexes.Count:N0} of {_records.Count:N0} records with {_filters.Count:N0} active filters.");
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
