using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

public sealed class BatchMuxWindow : Window
{
    internal BatchMuxTable Table { get; } = new();
    private readonly IFileHelper _files;
    private readonly IFolderHelper _folders;
    private readonly StackPanel _sources = new() { Spacing = 6 };
    private readonly ListBox _list;
    private readonly TextBox _destination = new() { Name = "BatchMkvOutputFolder", PlaceholderText = "Jeden katalog dla wszystkich wynikowych MKV" };
    private readonly TextBlock _status = new() { Height = 52, TextWrapping = TextWrapping.Wrap };
    private readonly Button _start = new() { Name = "StartBatchMux", Content = "Uruchom muksowanie kolejki" };
    private readonly Button _check = new() { Content = "Sprawdź całą tabelę" };
    private readonly Button _stop = new() { Content = "Zamknij" };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private CancellationTokenSource? _run;
    private bool _busy;
    private (MuxColumn Column, int Row)? _drag;

    public BatchMuxWindow(IFileHelper files, IFolderHelper? folders = null)
    {
        _files = files; _folders = folders ?? new FolderHelper();
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "Lektor PL 5.1 — masowe muksowanie MKV";
        Width = 1400; Height = 900; MinWidth = 900; MinHeight = 600; WindowState = WindowState.Maximized;
        foreach (var column in Enum.GetValues<MuxColumn>()) _sources.Children.Add(SourceRow(column));
        var output = new Grid { ColumnDefinitions = new ColumnDefinitions("185,*,Auto"), ColumnSpacing = 8 };
        output.Add(new TextBlock { Text = "Katalog wynikowy MKV", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        output.Add(_destination, 0, 1);
        var pick = new Button { Content = "Wybierz…" };
        pick.Click += async (_, _) => { var folder = await _folders.PickFolderAsync(this, "Wspólny katalog MKV", _destination.Text); if (!string.IsNullOrEmpty(folder)) _destination.Text = folder; };
        output.Add(pick, 0, 2); _sources.Children.Add(output);
        _destination.TextChanged += (_, _) =>
        {
            if (_busy) return;
            Table.OutputFolder = _destination.Text?.Trim().Trim('"') ?? string.Empty; Table.Rebuild();
        };
        var tableActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var removeRow = new Button { Content = "Usuń zaznaczony wiersz" };
        removeRow.Click += (_, _) => { if (!_busy && _list?.SelectedItem is BatchMuxRow selected) Table.RemoveRow(selected.Number - 1); };
        var clear = new Button { Content = "Wyczyść tabelę" };
        clear.Click += (_, _) => { if (!_busy) Table.Clear(); };
        tableActions.Children.Add(removeRow); tableActions.Children.Add(clear); _sources.Children.Add(tableActions);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("45,3*,3*,3*,3*,2*"), ColumnSpacing = 4 };
        var labels = new[] { "Lp.", "Wideo", "Miks PL (gotowe audio)", "Wymuszone napisy PL (opcjonalnie)", "Wynikowy MKV", "Stan" };
        for (var i = 0; i < labels.Length; i++) header.Add(new TextBlock { Text = labels[i], FontWeight = FontWeight.SemiBold }, 0, i);
        _list = new ListBox { Name = "BatchMuxTable", ItemsSource = Table.Rows, ItemTemplate = new FuncDataTemplate<BatchMuxRow>((row, _) => MakeRow(row!)) };
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
        table.Add(header, 0, 0); table.Add(_list, 1, 0);
        _start.Classes.Add("accent");
        _check.Click += async (_, _) => await RunAsync(false);
        _start.Click += async (_, _) => await RunAsync(true);
        _stop.Click += (_, _) => { if (_busy) _run?.Cancel(); else Close(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { _check, _stop, _start } };
        var footer = new StackPanel { Spacing = 8, Children = { _status, _progress, actions } };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Add(_sources, 0, 0);
        layout.Add(new TextBlock
        {
            Text = "Każda kolumna jest wczytywana alfabetycznie. Pliki łączymy WYŁĄCZNIE według wierszy. Przeciągnij komórkę na inną w tej samej kolumnie, aby zamienić je miejscami. Prawy przycisk: wstaw pustą / usuń i przesuń / wybierz plik. Pusta komórka SRT oznacza brak dodatkowych napisów.", TextWrapping = TextWrapping.Wrap
        }, 1, 0);
        layout.Add(table, 2, 0); layout.Add(footer, 3, 0); Content = layout;
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _run?.Cancel(); } };
    }

