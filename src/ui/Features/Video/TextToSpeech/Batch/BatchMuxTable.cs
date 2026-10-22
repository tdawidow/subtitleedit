using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

public enum MuxColumn { Video, Audio, Subtitles }

public partial class BatchMuxRow : ObservableObject
{
    public int Number { get; init; }
    public string Video { get; init; } = string.Empty;
    public string Audio { get; init; } = string.Empty;
    public string Subtitles { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    [ObservableProperty] private string _status = "Oczekuje";
}

// Columns are independent sequences. Pairing always follows row order, never fuzzy filename
// matching. All cell operations alter exactly one column; empty cells are significant.
public sealed class BatchMuxTable
{
    private readonly Dictionary<MuxColumn, List<string>> _columns = Enum.GetValues<MuxColumn>().ToDictionary(c => c, _ => new List<string>());
    public ObservableCollection<BatchMuxRow> Rows { get; } = new();
    public string OutputFolder { get; set; } = string.Empty;
    public static readonly string[] VideoExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg"];
    public static readonly string[] AudioExtensions = [".ac3", ".eac3", ".aac", ".m4a", ".mp3", ".wav", ".flac", ".ogg", ".opus", ".dts", ".mka", ".wma"];

    public void LoadFolder(MuxColumn column, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new DirectoryNotFoundException("Wskaż katalog źródłowy.");
        folder = BatchFileRules.FullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Brak katalogu: " + folder);
        var extensions = column == MuxColumn.Video ? VideoExtensions : column == MuxColumn.Audio ? AudioExtensions : [".srt"];
        SetColumn(column, Directory.EnumerateFiles(folder).Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)));
    }

    public void SetColumn(MuxColumn column, IEnumerable<string> files)
    {
        _columns[column] = files.Select(p => string.IsNullOrWhiteSpace(p) ? string.Empty : BatchFileRules.FullPath(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToList();
        Rebuild();
    }
    public void SetCell(MuxColumn column, int row, string path)
    {
        Pad(); CheckRow(row); _columns[column][row] = string.IsNullOrWhiteSpace(path) ? string.Empty : BatchFileRules.FullPath(path); Rebuild();
    }
    public void Swap(MuxColumn column, int from, int to)
    {
        Pad(); CheckRow(from); CheckRow(to);
        (_columns[column][from], _columns[column][to]) = (_columns[column][to], _columns[column][from]); Rebuild();
    }
    public void InsertEmpty(MuxColumn column, int row)
    {
        Pad(); if (row < 0 || row > Rows.Count) throw new ArgumentOutOfRangeException(nameof(row));
        _columns[column].Insert(row, string.Empty); Rebuild();
    }
    public void DeleteCell(MuxColumn column, int row)
    {
        Pad(); CheckRow(row); _columns[column].RemoveAt(row); Rebuild();
    }
    public void RemoveRow(int row)
    {
        Pad(); CheckRow(row);
        foreach (var column in _columns.Values) column.RemoveAt(row);
        Rebuild();
    }
    public void Clear()
    {
        foreach (var column in _columns.Values) column.Clear();
        Rebuild();
    }
    private void CheckRow(int row) { if (row < 0 || row >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(row)); }
    private void Pad()
    {
        var count = _columns.Values.Max(c => c.Count);
        foreach (var column in _columns.Values) while (column.Count < count) column.Add(string.Empty);
    }
    public void Rebuild()
    {
        Pad();
        while (_columns.Values.First().Count > 0 && _columns.Values.All(c => string.IsNullOrEmpty(c[^1])))
            foreach (var column in _columns.Values) column.RemoveAt(column.Count - 1);
        Rows.Clear();
        for (var i = 0; i < _columns[MuxColumn.Video].Count; i++)
        {
            var video = _columns[MuxColumn.Video][i];
            Rows.Add(new BatchMuxRow
            {
                Number = i + 1, Video = video, Audio = _columns[MuxColumn.Audio][i], Subtitles = _columns[MuxColumn.Subtitles][i],
                Output = string.IsNullOrWhiteSpace(OutputFolder) || string.IsNullOrEmpty(video) ? string.Empty
                    : Path.Combine(OutputFolder, Path.GetFileNameWithoutExtension(video) + "_PL.mkv"),
            });
        }
    }

    internal BatchMuxRow[] ValidatePaths()
    {
        if (Rows.Count == 0) throw new InvalidDataException("Wczytaj katalogi plików do tabeli.");
        BatchFileRules.CheckDestination(OutputFolder);
        foreach (var column in Enum.GetValues<MuxColumn>())
        {
            var name = column == MuxColumn.Video ? "wideo" : column == MuxColumn.Audio ? "Miks PL" : "napisy PL";
            var seen = new HashSet<string>(BatchFileRules.Paths);
            foreach (var row in Rows)
            {
                var path = column == MuxColumn.Video ? row.Video : column == MuxColumn.Audio ? row.Audio : row.Subtitles;
                if (string.IsNullOrWhiteSpace(path) && column == MuxColumn.Subtitles) continue;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException($"Wiersz {row.Number}, {name}: brak pliku. Popraw komórkę przed startem.", path);
                if (!seen.Add(BatchFileRules.FullPath(path)))
                    throw new InvalidDataException($"Niejednoznaczne przypisanie: ten sam plik występuje wielokrotnie w kolumnie {name}: {path}");
                if (column == MuxColumn.Subtitles) BatchFileRules.ReadSrt(path);
            }
        }
        BatchFileRules.CheckOutputs(Rows.Select(r => r.Output), Rows.SelectMany(r => new[] { r.Video, r.Audio, r.Subtitles }));
        return Rows.ToArray();
    }

    internal async Task<BatchMuxRow[]> PreflightAsync(CancellationToken token, Action<string>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var rows = ValidatePaths();
        LektorMkvMuxer.ResolveExecutable();
        foreach (var row in rows)
        {
            try
            {
                token.ThrowIfCancellationRequested(); row.Status = "Kontrola plików…";
                progress?.Invoke($"{row.Number}/{rows.Length}: {row.Status}");
                var video = await LektorMediaProbe.ReadAsync(row.Video, token);
                if (!video.Tracks.Any(t => t.IsVideo)) throw new InvalidDataException($"Wiersz {row.Number}: plik wideo nie zawiera obrazu.");
                var audio = await LektorMediaProbe.ReadAsync(row.Audio, token);
                if (audio.Tracks.Count != 1 || audio.Tracks[0].Type != "Audio" || audio.Duration <= 0)
                    throw new InvalidDataException($"Wiersz {row.Number}: Miks PL musi zawierać dokładnie jedną ścieżkę audio o znanej długości.");
                if (!string.IsNullOrEmpty(row.Subtitles)) await LektorMkvMuxer.ReadForcedSubtitlesAsync(row.Subtitles, token);
                row.Status = "Gotowy do startu";
            }
            catch (OperationCanceledException) { row.Status = "Przerwano"; throw; }
            catch (Exception ex) { row.Status = "Błąd kontroli: " + ex.Message; throw; }
        }
        return rows;
    }

    internal async Task RunAsync(CancellationToken token, Action<string>? progress = null)
    {
        // Validate the entire queue before creating the first MKV.
        var rows = await PreflightAsync(token, progress);
        foreach (var row in rows)
        {
            // Match the working batch MIX PL queue: publish a verified result without
            // ever replacing a file that appeared after the queue's preflight check.
            var temporary = Path.Combine(OutputFolder, ".batch-mkv-" + Guid.NewGuid().ToString("N") + ".mkv");
            try
            {
                token.ThrowIfCancellationRequested();
                BatchFileRules.CheckOutputs([row.Output], rows.SelectMany(r => new[] { r.Video, r.Audio, r.Subtitles }));
                await LektorExportService.ExportAsync(row.Video, string.Empty, row.Audio, temporary, 0,
                    new LektorExportOptions(), token, text => { row.Status = text; progress?.Invoke($"{row.Number}/{rows.Length}: {text}"); },
                    useExistingMix: true, forcedSubtitleFile: string.IsNullOrEmpty(row.Subtitles) ? null : row.Subtitles);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, row.Output, overwrite: false);
                row.Status = "Gotowe";
                progress?.Invoke($"{row.Number}/{rows.Length}: zapisano {Path.GetFileName(row.Output)}");
            }
            catch (OperationCanceledException) { row.Status = "Przerwano"; throw; }
            catch { row.Status = "Błąd — kolejka zatrzymana"; throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
