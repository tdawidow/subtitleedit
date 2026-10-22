using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Nikse.SubtitleEdit.Logic;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

// The batch queue uses exactly the options understood by the existing single-file mixer.
internal sealed class LektorMixSettingsPanel : Grid
{
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Dynamiczne ściszanie podczas mowy", "Stały poziom oryginału" } };
    private readonly ComboBox _codec = new() { ItemsSource = Enum.GetValues<LektorCodec>() };
    private readonly ComboBox _bitrate = new();
    private readonly NumericUpDown _original;
    private readonly NumericUpDown _narrator;
    private readonly NumericUpDown _threshold;
    private readonly NumericUpDown _ratio;
    private readonly NumericUpDown _attack;
    private readonly NumericUpDown _release;
    internal event Action? CodecChanged;

    internal LektorMixSettingsPanel(LektorExportOptions saved)
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*");
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto");
        ColumnSpacing = 12; RowSpacing = 6;
        _mode.SelectedIndex = saved.Mode == LektorMixMode.Fixed ? 1 : 0;
        _codec.SelectedItem = Enum.IsDefined(saved.Codec) ? saved.Codec : LektorCodec.Ac3;
        _original = Number(0, 200, saved.OriginalPercent);
        _narrator = Number(0, 200, saved.NarratorPercent);
        _threshold = Number(-60, 0, saved.ThresholdDb);
        _ratio = Number(1, 20, saved.Ratio);
        _attack = Number(0.01m, 2000, saved.AttackMs);
        _release = Number(0.01m, 9000, saved.ReleaseMs);
        UpdateBitrates(saved.BitrateKbps);
        _codec.SelectionChanged += (_, _) => { UpdateBitrates(null); CodecChanged?.Invoke(); };
        _mode.SelectionChanged += (_, _) => UpdateDynamic();
        UpdateDynamic();
        Add("Tryb miksowania", _mode, 0, 0); Add("Kodek wyniku", _codec, 0, 2);
        Add("Poziom oryginału (%)", _original, 1, 0); Add("Bitrate (kb/s)", _bitrate, 1, 2);
        Add("Poziom lektora (%)", _narrator, 2, 0); Add("Próg ściszania (dB)", _threshold, 2, 2);
        Add("Siła ściszania (ratio)", _ratio, 3, 0); Add("Czas ściszania (ms)", _attack, 3, 2);
        Add("Czas przywracania (ms)", _release, 4, 0);
    }

    private void Add(string label, Control control, int row, int column)
    {
        this.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, column);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        this.Add(control, row, column + 1);
    }

    private static NumericUpDown Number(decimal min, decimal max, double value) => new()
    {
        Minimum = min, Maximum = max, Value = double.IsFinite(value) ? (decimal)Math.Clamp(value, (double)min, (double)max) : min,
        Increment = 1, FormatString = "0.##", MinWidth = 100
    };

    private void UpdateBitrates(int? preferred)
    {
        var options = new LektorExportOptions { Codec = _codec.SelectedItem is LektorCodec codec ? codec : LektorCodec.Ac3 };
        _bitrate.ItemsSource = options.Bitrates; _bitrate.IsEnabled = options.Codec != LektorCodec.Flac;
        _bitrate.SelectedItem = preferred.HasValue && options.Bitrates.Contains(preferred.Value) ? preferred.Value :
            options.Codec == LektorCodec.Mp3 ? 320 : options.Codec == LektorCodec.Aac ? 256 : 384;
    }

    private void UpdateDynamic() => _threshold.IsEnabled = _ratio.IsEnabled = _attack.IsEnabled = _release.IsEnabled = _mode.SelectedIndex == 0;

    internal LektorExportOptions ReadOptions() => new()
    {
        Mode = _mode.SelectedIndex == 1 ? LektorMixMode.Fixed : LektorMixMode.Dynamic,
        Codec = _codec.SelectedItem is LektorCodec codec ? codec : LektorCodec.Ac3,
        BitrateKbps = _bitrate.SelectedItem is int bitrate ? bitrate : 0,
        OriginalPercent = (double)(_original.Value ?? 100), NarratorPercent = (double)(_narrator.Value ?? 100),
        ThresholdDb = (double)(_threshold.Value ?? -30), Ratio = (double)(_ratio.Value ?? 8),
        AttackMs = (double)(_attack.Value ?? 80), ReleaseMs = (double)(_release.Value ?? 500)
    };
}