    private Control SourceRow(MuxColumn column)
    {
        var title = column == MuxColumn.Video ? "Katalog wideo" : column == MuxColumn.Audio ? "Katalog miksów PL" : "Katalog wymuszonych SRT";
        var text = new TextBox { Name = "BatchSource" + column, PlaceholderText = title };
        var choose = new Button { Content = "Wybierz katalog…" };
        var load = new Button { Content = "Wczytaj" };
        void Load()
        {
            try
            {
                if (_busy) return;
                Table.LoadFolder(column, text.Text ?? string.Empty);
                _status.Text = "Wczytano kolumnę: " + title + ". Sprawdź zgodność wierszy przed uruchomieniem.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        choose.Click += async (_, _) =>
        {
            var folder = await _folders.PickFolderAsync(this, title, text.Text);
            if (!string.IsNullOrWhiteSpace(folder)) { text.Text = folder; Load(); }
        };
        load.Click += (_, _) => Load();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("185,*,Auto,Auto"), ColumnSpacing = 8 };
        grid.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        grid.Add(text, 0, 1); grid.Add(choose, 0, 2); grid.Add(load, 0, 3); return grid;
    }

    private Control MakeRow(BatchMuxRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("45,3*,3*,3*,3*,2*"), ColumnSpacing = 4, Height = 42, DataContext = row };
        grid.Add(new TextBlock { Text = row.Number.ToString(), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        grid.Add(MakeCell(row, MuxColumn.Video, row.Video), 0, 1);
        grid.Add(MakeCell(row, MuxColumn.Audio, row.Audio), 0, 2);
        grid.Add(MakeCell(row, MuxColumn.Subtitles, row.Subtitles), 0, 3);
        var result = new TextBlock { Text = Path.GetFileName(row.Output), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(result, row.Output); grid.Add(result, 0, 4);
        var status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        status.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(row.Status)));
        status.Bind(ToolTip.TipProperty, new Avalonia.Data.Binding(nameof(row.Status))); grid.Add(status, 0, 5);
        return grid;
    }

    private Control MakeCell(BatchMuxRow row, MuxColumn column, string path)
    {
        var index = row.Number - 1;
        var cell = new Border
        {
            Padding = new Thickness(6), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Gray,
            Background = string.IsNullOrEmpty(path) && column != MuxColumn.Subtitles ? new SolidColorBrush(Color.FromArgb(35, 220, 50, 50)) : Brushes.Transparent,
            Child = new TextBlock { Text = string.IsNullOrEmpty(path) ? "(pusto)" : Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(cell, string.IsNullOrEmpty(path) ? "Pusta komórka — prawy przycisk, aby wybrać plik" : path);
        var menu = new ContextMenu();
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => { if (!_busy) action(); }; menu.Items.Add(item); }
        var select = new MenuItem { Header = "Wybierz plik…" };
        select.Click += async (_, _) => { if (!_busy) await PickCell(column, index); }; menu.Items.Add(select);
        var paste = new MenuItem { Header = "Wklej ścieżkę" };
        paste.Click += async (_, _) =>
        {
            if (_busy || Clipboard == null) return;
            try { var value = await ClipboardHelper.GetTextAsync(this); if (value != null) Table.SetCell(column, index, value); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        menu.Items.Add(paste);
        Add("Wyczyść komórkę (bez przesuwania)", () => Table.SetCell(column, index, string.Empty));
        Add("Wstaw pustą komórkę — przesuń tę kolumnę w dół", () => Table.InsertEmpty(column, index));
        Add("Usuń komórkę — podnieś tę kolumnę", () => Table.DeleteCell(column, index));
        Add("Zamień z komórką wyżej", () => { if (index > 0) Table.Swap(column, index, index - 1); });
        Add("Zamień z komórką niżej", () => { if (index + 1 < Table.Rows.Count) Table.Swap(column, index, index + 1); });
        cell.ContextMenu = menu;
        cell.DoubleTapped += async (_, _) => { if (!_busy) await PickCell(column, index); };
        PointerPressedEventArgs? press = null; Point origin = default;
        cell.PointerPressed += (_, e) =>
        {
            if (_busy || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            _list.SelectedItem = row;
            press = e; origin = e.GetPosition(this); e.Pointer.Capture(cell); e.Handled = true;
        };
        cell.PointerReleased += (_, e) => { press = null; e.Pointer.Capture(null); };
        cell.PointerCaptureLost += (_, _) => press = null;
        cell.PointerMoved += async (_, e) =>
        {
            if (_busy || press == null || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed || Math.Abs(e.GetPosition(this).X - origin.X) + Math.Abs(e.GetPosition(this).Y - origin.Y) < 6) return;
            var initial = press; press = null; initial.Pointer.Capture(null); _drag = (column, index);
            try
            {
                var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateText("Lektor PL cell"));
                await DragDrop.DoDragDropAsync(initial, transfer, DragDropEffects.Move);
            }
            catch (Exception ex) { _status.Text = "Nie udało się przeciągnąć. Użyj zamiany komórek w menu: " + ex.Message; }
            finally { _drag = null; }
        };
        DragDrop.SetAllowDrop(cell, true);
        cell.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = !_busy && _drag?.Column == column ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; });
        cell.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!_busy && _drag is { } source && source.Column == column) Table.Swap(column, source.Row, index);
            e.Handled = true;
        });
        return cell;
    }

    private async Task PickCell(MuxColumn column, int index)
    {
        try
        {
            var path = column == MuxColumn.Video ? await _files.PickOpenVideoFile(this, "Wideo")
                : await _files.PickOpenFile(this, column == MuxColumn.Audio ? "Gotowy Miks PL" : "Wymuszone SRT PL", column == MuxColumn.Audio ? "Audio" : "SubRip", column == MuxColumn.Audio ? "*" : "*.srt");
            if (!string.IsNullOrWhiteSpace(path)) Table.SetCell(column, index, path);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    internal async Task RunAsync(bool export)
    {
        if (_busy) return;
        _busy = true; _run = new CancellationTokenSource();
        _sources.IsEnabled = false; _list.IsEnabled = false; _start.IsEnabled = false; _check.IsEnabled = false;
        _stop.Content = "Stop"; _progress.IsVisible = true;
        try
        {
            _status.Text = "Kontrola całej tabeli przed startem…";
            if (export)
            {
                await Table.RunAsync(_run.Token, text => _status.Text = text);
                _status.Text = $"Zakończono: {Table.Rows.Count} plików MKV. Okno jest gotowe do kolejnego zestawu.";
            }
            else { await Table.PreflightAsync(_run.Token); _status.Text = "Wszystkie pliki sprawdzone. Sprawdź powiązania wierszy i uruchom muksowanie."; }
        }
        catch (OperationCanceledException) { _status.Text = "Zatrzymano kolejkę. Zakończone MKV pozostają w katalogu wynikowym."; }
        catch (Exception ex) { _status.Text = "Zatrzymano: " + ex.Message; }
        finally
        {
            _run.Dispose(); _run = null; _busy = false;
            _sources.IsEnabled = true; _list.IsEnabled = true; _start.IsEnabled = true; _check.IsEnabled = true;
            _stop.Content = "Zamknij"; _progress.IsVisible = false;
        }
    }
}
