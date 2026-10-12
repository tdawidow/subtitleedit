using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

internal static class LektorExportService
{
    internal static string BuildFilter(int streamIndex, double seconds, LektorExportOptions options)
    {
        options.Validate();
        if (streamIndex < 0 || !double.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(streamIndex));
        static string F(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
        var prepare = $"aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=duration={F(seconds)}";
        var graph = $"[0:{streamIndex}]{prepare},volume={F(options.OriginalPercent / 100)}[orig];" +
            $"[1:a:0]{prepare},volume={F(options.NarratorPercent / 100)}[voice];";
        if (options.Mode == LektorMixMode.Dynamic)
        {
            graph += "[voice]asplit=2[side][speech];" +
                $"[orig][side]sidechaincompress=threshold={F(Math.Pow(10, options.ThresholdDb / 20))}" +
                $":ratio={F(options.Ratio)}:attack={F(options.AttackMs)}:release={F(options.ReleaseMs)}:makeup=1[bg];";
        }
        else graph += "[orig]anull[bg];[voice]anull[speech];";
        // Disable the limiter's automatic gain and compensate its look-ahead delay.
        return graph + "[bg][speech]amix=inputs=2:duration=longest:normalize=0:dropout_transition=0," +
            "alimiter=limit=0.891251:level=false:latency=true[out]";
    }

    internal static List<string> MixArguments(string video, string wave, string output, int streamIndex,
        double seconds, LektorExportOptions options)
    {
        var args = new List<string> { "-nostdin", "-hide_banner", "-y", "-i", video, "-i", wave,
            "-filter_complex", BuildFilter(streamIndex, seconds, options), "-map", "[out]",
            "-map_metadata", "-1", "-map_chapters", "-1",
            "-c:a", options.Encoder, "-ar", "48000", "-ac", "2" };
        if (options.Codec != LektorCodec.Flac) args.AddRange(["-b:a", options.BitrateKbps + "k"]);
        args.Add(output);
        return args;
    }

    internal static List<string> MuxArguments(string video, string mix, string output, LektorMediaProbe source)
    {
        var args = new List<string> { "-nostdin", "-hide_banner", "-y", "-fflags", "+genpts", "-i", video, "-i", mix };
        // Video first, new PL audio first among audio, then every other source stream.
        foreach (var track in source.Tracks.Where(t => t.Type == "Video")) args.AddRange(["-map", $"0:{track.Index}"]);
        args.AddRange(["-map", "1:a:0"]);
        foreach (var track in source.Tracks.Where(t => t.Type != "Video")) args.AddRange(["-map", $"0:{track.Index}"]);
        args.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c", "copy",
            "-disposition:a", "-default-forced", "-disposition:s", "-default-forced", "-disposition:a:0", "default",
            "-metadata:s:a:0", "language=pol", "-metadata:s:a:0", "title=Polski lektor PL", output]);
        // No -shortest: a short TTS file must never shorten the original video/audio.
        return args;
    }

    internal static async Task<string> ExportAsync(string video, string wave, string mix, string? mkv,
        int streamIndex, LektorExportOptions options, CancellationToken token, Action<string>? progress = null,
        bool useExistingMix = false)
    {
        options.Validate();
        GuardOutput(mix, video, wave, useExistingMix);
        if (mkv != null)
        {
            if (!mkv.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Plik wynikowy musi mieć rozszerzenie MKV.");
            GuardOutput(mkv, video, wave, false);
            if (SamePath(mkv, mix)) throw new ArgumentException("Miks i MKV muszą mieć różne nazwy.");
        }
        progress?.Invoke("Odczyt ścieżek źródłowych…");
        var source = await LektorMediaProbe.ReadAsync(video, token);
        if (!source.Tracks.Any(t => t.Type == "Video")) throw new ArgumentException("Plik źródłowy nie zawiera obrazu.");
        if (!source.Tracks.Any(t => t.Type == "Audio" && t.Index == streamIndex) && !useExistingMix)
            throw new ArgumentException("Wybrana ścieżka audio nie istnieje w pliku źródłowym.");

        if (!useExistingMix)
        {
            var narrator = await LektorMediaProbe.ReadAsync(wave, token);
            if (narrator.Tracks.Count(t => t.Type == "Audio") != 1)
                throw new ArgumentException("WAV lektora powinien zawierać jedną ścieżkę audio.");
            var seconds = Math.Max(source.Duration, narrator.Duration);
            if (seconds <= 0) throw new InvalidOperationException("Nieznana długość pliku źródłowego lub WAV.");
            var tempMix = TemporarySibling(mix);
            try
            {
                progress?.Invoke("Miksowanie i kodowanie audio…");
                await RunAsync(MixArguments(video, wave, tempMix, streamIndex, seconds, options), token);
                await VerifyMixAsync(tempMix, seconds, token);
                token.ThrowIfCancellationRequested();
                File.Move(tempMix, mix, true);
                Se.WriteToolsLog($"Lektor PL v4: saved mix \"{mix}\"");
            }
            finally { DeleteTemporary(tempMix); }
        }
        if (mkv == null) return mix;

        var mixInfo = await LektorMediaProbe.ReadAsync(mix, token);
        if (mixInfo.Tracks.Count != 1 || mixInfo.Tracks[0].Type != "Audio")
            throw new ArgumentException("Miks musi zawierać dokładnie jedną ścieżkę audio.");
        var tempMkv = TemporarySibling(mkv);
        try
        {
            progress?.Invoke("Tworzenie MKV…");
            await RunAsync(MuxArguments(video, mix, tempMkv, source), token);
            progress?.Invoke("Kontrola ścieżek, flag i długości…");
            var output = await LektorMediaProbe.ReadAsync(tempMkv, token);
            ValidateTracks(source, mixInfo, output);
            var before = await LektorMediaProbe.AuditAsync(video, token);
            var after = await LektorMediaProbe.AuditAsync(tempMkv, token);
            var videos = source.Tracks.Count(t => t.Type == "Video");
            if (after.Count != before.Count + 1) throw new InvalidOperationException("Brak oczekiwanych ścieżek audio/wideo.");
            for (var i = 0; i < before.Count; i++)
            {
                var copied = after[i < videos ? i : i + 1];
                if (copied.Count != before[i].Count || Math.Abs(copied.Seconds - before[i].Seconds) > 0.25)
                    throw new InvalidOperationException("Oryginalna ścieżka audio/wideo została skrócona lub zmieniona.");
            }
            if (after[videos].Count == 0 || Math.Abs(after[videos].Seconds - mixInfo.Duration) > 0.5)
                throw new InvalidOperationException("Nieprawidłowa długość dodanego lektora PL.");
            token.ThrowIfCancellationRequested();
            File.Move(tempMkv, mkv, true);
            Se.WriteToolsLog($"Lektor PL v4: verified and saved MKV \"{mkv}\"");
            return mkv;
        }
        finally { DeleteTemporary(tempMkv); }
    }

    internal static void ValidateTracks(LektorMediaProbe source, LektorMediaProbe mix, LektorMediaProbe output)
    {
        var expected = source.Tracks.Where(t => t.Type == "Video").Concat(mix.Tracks)
            .Concat(source.Tracks.Where(t => t.Type != "Video")).ToList();
        if (expected.Count != output.Tracks.Count || source.Chapters != output.Chapters)
            throw new InvalidOperationException("Nie zachowano kompletu ścieżek lub rozdziałów.");
        for (var i = 0; i < expected.Count; i++)
        {
            if (expected[i].Type != output.Tracks[i].Type || CodecName(expected[i]) != CodecName(output.Tracks[i]))
                throw new InvalidOperationException("Nie zachowano typu lub kodeka jednej ze ścieżek.");
        }
        var audio = output.Tracks.Where(t => t.Type == "Audio").ToList();
        if (audio.Count == 0 || audio[0].Language != "pol" || !audio[0].Default || audio[0].Forced ||
            audio.Skip(1).Any(t => t.Default || t.Forced) || output.Tracks.Any(t => t.Type == "Subtitle" && (t.Default || t.Forced)))
            throw new InvalidOperationException("Nieprawidłowe flagi domyślnej ścieżki PL lub napisów.");
    }

    private static string CodecName(LektorTrack track) => track.Info.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)[0];

    private static async Task VerifyMixAsync(string file, double seconds, CancellationToken token)
    {
        var info = await LektorMediaProbe.ReadAsync(file, token);
        if (info.Tracks.Count != 1 || info.Tracks[0].Type != "Audio" || Math.Abs(info.Duration - seconds) > 0.5)
            throw new InvalidOperationException($"Nieprawidłowy wynik miksowania audio: ścieżki={info.Tracks.Count}, czas={info.Duration:0.###} s, oczekiwano {seconds:0.###} s.");
    }

    private static async Task RunAsync(IEnumerable<string> args, CancellationToken token)
    {
        var tail = new FfmpegOutputTail();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) tail.Add(e.Data);
        });
        process.StartInfo.Arguments = string.Empty;
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        await process.StartAndWaitAsync(token, TimeSpan.FromHours(12));
        if (process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg zakończył się błędem " + process.ExitCode + ":\n" + tail);
    }

    internal static bool SamePath(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void GuardOutput(string output, string video, string wave, bool existing)
    {
        if (SamePath(output, video) || SamePath(output, wave))
            throw new ArgumentException("Plik wynikowy nie może zastąpić źródła ani WAV lektora.");
        if (existing && !File.Exists(output)) throw new FileNotFoundException("Nie znaleziono miksu.", output);
    }

    private static string TemporarySibling(string file) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!,
        "." + Path.GetFileNameWithoutExtension(file) + ".partial-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file));

    private static void DeleteTemporary(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Se.WriteToolsLog("Lektor PL: could not remove temporary file: " + ex.Message, true); }
    }
}
