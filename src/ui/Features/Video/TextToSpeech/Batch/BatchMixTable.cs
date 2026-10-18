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

internal enum MixColumn { Video, Narrator }

internal partial class BatchMixRow : ObservableObject
{
    public int Number { get; init; }
    public string Video { get; init; } = string.Empty;
    public string Narrator { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public ObservableCollection<LektorTrack> AudioTracks { get; } = new();
    [ObservableProperty] private LektorTrack? _selectedTrack;
    [ObservableProperty] private string _status = "Oczekuje";
    private bool _updating;
    internal Action<LektorTrack?>? TrackChanged { get; set; }
    partial void OnSelectedTrackChanged(LektorTrack? value) { if (!_updating) TrackChanged?.Invoke(value); }
    internal void SetTracks(IEnumerable<LektorTrack> tracks, LektorTrack? selected)
    {
        _updating = true;
        try
        {
            AudioTracks.Clear(); foreach (var track in tracks) AudioTracks.Add(track);
            SelectedTrack = selected;
        }
        finally { _updating = false; }
    }
}

internal sealed class BatchMixTable
{
    private readonly Dictionary<MixColumn, List<string>> _columns = Enum.GetValues<MixColumn>().ToDictionary(c => c, _ => new List<string>());
    // Selection belongs to a particular video, never to a row number or narrator cell.
    private sealed record TrackChoice(long Length, long Modified, LektorTrack[] Tracks)
    {
        public int? Index { get; set; }
    }
    private readonly Dictionary<string, TrackChoice> _tracks = new(BatchFileRules.Paths);
    public ObservableCollection<BatchMixRow> Rows { get; } = new();
    public string OutputFolder { get; set; } = string.Empty;
    public LektorExportOptions Options { get; set; } = new();

