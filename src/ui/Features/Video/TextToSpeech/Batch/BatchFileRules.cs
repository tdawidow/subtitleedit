using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

internal static class BatchFileRules
{
    // The portable build targets Windows; names differing only by case are collisions.
    internal static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    internal static string FullPath(string path) => Path.GetFullPath(path.Trim().Trim('"'));

    internal static Subtitle ReadSrt(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Brak pliku SRT: " + path);
        var format = new SubRip();
        var subtitle = new Subtitle { FileName = FullPath(path) };
        format.LoadSubtitle(subtitle, File.ReadAllLines(path, new UTF8Encoding(false, true)).ToList(), path);
        if (format.ErrorCount > 0 || subtitle.Paragraphs.Count == 0 || subtitle.Paragraphs.Any(p =>
            string.IsNullOrWhiteSpace(p.Text) || p.StartTime.TotalMilliseconds < 0 || p.EndTime.TotalMilliseconds <= p.StartTime.TotalMilliseconds))
            throw new InvalidDataException("Nieprawidłowy lub pusty SRT (sprawdź UTF-8 i czasy): " + path);
        return subtitle;
    }

    internal static void CheckDestination(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("Wskaż istniejący wspólny katalog wynikowy.");
        var probe = Path.Combine(folder, ".lektor-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(probe)) { } }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    internal static void CheckOutputs(IEnumerable<string> outputs, IEnumerable<string> inputs)
    {
        var sources = inputs.Where(p => !string.IsNullOrWhiteSpace(p)).Select(FullPath).ToHashSet(Paths);
        var names = new HashSet<string>(Paths);
        foreach (var output in outputs.Select(FullPath))
        {
            if (!names.Add(output)) throw new InvalidDataException("Kolizja nazw wynikowych: " + output);
            if (sources.Contains(output)) throw new InvalidDataException("Wynik zastąpiłby plik źródłowy: " + output);
            if (File.Exists(output) || Directory.Exists(output))
                throw new IOException("Wynik już istnieje. Przenieś go lub wyłącz odpowiedni wiersz: " + output);
        }
    }

    internal static string UnusedPath(string path)
    {
        var original = path;
        for (var i = 2; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(Path.GetDirectoryName(original)!, Path.GetFileNameWithoutExtension(original) + "_" + i + Path.GetExtension(original));
        return path;
    }
}
