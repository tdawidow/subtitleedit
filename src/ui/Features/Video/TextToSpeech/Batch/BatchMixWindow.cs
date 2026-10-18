using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

public sealed class BatchMixWindow : Window
{
    internal BatchMixTable Table { get; } = new();
    private readonly IFileHelper _files;
    private readonly IFolderHelper _folders;
    private readonly StackPanel _sources = new() { Spacing = 6 };
    private readonly LektorMixSettingsPanel _settings;
    private readonly ListBox _list;
    private readonly TextBox _destination = new() { Name = "BatchMixOutputFolder", PlaceholderText = "Jeden katalog dla wszystkich wynikowych miksów audio" };
    private readonly TextBlock _status = new() { Name = "BatchMixStatus", Height = 55, TextWrapping = TextWrapping.Wrap };
    private readonly Button _start = new() { Name = "StartBatchMix", Content = "Twórz kolejkę MIX PL" };
    private readonly Button _check = new() { Content = "Sprawdź całą tabelę" };
    private readonly Button _readTracks = new() { Content = "Odczytaj ścieżki audio filmów" };
    private readonly Button _stop = new() { Content = "Zamknij" };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private CancellationTokenSource? _run;
    private (MixColumn Column, int Row)? _drag;

    public BatchMixWindow(IFileHelper files, IFolderHelper? folders = null)
    {
        _files = files; _folders = folders ?? new FolderHelper();
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "Lektor PL 5.1 — masowe miksowanie audio MIX PL";
        Width = 1500; Height = 950; MinWidth = 1000; MinHeight = 650; WindowState = WindowState.Maximized;
        _settings = new LektorMixSettingsPanel(Se.Settings.Video.TextToSpeech.LektorExport ?? new LektorExportOptions());
        Table.Options = _settings.ReadOptions();
        _settings.CodecChanged += () => { if (_run == null) { Table.Options = _settings.ReadOptions(); Table.Rebuild(); } };
        foreach (var column in Enum.GetValues<MixColumn>()) _sources.Children.Add(SourceRow(column));
        var output = new Grid { ColumnDefinitions = new ColumnDefinitions("185,*,Auto"), ColumnSpacing = 8 };
        output.Add(new TextBlock { Text = "Katalog wynikowy MIX PL", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        output.Add(_destination, 0, 1);
        var pick = new Button { Content = "Wybierz…" };
        pick.Click += async (_, _) =>
        {
            try
            {
                var folder = await _folders.PickFolderAsync(this, "Wspólny katalog MIX PL", _destination.Text);
                if (!string.IsNullOrEmpty(folder)) _destination.Text = folder;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        output.Add(pick, 0, 2); _sources.Children.Add(output);
        _destination.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _run != null) return;
            Table.OutputFolder = _destination.Text?.Trim().Trim('"') ?? string.Empty; Table.Rebuild();
        };
        var tableActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var remove = new Button { Content = "Usuń zaznaczony wiersz" };
        remove.Click += (_, _) => { if (_run == null && _list?.SelectedItem is BatchMixRow row) Table.RemoveRow(row.Number - 1); };
        var clear = new Button { Content = "Wyczyść tabelę" };
        clear.Click += (_, _) => { if (_run == null) Table.Clear(); };
        _readTracks.Click += async (_, _) => await RunAsync(false, tracksOnly: true);
        tableActions.Children.Add(_readTracks); tableActions.Children.Add(remove); tableActions.Children.Add(clear);
        _sources.Children.Add(tableActions);
        _sources.Children.Add(new Expander
        {
            Header = "Wspólne ustawienia miksowania — działający mechanizm Tylko miks", IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, Content = _settings
        });
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("45,3*,3*,3*,3*,2*"), ColumnSpacing = 4 };
        var labels = new[] { "Lp.", "Wideo źródłowe", "Oryginalna ścieżka audio", "Czysty WAV lektora PL", "Wynikowy MIX PL", "Stan" };
        for (var i = 0; i < labels.Length; i++) header.Add(new TextBlock { Text = labels[i], FontWeight = FontWeight.SemiBold }, 0, i);
        _list = new ListBox { Name = "BatchMixTable", ItemsSource = Table.Rows,
            ItemTemplate = new FuncDataTemplate<BatchMixRow>((row, _) => row == null ? new Border() : MakeRow(row)) };
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
        table.Add(header, 0, 0); table.Add(_list, 1, 0);
        _start.Classes.Add("accent");
        _check.Click += async (_, _) => await RunAsync(false);
        _start.Click += async (_, _) => await RunAsync(true);
        _stop.Click += (_, _) => { if (_run != null) _run.Cancel(); else Close(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { _check, _stop, _start } };
        var footer = new StackPanel { Spacing = 8, Children = { _status, _progress, actions } };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 10 };
        layout.Add(new ScrollViewer { Content = _sources, MaxHeight = 420 }, 0, 0);
        layout.Add(new TextBlock
        {
            Text = "Kolumny wideo i WAV są wczytywane alfabetycznie i parowane według wierszy. Odczytaj ścieżki filmów i wybierz właściwe audio w każdym wierszu. Przeciągnij komórkę w tej samej kolumnie, aby zamienić pliki; prawy przycisk pozwala wstawić pustą komórkę lub usunąć i przesunąć. Wynikiem jest samo audio MIX PL.",
            TextWrapping = TextWrapping.Wrap
        }, 1, 0);
        layout.Add(table, 2, 0); layout.Add(footer, 3, 0); Content = layout;
        Closing += (_, e) => { if (_run != null) { e.Cancel = true; _run.Cancel(); } };
    }

    private Control SourceRow(MixColumn column)
    {
        var title = column == MixColumn.Video ? "Katalog wideo" : "Katalog WAV lektora PL";
        var text = new TextBox { Name = "BatchMixSource" + column, PlaceholderText = title };
        void Load()
        {
            if (_run != null) return;
            try { Table.LoadFolder(column, text.Text ?? string.Empty); _status.Text = "Wczytano: " + title + ". Sprawdź pary i odczytaj ścieżki audio."; }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        var choose = new Button { Content = "Wybierz katalog…" };
        choose.Click += async (_, _) =>
        {
            try
            {
                var folder = await _folders.PickFolderAsync(this, title, text.Text);
                if (!string.IsNullOrWhiteSpace(folder)) { text.Text = folder; Load(); }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        var load = new Button { Content = "Wczytaj" }; load.Click += (_, _) => Load();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("185,*,Auto,Auto"), ColumnSpacing = 8 };
        grid.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        grid.Add(text, 0, 1); grid.Add(choose, 0, 2); grid.Add(load, 0, 3); return grid;
    }

    private Control MakeRow(BatchMixRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("45,3*,3*,3*,3*,2*"), ColumnSpacing = 4, MinHeight = 44, DataContext = row };
        grid.Add(new TextBlock { Text = row.Number.ToString(), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        grid.Add(MakeCell(row, MixColumn.Video, row.Video), 0, 1);
        var track = new ComboBox { Name = "BatchMixAudioTrack", ItemsSource = row.AudioTracks,
            HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Odczytaj / wybierz audio" };
        track.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(row.SelectedTrack)) { Mode = BindingMode.TwoWay });
        track.Bind(ToolTip.TipProperty, new Binding(nameof(row.SelectedTrack))); grid.Add(track, 0, 2);
        grid.Add(MakeCell(row, MixColumn.Narrator, row.Narrator), 0, 3);
        var result = new TextBlock { Text = Path.GetFileName(row.Output), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(result, row.Output); grid.Add(result, 0, 4);
        var status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(row.Status)));
        status.Bind(ToolTip.TipProperty, new Binding(nameof(row.Status))); grid.Add(status, 0, 5);
        return grid;
    }

    private Control MakeCell(BatchMixRow row, MixColumn column, string path)
    {
        var index = row.Number - 1;
        var cell = new Border
        {
            Padding = new Thickness(6), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Gray,
            Background = string.IsNullOrEmpty(path) ? new SolidColorBrush(Color.FromArgb(35, 220, 50, 50)) : Brushes.Transparent,
            Child = new TextBlock { Text = string.IsNullOrEmpty(path) ? "(pusto)" : Path.GetFileName(path),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }
        };
        ToolTip.SetTip(cell, string.IsNullOrEmpty(path) ? "Pusta komórka — prawy przycisk, aby wybrać plik" : path);
        var menu = new ContextMenu();
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { if (_run == null) action(); }; menu.Items.Add(item);
        }
        var select = new MenuItem { Header = "Wybierz plik…" };
        select.Click += async (_, _) => { if (_run == null) await PickCell(column, index); }; menu.Items.Add(select);
        var paste = new MenuItem { Header = "Wklej ścieżkę" };
        paste.Click += async (_, _) =>
        {
            if (_run != null || Clipboard == null) return;
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
        cell.DoubleTapped += async (_, _) => { if (_run == null) await PickCell(column, index); };
        PointerPressedEventArgs? press = null; Point origin = default;
        cell.PointerPressed += (_, e) =>
        {
            if (_run != null || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            _list.SelectedItem = row; press = e; origin = e.GetPosition(this); e.Pointer.Capture(cell); e.Handled = true;
        };
        cell.PointerReleased += (_, e) => { press = null; e.Pointer.Capture(null); };
        cell.PointerCaptureLost += (_, _) => press = null;
        cell.PointerMoved += async (_, e) =>
        {
            if (_run != null || press == null || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed ||
                Math.Abs(e.GetPosition(this).X - origin.X) + Math.Abs(e.GetPosition(this).Y - origin.Y) < 6) return;
            var initial = press; press = null; initial.Pointer.Capture(null); _drag = (column, index);
            try
            {
                var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateText("Lektor PL MIX cell"));
                await DragDrop.DoDragDropAsync(initial, transfer, DragDropEffects.Move);
            }
            catch (Exception ex) { _status.Text = "Nie udało się przeciągnąć. Użyj zamiany komórek w menu: " + ex.Message; }
            finally { _drag = null; }
        };
        DragDrop.SetAllowDrop(cell, true);
        cell.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = _run == null && _drag?.Column == column ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
        });
        cell.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_run == null && _drag is { } source && source.Column == column) Table.Swap(column, source.Row, index);
            e.Handled = true;
        });
        return cell;
    }

    private async Task PickCell(MixColumn column, int index)
    {
        try
        {
            var path = column == MixColumn.Video ? await _files.PickOpenVideoFile(this, "Wideo z oryginalnym audio") :
                await _files.PickOpenFile(this, "Czysty WAV lektora PL", "WAV", "*.wav");
            if (!string.IsNullOrWhiteSpace(path)) Table.SetCell(column, index, path);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    internal async Task RunAsync(bool export, bool tracksOnly = false)
    {
        if (_run != null) return;
        _run = new CancellationTokenSource();
        _sources.IsEnabled = _list.IsEnabled = _start.IsEnabled = _check.IsEnabled = false;
        _stop.Content = "Stop"; _progress.IsVisible = true;
        try
        {
            Table.Options = _settings.ReadOptions(); Table.Rebuild();
            _status.Text = "Kontrola plików kolejki…";
            if (tracksOnly)
            {
                await Table.ReadTracksAsync(_run.Token, text => _status.Text = text);
                _status.Text = "Odczytano audio filmów. Przy wielu ścieżkach wybierz właściwą w każdym wierszu tabeli.";
            }
            else if (export)
            {
                Se.Settings.Video.TextToSpeech.LektorExport = Table.Options; Se.SaveSettings();
                await Table.RunAsync(_run.Token, text => _status.Text = text);
                _status.Text = $"Zakończono: {Table.Rows.Count} miksów PL. Okno jest gotowe do kolejnego zestawu.";
            }
            else
            {
                await Table.PreflightAsync(_run.Token, text => _status.Text = text);
                _status.Text = "Sprawdzono całą kolejkę. Pliki i wybrane ścieżki są gotowe do miksowania.";
            }
        }
        catch (OperationCanceledException) { _status.Text = "Zatrzymano kolejkę. Ukończone miksy pozostają w katalogu wynikowym."; }
        catch (Exception ex) { _status.Text = "Zatrzymano: " + ex.Message; }
        finally
        {
            _run.Dispose(); _run = null;
            _sources.IsEnabled = _list.IsEnabled = _start.IsEnabled = _check.IsEnabled = true;
            _stop.Content = "Zamknij"; _progress.IsVisible = false;
        }
    }
}
