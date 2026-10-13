using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

// Use the same Matroska muxer as MKVToolNix GUI. FFmpeg still handles audio only.
internal static class LektorMkvMuxer
{
    internal sealed record Track(int Id, string Type);
    internal sealed record Attachment(int Id, string Name, string Mime, long Size);
    private sealed record AttachmentFingerprint(string Name, string Mime, long Size, string Hash);

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

    internal static List<Attachment> ParseAttachments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("attachments").EnumerateArray().Select(a => new Attachment(
            a.GetProperty("id").GetInt32(), a.GetProperty("file_name").GetString()!,
            a.GetProperty("content_type").GetString()!, a.GetProperty("size").GetInt64())).ToList();
    }

    internal static string NormalizeAttachmentMime(string mime) => mime.Trim().ToLowerInvariant() switch
    {
        "font/ttf" or "font/sfnt" or "font/collection" or "application/x-truetype-font" or
            "application/x-truetype-fonts" => "font/truetype",
        "font/otf" or "application/vnd.ms-opentype" => "font/opentype",
        var other => other
    };

    internal static async Task VerifyAttachmentsAsync(string executable, string source, string output, CancellationToken token)
    {
        var original = ParseAttachments(await RunAsync(executable, ["-J", source], token));
        var copied = ParseAttachments(await RunAsync(executable, ["-J", output], token));
        if (original.Count != copied.Count)
            throw new InvalidOperationException($"Nie zachowano kompletu załączników: {original.Count} → {copied.Count}.");
        if (original.Count == 0) return;
        var extract = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!,
            OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract");
        if (!File.Exists(extract))
            throw new FileNotFoundException("Kontrola załączników wymaga mkvextract z tego samego katalogu MKVToolNix co mkvmerge.", extract);
        var folder = Path.Combine(Path.GetTempPath(), "lektor-attachments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var before = await FingerprintsAsync(extract, source, original, Path.Combine(folder, "source"), token);
            var after = await FingerprintsAsync(extract, output, copied, Path.Combine(folder, "output"), token);
            // Attachment IDs/order may change; compare the actual bytes and meaningful metadata.
            var remaining = after.ToList();
            foreach (var item in before)
            {
                if (!remaining.Remove(item))
                    throw new InvalidOperationException($"Nie zachowano załącznika „{item.Name}” ({item.Mime}, {item.Size} bajtów) lub zmieniła się jego zawartość SHA-256.");
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (Exception ex) { Se.WriteToolsLog("Lektor PL: attachment cleanup failed: " + ex.Message, true); }
        }
    }

    private static async Task<List<AttachmentFingerprint>> FingerprintsAsync(string executable, string file,
        IReadOnlyList<Attachment> attachments, string folder, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        var args = new List<string> { file, "attachments" };
        for (var i = 0; i < attachments.Count; i++)
            args.Add($"{attachments[i].Id}:{Path.Combine(folder, i + ".bin")}");
        await RunAsync(executable, args, token, captureJson: false);
        var result = new List<AttachmentFingerprint>();
        for (var i = 0; i < attachments.Count; i++)
        {
            var item = attachments[i];
            await using var data = File.OpenRead(Path.Combine(folder, i + ".bin"));
            if (data.Length != item.Size)
                throw new InvalidOperationException("Nieprawidłowy rozmiar załącznika: " + item.Name);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(data, token));
            // FFmpeg on Windows can store an absolute path as the attachment name;
            // mkvmerge strips directories. Handle both separators on every platform.
            var name = item.Name.Replace('\\', '/').Split('/')[^1];
            result.Add(new(name, NormalizeAttachmentMime(item.Mime), item.Size, hash));
        }
        return result;
    }

    internal static List<string> Arguments(string video, string mix, string output,
        IReadOnlyList<Track> source, int mixId)
    {
        var order = source.Where(t => t.Type == "video").Select(t => $"0:{t.Id}")
            .Concat(new[] { $"1:{mixId}" }).Concat(source.Where(t => t.Type != "video").Select(t => $"0:{t.Id}"));
        var args = new List<string> { "--output", output, "--track-order", string.Join(',', order) };
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
        // Identification JSON is language-independent. Locale names differ across
        // MKVToolNix versions/platforms, so do not force an interface translation.
        var source = ParseTracks(await RunAsync(executable, ["-J", video], token));
        var added = ParseTracks(await RunAsync(executable, ["-J", mix], token));
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
