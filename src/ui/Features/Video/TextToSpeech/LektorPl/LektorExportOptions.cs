using System;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

public enum LektorMixMode { Fixed, Dynamic }
public enum LektorCodec { Ac3, Aac, Mp3, Flac }

public sealed class LektorExportOptions
{
    public LektorMixMode Mode { get; set; } = LektorMixMode.Dynamic;
    public LektorCodec Codec { get; set; } = LektorCodec.Ac3;
    public int BitrateKbps { get; set; } = 384;
    public double OriginalPercent { get; set; } = 100;
    public double NarratorPercent { get; set; } = 100;
    public double ThresholdDb { get; set; } = -30;
    public double Ratio { get; set; } = 8;
    public double AttackMs { get; set; } = 80;
    public double ReleaseMs { get; set; } = 500;

    public string Extension => Codec switch
    {
        LektorCodec.Ac3 => ".ac3", LektorCodec.Aac => ".m4a",
        LektorCodec.Mp3 => ".mp3", LektorCodec.Flac => ".flac",
        _ => throw new ArgumentOutOfRangeException(nameof(Codec))
    };

    public string Encoder => Codec switch
    {
        LektorCodec.Ac3 => "ac3", LektorCodec.Aac => "aac",
        LektorCodec.Mp3 => "libmp3lame", LektorCodec.Flac => "flac",
        _ => throw new ArgumentOutOfRangeException(nameof(Codec))
    };

    public int[] Bitrates => Codec switch
    {
        LektorCodec.Ac3 => [192, 224, 256, 320, 384, 448, 512, 576, 640],
        LektorCodec.Aac => [128, 192, 256, 320, 384],
        LektorCodec.Mp3 => [128, 160, 192, 224, 256, 320],
        LektorCodec.Flac => [],
        _ => throw new ArgumentOutOfRangeException(nameof(Codec))
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Codec))
            throw new ArgumentException("Nieprawidłowy tryb miksowania lub kodek.");
        if (Codec != LektorCodec.Flac && Array.IndexOf(Bitrates, BitrateKbps) < 0)
            throw new ArgumentException("Bitrate nie jest obsługiwany przez wybrany kodek.");
        Check(OriginalPercent, 0, 200); Check(NarratorPercent, 0, 200);
        Check(ThresholdDb, -60, 0); Check(Ratio, 1, 20);
        Check(AttackMs, 0.01, 2000); Check(ReleaseMs, 0.01, 9000);
    }

    private static void Check(double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(nameof(value), "Nieprawidłowy parametr miksowania.");
    }
}
