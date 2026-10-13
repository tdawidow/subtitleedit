using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

// A self-contained export dialog can also work on an existing WAV without loading a TTS engine.
public sealed class LektorExportWindow : Window
{
    private readonly IFileHelper _files;
    private readonly TextBox _video = new() { IsReadOnly = true };
    private readonly TextBox _wave = new() { IsReadOnly = true };
    private readonly TextBox _mkvmerge = new() { IsReadOnly = true };
    private readonly ComboBox _track = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Dynamiczne ściszanie podczas mowy", "Stały poziom oryginału" } };
    private readonly ComboBox _codec = new() { ItemsSource = Enum.GetValues<LektorCodec>() };
    private readonly ComboBox _bitrate = new();
    private readonly NumericUpDown _original;
    private readonly NumericUpDown _narrator;
    private readonly NumericUpDown _threshold;
    private readonly NumericUpDown _ratio;
    private readonly NumericUpDown _attack;
    private readonly NumericUpDown _release;
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _inputs = new() { Spacing = 10 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Button _cancel = new() { Content = "Zamknij" };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private CancellationTokenSource? _run;
    private bool _busy;

    public LektorExportWindow(IFileHelper files, string video, string wave)
    {
        _files = files;
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "Lektor PL — wersja 4.1.1";
        Width = 820; Height = 730; MinWidth = 600; MinHeight = 500;
        _video.Text = video; _wave.Text = wave;
        try { _mkvmerge.Text = LektorMkvMuxer.ResolveExecutable(); }
        catch (FileNotFoundException) { _mkvmerge.PlaceholderText = "Wybierz mkvmerge z MKVToolNix (tylko dla MKV)"; }
        var saved = Se.Settings.Video.TextToSpeech.LektorExport ?? new LektorExportOptions();
        _mode.SelectedIndex = saved.Mode == LektorMixMode.Fixed ? 1 : 0;
        _codec.SelectedItem = Enum.IsDefined(saved.Codec) ? saved.Codec : LektorCodec.Ac3;
        _original = Number(0, 200, saved.OriginalPercent);
        _narrator = Number(0, 200, saved.NarratorPercent);
        _threshold = Number(-60, 0, saved.ThresholdDb);
        _ratio = Number(1, 20, saved.Ratio);
        _attack = Number(1, 2000, saved.AttackMs);
        _release = Number(1, 9000, saved.ReleaseMs);
        UpdateBitrates(saved.BitrateKbps);
        _codec.SelectionChanged += (_, _) => UpdateBitrates(null);
        _mode.SelectionChanged += (_, _) => UpdateDynamicControls();
        UpdateDynamicControls();

        _inputs.Children.Add(new TextBlock
        {
            Text = "Miks stereo 48 kHz. WAV lektora pozostaje zapisany. MKV zachowuje oryginalny obraz, audio, napisy, rozdziały i załączniki. Polski lektor będzie pierwszym i domyślnym audio; napisy bez automatycznego wyboru.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _inputs.Children.Add(FileRow("Źródłowe wideo", _video, async () =>
        {
            var file = await _files.PickOpenVideoFile(this, "Wybierz źródłowe wideo");
            if (!string.IsNullOrEmpty(file)) { _video.Text = file; await LoadTracks(); }
        }));
        _inputs.Children.Add(FileRow("WAV lektora PL", _wave, async () =>
        {
            var file = await _files.PickOpenFile(this, "Wybierz zsynchronizowany WAV", "WAV", "*.wav");
            if (!string.IsNullOrEmpty(file)) _wave.Text = file;
        }));
        _inputs.Children.Add(FileRow("MKVToolNix / mkvmerge", _mkvmerge, async () =>
        {
            var file = await _files.PickOpenFile(this, "Wybierz mkvmerge z MKVToolNix", "mkvmerge", OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge");
            if (!string.IsNullOrEmpty(file))
            {
                _mkvmerge.Text = file;
                Se.Settings.Video.TextToSpeech.LektorMkvmergePath = file;
                Se.SaveSettings();
            }
        }));
        AddRow("Audio do miksowania", _track);
        AddRow("Tryb miksowania", _mode);
        AddRow("Kodek wynikowy", _codec);
        AddRow("Bitrate (kb/s)", _bitrate);
        AddRow("Poziom oryginału (%)", _original);
        AddRow("Poziom lektora (%)", _narrator);
        AddRow("Próg ściszania (dB)", _threshold);
        AddRow("Siła ściszania (ratio)", _ratio);
        AddRow("Czas ściszania (ms)", _attack);
        AddRow("Czas przywracania (ms)", _release);
        _actions.Children.Add(Action("Miks + MKV", () => Export(true, false)));
        _actions.Children.Add(Action("Tylko miks", () => Export(false, false)));
        _actions.Children.Add(Action("MKV z istniejącego miksu…", () => Export(true, true)));
        _cancel.Click += (_, _) => { if (_busy) _run?.Cancel(); else Close(); };
        var bottom = new StackPanel { Spacing = 10 };
        bottom.Children.Add(_status); bottom.Children.Add(_progress); bottom.Children.Add(_actions); bottom.Children.Add(_cancel);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Avalonia.Thickness(18), RowSpacing = 12 };
        layout.Children.Add(new ScrollViewer { Content = _inputs });
        Grid.SetRow(bottom, 1); layout.Children.Add(bottom);
        Content = layout;
        Opened += async (_, _) => await LoadTracks();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _run?.Cancel(); } };
    }

    private static NumericUpDown Number(decimal min, decimal max, double value) => new()
    {
        Minimum = min, Maximum = max, Value = double.IsFinite(value) ? (decimal)Math.Clamp(value, (double)min, (double)max) : min,
        Increment = 1, FormatString = "0.##", MinWidth = 140
    };

    private void AddRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1); grid.Children.Add(input); _inputs.Children.Add(grid);
    }

    private static Control FileRow(string label, TextBox text, Func<Task> pick)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var browse = Action("Wybierz…", pick); Grid.SetColumn(browse, 2); grid.Children.Add(browse);
        return grid;
    }

    private static Button Action(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void UpdateBitrates(int? preferred)
    {
        var options = new LektorExportOptions { Codec = _codec.SelectedItem is LektorCodec codec ? codec : LektorCodec.Ac3 };
        _bitrate.ItemsSource = options.Bitrates;
        _bitrate.IsEnabled = options.Codec != LektorCodec.Flac;
        _bitrate.SelectedItem = preferred.HasValue && options.Bitrates.Contains(preferred.Value) ? preferred.Value :
            options.Codec == LektorCodec.Mp3 ? 320 : options.Codec == LektorCodec.Aac ? 256 : 384;
    }

    private void UpdateDynamicControls()
    {
        var enabled = _mode.SelectedIndex == 0;
        _threshold.IsEnabled = _ratio.IsEnabled = _attack.IsEnabled = _release.IsEnabled = enabled;
    }

    private async Task LoadTracks()
    {
        if (_busy) return;
        _track.ItemsSource = null;
        if (!File.Exists(_video.Text)) return;
        _inputs.IsEnabled = false; _actions.IsEnabled = false;
        try
        {
            _status.Text = "Odczyt ścieżek…";
            var probe = await LektorMediaProbe.ReadAsync(_video.Text!, CancellationToken.None);
            var audio = probe.Tracks.Where(t => t.Type == "Audio").ToList();
            _track.ItemsSource = audio; _track.SelectedIndex = audio.Count > 0 ? 0 : -1;
            _status.Text = $"Wideo: {probe.Tracks.Count(t => t.IsVideo)}; audio: {audio.Count}; napisy: {probe.Tracks.Count(t => t.Type == "Subtitle")}; załączniki: {probe.Tracks.Count(t => !t.IsMedia)}; rozdziały: {probe.Chapters}.";
        }
        catch (Exception ex) { _status.Text = "Nie można odczytać wideo: " + ex.Message; }
        finally { _inputs.IsEnabled = true; _actions.IsEnabled = true; }
    }

    private LektorExportOptions ReadOptions() => new()
    {
        Mode = _mode.SelectedIndex == 1 ? LektorMixMode.Fixed : LektorMixMode.Dynamic,
        Codec = (LektorCodec)_codec.SelectedItem!, BitrateKbps = _bitrate.SelectedItem is int bitrate ? bitrate : 0,
        OriginalPercent = (double)(_original.Value ?? 100), NarratorPercent = (double)(_narrator.Value ?? 100),
        ThresholdDb = (double)(_threshold.Value ?? -30), Ratio = (double)(_ratio.Value ?? 8),
        AttackMs = (double)(_attack.Value ?? 80), ReleaseMs = (double)(_release.Value ?? 500)
    };

    private async Task Export(bool makeMkv, bool existing)
    {
        if (_busy) return;
        // Set busy before file pickers: a double click must not start two exports.
        _busy = true; _inputs.IsEnabled = false; _actions.IsEnabled = false;
        _run = new CancellationTokenSource(); _cancel.Content = "Anuluj";
        try
        {
            var video = _video.Text ?? string.Empty; var wave = _wave.Text ?? string.Empty;
            if (!File.Exists(video)) throw new ArgumentException("Wybierz źródłowe wideo.");
            if (!existing && !File.Exists(wave)) throw new ArgumentException("Wybierz zapisany WAV lektora.");
            var options = ReadOptions(); options.Validate();
            var track = _track.SelectedItem as LektorTrack;
            if (!existing && track == null) throw new ArgumentException("Wybierz audio do miksowania.");
            string mix;
            if (existing)
            {
                mix = await _files.PickOpenFile(this, "Wybierz gotowy miks PL", "Audio", "*");
                if (string.IsNullOrEmpty(mix)) return;
            }
            else mix = string.Empty;
            string? mkv = null;
            if (makeMkv)
            {
                mkv = await _files.PickSaveFile(this, ".mkv", Path.GetFileNameWithoutExtension(video) + "_PL.mkv", "Zapisz MKV z lektorem PL");
                if (string.IsNullOrEmpty(mkv)) return;
                if (!existing)
                {
                    mix = Path.Combine(Path.GetDirectoryName(mkv)!, Path.GetFileNameWithoutExtension(mkv) + "_MIX_PL" + options.Extension);
                    if (File.Exists(mix) && await MessageBox.Show(this, "Miks już istnieje",
                        "Zastąpić zapisany miks?\n" + mix, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxResult.Yes) return;
                }
            }
            else
            {
                mix = await _files.PickSaveFile(this, options.Extension, Path.GetFileNameWithoutExtension(video) + "_MIX_PL" + options.Extension, "Zapisz miks PL");
                if (string.IsNullOrEmpty(mix)) return;
            }
            _run.Token.ThrowIfCancellationRequested();
            Se.Settings.Video.TextToSpeech.LektorExport = options; Se.SaveSettings();
            _progress.IsVisible = true;
            var output = await LektorExportService.ExportAsync(video, wave, mix, mkv, track?.Index ?? -1, options, _run.Token,
                text => Dispatcher.UIThread.Post(() => _status.Text = text), existing);
            _status.Text = "Zakończono i sprawdzono: " + output;
            await MessageBox.Show(this, "Lektor PL", "Plik zapisany i sprawdzony:\n" + output + "\n\nMiks audio:\n" + mix,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (makeMkv) { _busy = false; Close(output); }
        }
        catch (OperationCanceledException) { _status.Text = "Anulowano eksport. Zapisany WAV i ukończony miks pozostają dostępne."; }
        catch (Exception ex)
        {
            SeLogger.Error(ex, "Lektor PL v4 export failed");
            _status.Text = "Eksport nie został ukończony. Zapisany WAV i ukończony miks pozostają dostępne.";
            await MessageBox.Show(this, "Błąd eksportu Lektor PL", ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false; _run.Dispose(); _run = null;
            _inputs.IsEnabled = true; _actions.IsEnabled = true; _progress.IsVisible = false; _cancel.Content = "Zamknij";
        }
    }
}
