using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

// Use the same Matroska muxer as MKVToolNix GUI. FFmpeg still handles audio only.
internal static class LektorMkvMuxer
{
    internal sealed record Track(int Id, string Type);

    internal static string ResolveExecutable()
    {
        var configured = Se.Settings.Video.TextToSpeech.LektorMkvmergePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return Path.GetFullPath(configured);
            throw new FileNotFoundException("Nie znaleziono wskazanego mkvmerge. Wybierz mkvmerge z MKVToolNix.", configured);
        }
        var name = OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "MKVToolNix", name),
            Path.Combine(AppContext.BaseDirectory, name)
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                candidates.Add(Path.Combine(Environment.GetFolderPath(folder), "MKVToolNix", name));
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(folder)) candidates.Add(Path.Combine(folder.Trim('"'), name));
        var found = candidates.FirstOrDefault(File.Exists);
        return found ?? throw new FileNotFoundException(
            "Tworzenie MKV wymaga mkvmerge z MKVToolNix. Wybierz mkvmerge.exe w oknie Lektor PL. Opcja Tylko miks działa bez MKVToolNix.");
    }

    internal static List<Track> ParseTracks(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.GetProperty("container").GetProperty("supported").GetBoolean())
            throw new InvalidOperationException("MKVToolNix nie obsługuje tego pliku źródłowego.");
        // mkvmerge IDs are NOT FFmpeg global stream indexes (attachments are separate).
        return document.RootElement.GetProperty("tracks").EnumerateArray()
            .Select(t => new Track(t.GetProperty("id").GetInt32(), t.GetProperty("type").GetString()!)).ToList();
    }

    internal static List<string> Arguments(string video, string mix, string output,
        IReadOnlyList<Track> source, int mixId)
    {
        var order = source.Where(t => t.Type == "video").Select(t => $"0:{t.Id}")
            .Concat(new[] { $"1:{mixId}" }).Concat(source.Where(t => t.Type != "video").Select(t => $"0:{t.Id}"));
        var args = new List<string> { "--ui-language", "en_US", "--output", output, "--track-order", string.Join(',', order) };
        foreach (var track in source.Where(t => t.Type is "audio" or "subtitles"))
            args.AddRange(["--default-track-flag", $"{track.Id}:0", "--forced-display-flag", $"{track.Id}:0"]);
        // Keep source timestamps, codec-private data, chapters, tags and attachments.
        // No generated PTS, frame rate override, codec conversion or timestamp rewriting.
        args.Add(video);
        args.AddRange(["--no-chapters", "--no-global-tags", "--no-track-tags", "--no-attachments",
            "--language", $"{mixId}:pol", "--track-name", $"{mixId}:Polski lektor PL",
            "--default-track-flag", $"{mixId}:1", "--forced-display-flag", $"{mixId}:0", mix]);
        return args;
    }

    internal static async Task MuxAsync(string executable, string video, string mix, string output,
        CancellationToken token)
    {
        var source = ParseTracks(await RunAsync(executable, ["--ui-language", "en_US", "-J", video], token));
        var added = ParseTracks(await RunAsync(executable, ["--ui-language", "en_US", "-J", mix], token));
        if (added.Count != 1 || added[0].Type != "audio")
            throw new ArgumentException("Miks musi zawierać dokładnie jedną ścieżkę audio.");
        await RunAsync(executable, Arguments(video, mix, output, source, added[0].Id), token, captureJson: false);
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> args,
        CancellationToken token, bool captureJson = true)
    {
        token.ThrowIfCancellationRequested();
        var log = new StringBuilder();
        var tail = new FfmpegOutputTail();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (captureJson) log.AppendLine(e.Data);
            lock (tail) tail.Add(e.Data);
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (tail) tail.Add(e.Data); };
        await process.StartAndWaitAsync(token, TimeSpan.FromHours(12));
        // Code 1 means mux completed WITH warnings, not an unqualified success.
        // Keep the saved audio and old MKV, and show the warning for review.
        if (process.ExitCode != 0)
            throw new InvalidOperationException("MKVToolNix " +
                (process.ExitCode == 1 ? "zgłosił ostrzeżenie" : "zakończył się błędem " + process.ExitCode) + ":\n" + tail);
        return log.ToString();
    }
}