    internal void LoadFolder(MixColumn column, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new DirectoryNotFoundException("Wskaż katalog źródłowy.");
        folder = BatchFileRules.FullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Brak katalogu: " + folder);
        var extensions = column == MixColumn.Video ? BatchMuxTable.VideoExtensions : [".wav"];
        SetColumn(column, Directory.EnumerateFiles(folder).Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)));
    }

    internal void SetColumn(MixColumn column, IEnumerable<string> files)
    {
        _columns[column] = files.Select(p => string.IsNullOrWhiteSpace(p) ? string.Empty : BatchFileRules.FullPath(p))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToList();
        Rebuild();
    }
    internal void SetCell(MixColumn column, int row, string path)
    {
        Pad(); CheckRow(row);
        _columns[column][row] = string.IsNullOrWhiteSpace(path) ? string.Empty : BatchFileRules.FullPath(path);
        Rebuild();
    }
    internal void Swap(MixColumn column, int from, int to)
    {
        Pad(); CheckRow(from); CheckRow(to);
        (_columns[column][from], _columns[column][to]) = (_columns[column][to], _columns[column][from]); Rebuild();
    }
    internal void InsertEmpty(MixColumn column, int row)
    {
        Pad(); if (row < 0 || row > Rows.Count) throw new ArgumentOutOfRangeException(nameof(row));
        _columns[column].Insert(row, string.Empty); Rebuild();
    }
    internal void DeleteCell(MixColumn column, int row)
    {
        Pad(); CheckRow(row); _columns[column].RemoveAt(row); Rebuild();
    }
    internal void RemoveRow(int row)
    {
        Pad(); CheckRow(row); foreach (var column in _columns.Values) column.RemoveAt(row); Rebuild();
    }
    internal void Clear()
    {
        foreach (var column in _columns.Values) column.Clear();
        _tracks.Clear(); Rebuild();
    }
    private void CheckRow(int row) { if (row < 0 || row >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(row)); }
    private void Pad()
    {
        var count = _columns.Values.Max(c => c.Count);
        foreach (var column in _columns.Values) while (column.Count < count) column.Add(string.Empty);
    }
    private static bool MatchesFile(string file, TrackChoice choice)
    {
        var info = new FileInfo(file);
        return info.Exists && info.Length == choice.Length && info.LastWriteTimeUtc.Ticks == choice.Modified;
    }
    internal void Rebuild()
    {
        Pad();
        while (_columns[MixColumn.Video].Count > 0 && _columns.Values.All(c => string.IsNullOrEmpty(c[^1])))
            foreach (var column in _columns.Values) column.RemoveAt(column.Count - 1);
        Rows.Clear();
        for (var i = 0; i < _columns[MixColumn.Video].Count; i++)
        {
            var video = _columns[MixColumn.Video][i];
            var row = new BatchMixRow
            {
                Number = i + 1, Video = video, Narrator = _columns[MixColumn.Narrator][i],
                Output = string.IsNullOrWhiteSpace(OutputFolder) || string.IsNullOrEmpty(video) ? string.Empty :
                    Path.Combine(OutputFolder, Path.GetFileNameWithoutExtension(video) + "_MiksPL" + Options.Extension),
            };
            row.TrackChanged = selected =>
            {
                // Recycled/detached ComboBoxes clear their selection while Rows is rebuilt.
                // Only a currently displayed row may change the stored selection for a video.
                if (Rows.Contains(row) && _tracks.TryGetValue(video, out var choice) && MatchesFile(video, choice))
                    choice.Index = selected != null && choice.Tracks.Contains(selected) ? selected.Index : null;
            };
            if (_tracks.TryGetValue(video, out var cached) && MatchesFile(video, cached))
                row.SetTracks(cached.Tracks, cached.Tracks.FirstOrDefault(t => t.Index == cached.Index));
            Rows.Add(row);
        }
    }

    internal BatchMixRow[] ValidatePaths()
    {
        Options.Validate();
        if (Rows.Count == 0) throw new InvalidDataException("Wczytaj katalog wideo i katalog WAV lektora.");
        BatchFileRules.CheckDestination(OutputFolder);
        foreach (var column in Enum.GetValues<MixColumn>())
        {
            var seen = new HashSet<string>(BatchFileRules.Paths);
            foreach (var row in Rows)
            {
                var path = column == MixColumn.Video ? row.Video : row.Narrator;
                var name = column == MixColumn.Video ? "wideo" : "WAV lektora";
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException($"Wiersz {row.Number}, {name}: brak pliku. Popraw komórkę przed startem.", path);
                if (!seen.Add(BatchFileRules.FullPath(path)))
                    throw new InvalidDataException($"W kolumnie {name} ten sam plik występuje wielokrotnie: {path}");
                if (column == MixColumn.Narrator && !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Wiersz {row.Number}: wskaż czysty WAV lektora PL.");
            }
        }
        BatchFileRules.CheckOutputs(Rows.Select(r => r.Output), Rows.SelectMany(r => new[] { r.Video, r.Narrator }));
        return Rows.ToArray();
    }

    internal async Task ReadTracksAsync(CancellationToken token, Action<string>? progress = null)
    {
        if (!Rows.Any(r => !string.IsNullOrWhiteSpace(r.Video)))
            throw new InvalidDataException("Wczytaj katalog filmów.");
        foreach (var row in Rows)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.Video)) continue;
            try
            {
                row.Status = "Odczyt ścieżek audio…";
                progress?.Invoke($"{row.Number}/{Rows.Count}: {row.Status}");
                var source = await LektorMediaProbe.ReadAsync(row.Video, token);
                var tracks = source.Tracks.Where(t => t.Type == "Audio").ToArray();
                if (!source.Tracks.Any(t => t.IsVideo) || tracks.Length == 0 || source.Duration <= 0)
                    throw new InvalidDataException($"Wiersz {row.Number}: film musi zawierać obraz i oryginalne audio o znanej długości.");
                int? index = null;
                if (_tracks.TryGetValue(row.Video, out var previous) && MatchesFile(row.Video, previous) &&
                    previous.Index is { } previousIndex &&
                    tracks.Any(t => t.Index == previousIndex && previous.Tracks.Contains(t))) index = previousIndex;
                if (tracks.Length == 1) index = tracks[0].Index;
                var info = new FileInfo(row.Video);
                var choice = new TrackChoice(info.Length, info.LastWriteTimeUtc.Ticks, tracks) { Index = index };
                _tracks[row.Video] = choice;
                row.SetTracks(tracks, tracks.FirstOrDefault(t => t.Index == index));
                row.Status = index.HasValue ? "Wybrano oryginalne audio" : "Wybierz oryginalną ścieżkę audio";
            }
            catch (OperationCanceledException) { row.Status = "Przerwano"; throw; }
            catch { row.Status = "Błąd odczytu ścieżek"; throw; }
        }
    }

    internal async Task<BatchMixRow[]> PreflightAsync(CancellationToken token, Action<string>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var rows = ValidatePaths();
        await ReadTracksAsync(token, progress);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (row.SelectedTrack == null)
                throw new InvalidDataException($"Wiersz {row.Number}: wybierz oryginalną ścieżkę audio filmu w tabeli.");
            row.Status = "Kontrola WAV lektora…";
            var narrator = await LektorMediaProbe.ReadAsync(row.Narrator, token);
            if (narrator.Tracks.Count != 1 || narrator.Tracks[0].Type != "Audio" || narrator.Duration <= 0)
                throw new InvalidDataException($"Wiersz {row.Number}: WAV musi zawierać jedną ścieżkę lektora o znanej długości.");
            row.Status = "Gotowy do miksowania";
        }
        return rows;
    }

    internal async Task RunAsync(CancellationToken token, Action<string>? progress = null)
    {
        // Validate every pair before the first output; this module never invokes the MKV muxer.
        var rows = await PreflightAsync(token, progress);
        var options = Options;
        foreach (var row in rows)
        {
            var temporary = Path.Combine(OutputFolder, ".batch-mix-" + Guid.NewGuid().ToString("N") + options.Extension);
            try
            {
                token.ThrowIfCancellationRequested();
                BatchFileRules.CheckOutputs([row.Output], rows.SelectMany(r => new[] { r.Video, r.Narrator }));
                await LektorExportService.ExportAsync(row.Video, row.Narrator, temporary, null, row.SelectedTrack!.Index,
                    options, token, text => { row.Status = text; progress?.Invoke($"{row.Number}/{rows.Length}: {text}"); });
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
