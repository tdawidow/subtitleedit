using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;
using Nikse.SubtitleEdit.Logic.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

internal sealed record NarratorJoinAnalysis(double FirstDuration, double SecondDuration, double OverlapSeconds,
    bool FirstMono, bool SecondMono)
{
    internal double Duration => Math.Max(FirstDuration, SecondDuration);
}

// Recovery outputs already contain their original leading/internal silence. Overlay them at
// time zero; concatenation, silence trimming and FixSpeed would destroy that synchronization.
internal static class NarratorJoinService
{
    internal static string SuggestedName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Trim().Trim('"'));
        var separator = name.IndexOf('_');
        if (separator > 0) name = name[..separator];
        return string.IsNullOrWhiteSpace(name) ? "Lektor_PL.wav" : name + ".wav";
    }

    internal static string OutputPath(string folder, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
            name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ||
            !name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Podaj samą nazwę wynikowego pliku z rozszerzeniem .wav.");
        return Path.Combine(BatchFileRules.FullPath(folder), name);
    }

    private static void ValidateInputs(string first, string second)
    {
        foreach (var file in new[] { first, second })
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Wskaż obie części lektora.", file);
            if (!new[] { ".wav", ".ac3" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Części lektora muszą być plikami WAV lub AC3.");
        }
        if (BatchFileRules.Paths.Equals(BatchFileRules.FullPath(first), BatchFileRules.FullPath(second)))
            throw new ArgumentException("Wskazano ten sam plik jako obie części lektora.");
    }

    internal static async Task<NarratorJoinAnalysis> AnalyzeAsync(string first, string second,
        CancellationToken token, Action<string>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        ValidateInputs(first, second);
        var durations = new List<double>();
        var activity = new List<List<(double Start, double End)>>();
        var mono = new List<bool>();
        foreach (var file in new[] { first, second })
        {
            progress?.Invoke($"Sprawdzanie części {durations.Count + 1}/2: {Path.GetFileName(file)}…");
            var info = await LektorMediaProbe.ReadAsync(file, token);
            if (info.Tracks.Count != 1 || info.Tracks[0].Type != "Audio" ||
                !double.IsFinite(info.Duration) || info.Duration <= 0)
                throw new InvalidDataException("Część lektora musi zawierać jedną ścieżkę audio o znanej długości: " + file);
            durations.Add(info.Duration);
            var isMono = Regex.IsMatch(info.Tracks[0].Info, @",\s*(mono|1 channels?)(,|$)");
            if (!isMono && !Regex.IsMatch(info.Tracks[0].Info, @",\s*(stereo|2 channels?)(,|$)"))
                throw new InvalidDataException("Część lektora musi być mono lub stereo: " + file);
            mono.Add(isMono);
            var log = await RunAsync(["-nostdin", "-hide_banner", "-nostats", "-i", file, "-map", "0:a:0",
                "-af", "silencedetect=noise=-45dB:d=0.15", "-f", "null", "-"], token);
            var intervals = ParseActivity(log, info.Duration);
            if (intervals.Count == 0)
                throw new InvalidDataException("Nie wykryto aktywnego audio w części lektora: " + file);
            activity.Add(intervals);
        }
        var overlap = Overlap(activity[0], activity[1]);
        if (overlap > 0.2)
            throw new InvalidDataException($"W obu częściach wykryto jednoczesne audio ({overlap:0.###} s). " +
                "Sprawdź, czy są to uzupełniające się części tego samego lektora. Nie połączono plików.");
        return new NarratorJoinAnalysis(durations[0], durations[1], overlap, mono[0], mono[1]);
    }

    internal static List<(double Start, double End)> ParseActivity(string log, double duration)
    {
        var active = new List<(double, double)>();
        var cursor = 0.0;
        var inSilence = false;
        foreach (Match match in Regex.Matches(log, @"silence_(start|end):\s*([\d.eE+\-]+)"))
        {
            var time = Math.Clamp(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 0, duration);
            if (match.Groups[1].Value == "start")
            {
                if (!inSilence && time > cursor) active.Add((cursor, time));
                inSilence = true;
            }
            else { cursor = time; inSilence = false; }
        }
        if (!inSilence && cursor < duration) active.Add((cursor, duration));
        return active;
    }

    private static double Overlap(List<(double Start, double End)> a, List<(double Start, double End)> b)
    {
        var seconds = 0.0; var i = 0; var j = 0;
        while (i < a.Count && j < b.Count)
        {
            seconds += Math.Max(0, Math.Min(a[i].End, b[j].End) - Math.Max(a[i].Start, b[j].Start));
            if (a[i].End < b[j].End) i++; else j++;
        }
        return seconds;
    }

    internal static async Task<string> JoinAsync(string first, string second, string output,
        CancellationToken token, Action<string>? progress = null)
    {
        first = BatchFileRules.FullPath(first); second = BatchFileRules.FullPath(second);
        output = BatchFileRules.FullPath(output);
        if (!output.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Plik wynikowy musi mieć rozszerzenie WAV.");
        BatchFileRules.CheckDestination(Path.GetDirectoryName(output)!);
        BatchFileRules.CheckOutputs([output], [first, second]);
        var analysis = await AnalyzeAsync(first, second, token, progress);
        var temp = Path.Combine(Path.GetDirectoryName(output)!, ".lektor-join-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            progress?.Invoke("Łączenie części na wspólnej osi czasu od 00:00…");
            const string prepare = "aresample=48000:first_pts=0,aformat=sample_fmts=fltp:channel_layouts=stereo";
            // Duplicate mono explicitly; FFmpeg's default center-to-stereo downmix otherwise
            // lowers a mono narrator by 3 dB even though the other part is silent.
            var firstPrepare = (analysis.FirstMono ? "pan=stereo|c0=c0|c1=c0," : string.Empty) + prepare;
            var secondPrepare = (analysis.SecondMono ? "pan=stereo|c0=c0|c1=c0," : string.Empty) + prepare;
            var log = await RunAsync(["-nostdin", "-hide_banner", "-nostats", "-n", "-i", first, "-i", second,
                "-filter_complex", $"[0:a:0]{firstPrepare}[a];[1:a:0]{secondPrepare}[b];" +
                "[a][b]amix=inputs=2:duration=longest:normalize=0:dropout_transition=0," +
                "astats=measure_perchannel=none:measure_overall=Peak_level:reset=0[out]",
                "-map", "[out]", "-map_metadata", "-1", "-c:a", "pcm_s16le", "-ar", "48000", "-ac", "2",
                // Reserve RF64 only for outputs that need it. The standard RIFF header keeps
                // shorter WAVs compatible with the existing waveform reader as well.
                "-rf64", analysis.Duration * 192000 + 4096 >= uint.MaxValue ? "always" : "never", temp], token);
            // Measure the floating point sum before PCM conversion, so clipping is never hidden
            // by the output encoder. Do not fix it by quietly changing the narrator's gain.
            var peaks = Regex.Matches(log, @"Peak level dB:\s*(-?inf|[\d.eE+\-]+)");
            if (peaks.Count == 0) throw new InvalidDataException("Nie udało się sprawdzić poziomu połączonego audio.");
            foreach (Match peak in peaks)
                if (peak.Groups[1].Value != "-inf" &&
                    double.Parse(peak.Groups[1].Value, CultureInfo.InvariantCulture) > 0)
                    throw new InvalidDataException("Połączone części spowodowałyby przesterowanie. Sprawdź właściwy komplet plików.");
            progress?.Invoke("Kontrola wynikowego WAV…");
            var result = await LektorMediaProbe.ReadAsync(temp, token);
            if (result.Tracks.Count != 1 || result.Tracks[0].Type != "Audio" ||
                !result.Tracks[0].Info.StartsWith("pcm_s16le", StringComparison.Ordinal) ||
                Math.Abs(result.Duration - analysis.Duration) > 0.1)
                throw new InvalidDataException("Nieprawidłowy format lub długość połączonego WAV.");
            token.ThrowIfCancellationRequested();
            File.Move(temp, output, overwrite: false);
            return output;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken token)
    {
        var log = new StringBuilder();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) =>
        {
            if (e.Data != null) lock (log) log.AppendLine(e.Data);
        });
        process.StartInfo.Arguments = string.Empty;
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        await process.StartAndWaitAsync(token, TimeSpan.FromHours(12));
        if (process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg: " + log.ToString()[Math.Max(0, log.Length - 6000)..]);
        return log.ToString();
    }
}
