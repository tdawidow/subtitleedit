using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

public sealed class NarratorJoinWindow : Window
{
    private readonly IFileHelper _files;
    private readonly IFolderHelper _folders;
    private readonly TextBox _first = new() { Name = "NarratorPart1", PlaceholderText = "Pierwsza odzyskana część lektora WAV / AC3" };
    private readonly TextBox _second = new() { Name = "NarratorPart2", PlaceholderText = "Druga część lektora WAV / AC3" };
    private readonly TextBox _folder = new() { Name = "NarratorJoinFolder", PlaceholderText = "Katalog wynikowego WAV" };
    private readonly TextBox _name = new() { Name = "NarratorJoinName", Text = "Lektor_PL.wav" };
    private readonly TextBlock _output = new() { Name = "NarratorJoinOutput", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "NarratorJoinStatus", MinHeight = 70, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _inputs = new() { Spacing = 12 };
    private readonly Button _check = new() { Content = "Sprawdź części" };
    private readonly Button _start = new() { Name = "StartNarratorJoin", Content = "Połącz w jeden WAV" };
    private readonly Button _stop = new() { Content = "Zamknij" };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private CancellationTokenSource? _run;

    public NarratorJoinWindow(IFileHelper files, IFolderHelper? folders = null)
    {
        _files = files; _folders = folders ?? new FolderHelper();
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "Lektor PL 5.2 — połącz części lektora po przerwaniu TTS";
        Width = 1050; Height = 620; MinWidth = 700; MinHeight = 500;
        _inputs.Children.Add(new TextBlock
        {
            Text = "Wskaż dwie uzupełniające się części czystego lektora tego samego filmu. Każda musi zachowywać pierwotną oś czasu i ciszę od 00:00. Program zachowa pozycje wypowiedzi i poziom głosu. Łączenie nie zużywa znaków TTS.",
            TextWrapping = TextWrapping.Wrap
        });
        _inputs.Children.Add(FileRow("Lektor PL — część 1", _first, () => PickPartAsync(_first)));
        _inputs.Children.Add(FileRow("Lektor PL — część 2", _second, () => PickPartAsync(_second)));
        _inputs.Children.Add(FileRow("Katalog wynikowy", _folder, async () =>
        {
            var folder = await _folders.PickFolderAsync(this, "Katalog połączonego WAV", _folder.Text);
            if (!string.IsNullOrWhiteSpace(folder)) _folder.Text = folder;
        }));
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), ColumnSpacing = 10 };
        nameRow.Add(new TextBlock { Text = "Nazwa wynikowego WAV", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        nameRow.Add(_name, 0, 1); _inputs.Children.Add(nameRow);
        _inputs.Children.Add(_output);
        _inputs.Children.Add(new TextBlock
        {
            Text = "Długość wyniku odpowiada dłuższej części. Wskaż pliki zachowujące pełną oś czasu, a nie ręcznie przycięte fragmenty. Przed startem sprawdź, czy oba pliki dotyczą tego samego odcinka.",
            TextWrapping = TextWrapping.Wrap
        });
        _first.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _run != null) return;
            try { _name.Text = NarratorJoinService.SuggestedName(_first.Text ?? string.Empty); }
            catch (ArgumentException ex) { _status.Text = ex.Message; }
        };
        _name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateOutput(); };
        _folder.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateOutput(); };
        _check.Click += async (_, _) => await RunAsync(false);
        _start.Click += async (_, _) => await RunAsync(true);
        _start.Classes.Add("accent");
        _stop.Click += (_, _) => { if (_run != null) _run.Cancel(); else Close(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { _check, _stop, _start } };
        var footer = new StackPanel { Spacing = 10, Children = { _status, _progress, actions } };
        var layout = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
        layout.Add(new ScrollViewer { Content = _inputs }, 0, 0); layout.Add(footer, 1, 0); Content = layout;
        Closing += (_, e) => { if (_run != null) { e.Cancel = true; _run.Cancel(); } };
    }

    private Control FileRow(string label, TextBox text, Func<Task> pick)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,Auto"), ColumnSpacing = 10 };
        row.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        row.Add(text, 0, 1);
        var button = new Button { Content = "Wybierz…" };
        button.Click += async (_, _) =>
        {
            try { await pick(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        row.Add(button, 0, 2); return row;
    }

    private async Task PickPartAsync(TextBox field)
    {
        var selected = await _files.PickOpenFiles(this, "Wybierz część lektora", "Lektor WAV / AC3",
            ["*.wav", "*.ac3"], string.Empty, []);
        if (selected.Length > 0) field.Text = selected[0];
    }

    private void UpdateOutput()
    {
        try { _output.Text = "Wynik: " + NarratorJoinService.OutputPath(_folder.Text ?? string.Empty, _name.Text ?? string.Empty); }
        catch (ArgumentException) { _output.Text = "Wskaż katalog i nazwę wynikowego WAV."; }
    }

    internal async Task RunAsync(bool join)
    {
        if (_run != null) return;
        _run = new CancellationTokenSource();
        _inputs.IsEnabled = _start.IsEnabled = _check.IsEnabled = false;
        _stop.Content = "Stop"; _progress.IsVisible = true;
        try
        {
            var first = (_first.Text ?? string.Empty).Trim().Trim('"');
            var second = (_second.Text ?? string.Empty).Trim().Trim('"');
            if (join)
            {
                var output = NarratorJoinService.OutputPath(_folder.Text ?? string.Empty, _name.Text ?? string.Empty);
                await NarratorJoinService.JoinAsync(first, second, output, _run.Token, text => _status.Text = text);
                _status.Text = "Zapisano i sprawdzono: " + output + "\nOkno jest gotowe do połączenia kolejnych części.";
            }
            else
            {
                var analysis = await NarratorJoinService.AnalyzeAsync(first, second, _run.Token, text => _status.Text = text);
                _status.Text = $"Część 1: {TimeSpan.FromSeconds(analysis.FirstDuration):hh\\:mm\\:ss\\.fff}; " +
                    $"część 2: {TimeSpan.FromSeconds(analysis.SecondDuration):hh\\:mm\\:ss\\.fff}. " +
                    $"Wynik: {TimeSpan.FromSeconds(analysis.Duration):hh\\:mm\\:ss\\.fff}.\n" +
                    "Nie wykryto istotnego nakładania audio. Sprawdź komplet odcinka i wybierz Połącz.";
            }
        }
        catch (OperationCanceledException) { _status.Text = "Zatrzymano łączenie. Pliki źródłowe pozostały zachowane."; }
        catch (Exception ex) { _status.Text = "Nie ukończono: " + ex.Message; }
        finally
        {
            _run.Dispose(); _run = null;
            _inputs.IsEnabled = _start.IsEnabled = _check.IsEnabled = true;
            _stop.Content = "Zamknij"; _progress.IsVisible = false;
        }
    }
}
