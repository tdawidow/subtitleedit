using System;
using System.IO;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

internal static class TtsAudioValidation
{
    public static bool IsUsable(string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName) || new FileInfo(fileName).Length == 0) return false;
            if (string.Equals(Path.GetExtension(fileName), ".mp3", StringComparison.OrdinalIgnoreCase))
                return Mp3DurationReader.TryGetDurationSeconds(fileName) > 0;
            if (string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(fileName);
                var header = new WaveHeader2(stream);
                return header.SampleRate > 0 && header.NumberOfChannels > 0 && header.DataChunkSize > 0 &&
                       stream.Length >= (long)header.DataStartPosition + header.DataChunkSize;
            }
            return FfmpegMediaInfo.Parse(fileName).Duration?.TotalMilliseconds > 0;
        }
        catch { return false; }
    }
}
