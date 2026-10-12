using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

// Uses the installed FFmpeg, without adding a dependency on a separate ffprobe binary.
internal sealed record LektorTrack(int Index, string Type, string Info, string Language, bool Default, bool Forced)
{
    public string Title { get; set; } = string.Empty;
    public override string ToString() => $"#{Index} | {Language} | {Title} | {Info}";
}

internal sealed class LektorMediaProbe
{
    public List<LektorTrack> Tracks { get; } = [];
    public int Chapters { get; private set; }
    public double Duration { get; private set; }

    public static async Task<LektorMediaProbe> ReadAsync(string file, CancellationToken token)
    {
        var log = new StringBuilder();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) =>
        {
            if (e.Data != null) lock (log) log.AppendLine(e.Data);
        });
        process.StartInfo.Arguments = string.Empty;
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-i", file })
            process.StartInfo.ArgumentList.Add(arg);
        // The nonzero exit here is intentional: -i prints metadata without creating output.
        await process.StartAndWaitAsync(token, TimeSpan.FromSeconds(60));
        var result = Parse(log.ToString());
        if (result.Tracks.Count == 0)
            throw new InvalidOperationException("Nie można odczytać ścieżek pliku: " + file);
        return result;
    }

    internal static LektorMediaProbe Parse(string log)
    {
        var result = new LektorMediaProbe();
        LektorTrack? current = null;
        foreach (var line in log.Split('\n'))
        {
            var text = line.Trim();
            var stream = Regex.Match(text, @"^Stream #0:(\d+)(?:\[[^\]]+\])?(?:\(([^)]+)\))?: (Video|Audio|Subtitle|Attachment|Data): (.+)$");
            if (stream.Success)
            {
                current = new LektorTrack(int.Parse(stream.Groups[1].Value, CultureInfo.InvariantCulture),
                    stream.Groups[3].Value, stream.Groups[4].Value, stream.Groups[2].Value,
                    text.Contains("(default)", StringComparison.Ordinal), text.Contains("(forced)", StringComparison.Ordinal));
                result.Tracks.Add(current);
            }
            else if (text.StartsWith("Stream #", StringComparison.Ordinal))
                throw new InvalidOperationException("Nierozpoznana ścieżka źródłowa: " + text);
            else if (text.StartsWith("Chapter #", StringComparison.Ordinal)) result.Chapters++;
            else if (current != null && Regex.IsMatch(text, @"^title\s*:"))
                current.Title = text[(text.IndexOf(':') + 1)..].Trim();
            var duration = Regex.Match(text, @"^Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
            if (duration.Success)
                result.Duration = double.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
                    double.Parse(duration.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
                    double.Parse(duration.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        return result;
    }

    internal sealed class PacketExtent
    {
        public long Count { get; set; }
        public double First { get; set; } = double.PositiveInfinity;
        public double Last { get; set; } = double.NegativeInfinity;
        public double Seconds => Count == 0 ? 0 : Last - First;
    }

    // Scan compressed packets, never decode video. Per-stream counts expose a truncated
    // picture even when the container duration is extended by the new audio.
    internal static async Task<List<PacketExtent>> AuditAsync(string file, CancellationToken token)
    {
        var timeBases = new Dictionary<int, double>();
        var extents = new Dictionary<int, PacketExtent>();
        using var process = FfmpegGenerator.GetProcess(string.Empty, null);
        process.StartInfo.Arguments = string.Empty;
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-v", "error", "-i", file,
            "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f", "framecrc", "-" })
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        var errors = new FfmpegOutputTail();
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.Add(e.Data); };
        process.OutputDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line == null) return;
            if (line.StartsWith("#tb ", StringComparison.Ordinal))
            {
                var parts = line[4..].Split([':', '/']);
                if (parts.Length == 3 && int.TryParse(parts[0], out var index) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var numerator) &&
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var denominator) && denominator > 0)
                { timeBases[index] = numerator / denominator; extents[index] = new PacketExtent(); }
                return;
            }
            if (line.StartsWith('#')) return;
            var fields = line.Split(',');
            if (fields.Length >= 6 && int.TryParse(fields[0], out var stream) && timeBases.TryGetValue(stream, out var tb) &&
                long.TryParse(fields[2], out var pts) && long.TryParse(fields[3], out var duration))
            {
                var extent = extents[stream];
                extent.Count++;
                extent.First = Math.Min(extent.First, pts * tb);
                extent.Last = Math.Max(extent.Last, (pts + duration) * tb);
            }
        };
        await process.StartAndWaitAsync(token, TimeSpan.FromHours(4));
        if (process.ExitCode != 0 || extents.Count == 0)
            throw new InvalidOperationException("Nie udało się sprawdzić długości ścieżek. " + errors);
        return extents.OrderBy(x => x.Key).Select(x => x.Value).ToList();
    }
}
