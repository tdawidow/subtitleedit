using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Nikse.SubtitleEdit.Logic.Config;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.ActorVoices;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech;

public partial class TextToSpeechViewModel
{
    public bool IsBatchMode { get; internal set; }
    public ObservableCollection<TtsBatchItem> BatchItems { get; } = new();
    [ObservableProperty] private TtsBatchItem? _selectedBatchItem;
    [ObservableProperty] private string _batchOutputFolder = string.Empty;
    [ObservableProperty] private string _batchQueueInfo = "Dodaj pliki SRT do kolejki.";
    [ObservableProperty] private string _batchStatus = "Przed startem sprawdź kolejność, zapotrzebowanie i wspólny katalog wynikowy.";
    private TtsBatchItem? _batchActiveItem;
    private string? _batchEstimateSettings;

    private void RefreshBatchSettingsSignature()
    {
        if (IsGenerating) return;
        var signature = TtsUsageStore.Hash(JsonSerializer.Serialize(new
        {
            Engine = SelectedEngine?.Name, Voice = SelectedVoice?.Name, SelectedRegion, SelectedModel,
            ApiKey, KeyFile, Instruction, Settings = Se.Settings.Video.TextToSpeech,
        }));
        if (signature == _batchEstimateSettings) return;
        _batchEstimateSettings = signature;
        foreach (var item in BatchItems) item.CachedClips = 0;
    }

    partial void OnBatchOutputFolderChanged(string value)
    {
        foreach (var item in BatchItems)
            item.OutputFile = string.IsNullOrWhiteSpace(value) ? string.Empty : Path.Combine(value.Trim().Trim('"'), Path.GetFileNameWithoutExtension(item.SourceFile) + ".wav");
    }

    internal void AddBatchFiles(IEnumerable<string> files)
    {
        if (IsGenerating) return;
        foreach (var path in files)
        {
            var source = BatchFileRules.FullPath(path);
            if (BatchItems.Any(i => BatchFileRules.Paths.Equals(i.SourceFile, source))) continue;
            var item = new TtsBatchItem(source);
            try { ReadBatchSource(item); }
            catch (Exception ex) { item.Status = ex.Message; }
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TtsBatchItem.Included)) RefreshUsageEstimate(); };
            BatchItems.Add(item);
        }
        RenumberBatch();
        OnBatchOutputFolderChanged(BatchOutputFolder);
        RefreshUsageEstimate();
    }

    [RelayCommand] private async Task AddBatchSrt()
    {
        if (Window == null || IsGenerating) return;
        var files = await _fileHelper.PickOpenFiles(Window, "Dodaj SRT — kolejność można zmienić strzałkami", "SubRip (SRT)", ["*.srt"], string.Empty, []);
        AddBatchFiles(files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }
    [RelayCommand] private async Task PickBatchOutput()
    {
        if (Window == null || IsGenerating) return;
        var folder = await _folderHelper.PickFolderAsync(Window, "Wspólny katalog wynikowy WAV", BatchOutputFolder);
        if (!string.IsNullOrWhiteSpace(folder)) BatchOutputFolder = folder;
    }
    [RelayCommand] private void RemoveBatchItem()
    {
        if (IsGenerating || SelectedBatchItem == null) return;
        BatchItems.Remove(SelectedBatchItem); RenumberBatch(); RefreshUsageEstimate();
    }
    [RelayCommand] private void BatchUp() => MoveBatch(-1);
    [RelayCommand] private void BatchDown() => MoveBatch(1);
    private void MoveBatch(int delta)
    {
        if (IsGenerating || SelectedBatchItem == null) return;
        var index = BatchItems.IndexOf(SelectedBatchItem);
        if (index + delta < 0 || index + delta >= BatchItems.Count) return;
        BatchItems.Move(index, index + delta); RenumberBatch();
    }
    private void RenumberBatch() { for (var i = 0; i < BatchItems.Count; i++) BatchItems[i].Number = i + 1; }

    private static void ReadBatchSource(TtsBatchItem item)
    {
        item.Subtitle = BatchFileRules.ReadSrt(item.SourceFile);
        var text = item.Subtitle.Paragraphs.Select(BatchText).ToArray();
        item.PlainCharacters = text.Sum(t => TtsUsageText.Count(t, false));
        item.AzureCharacters = text.Sum(t => TtsUsageText.Count(t, true));
    }

    private static string BatchText(Paragraph paragraph) => Utilities.UnbreakLine(HtmlUtil.RemoveHtmlTags(ActorVoiceDetector.StripWebVttVoiceTags(paragraph.Text), true));

    private Dictionary<string, (string Name, long Needed)> BatchUsageGroups(Dictionary<string, (string Name, long Needed)> current)
    {
        if (SelectedEngine == null) return current;
        RefreshBatchSettingsSignature();
        long needed = 0, total = 0;
        foreach (var item in BatchItems)
        {
            item.TotalCharacters = SelectedEngine is AzureSpeech ? item.AzureCharacters : item.PlainCharacters;
            if (_batchActiveItem == item && IsGenerating)
                item.NeededCharacters = current.Values.Sum(g => g.Needed);
            else if (!IsGenerating && item.CachedClips == 0)
                item.NeededCharacters = item.TotalCharacters;
            if (!item.Included || item.Completed) continue;
            needed += item.NeededCharacters;
            total += item.TotalCharacters;
        }
        BatchQueueInfo = $"Wybrane pliki: {BatchItems.Count(i => i.Included && !i.Completed)} / {BatchItems.Count}    Wszystkie znaki: {total:N0}    Do wysłania: {needed:N0}";
        return new() { [UsageAccount(SelectedEngine)] = (SelectedEngine.Name, needed) };
    }

    private void SetBatchSubtitle(Subtitle subtitle)
    {
        _subtitle = new Subtitle(subtitle) { FileName = subtitle.FileName };
        _originalSubtitle = null; _originalByStartMs = null;
        _videoFileName = string.Empty; _mediaInfo = null; HasVideoFile = false;
        _castKind = ActorVoiceDetector.CastKind.None; _actorVoiceMappings.Clear(); _skipNoiseParagraphs.Clear();
        _usageCompleted.Clear(); _usageResolvedPlan = null;
    }

    internal void CheckBatchQueue()
    {
        if (SelectedEngine == null || SelectedVoice == null) throw new InvalidOperationException("Wybierz silnik i głos.");
        if (PerLineVoiceClone.IsSelected(SelectedVoice)) throw new InvalidOperationException("Masowe SRT → WAV wymaga wspólnego głosu; klonowanie z osobnych filmów jest dostępne w trybie pojedynczym.");
        if (string.IsNullOrWhiteSpace(BatchOutputFolder)) throw new InvalidOperationException("Wskaż wspólny katalog wynikowy WAV.");
        RefreshBatchSettingsSignature();
        BatchOutputFolder = BatchFileRules.FullPath(BatchOutputFolder);
        BatchFileRules.CheckDestination(BatchOutputFolder);
        var queue = BatchItems.Where(i => i.Included && !i.Completed).ToArray();
        if (queue.Length == 0) throw new InvalidOperationException("Zaznacz przynajmniej jeden plik SRT.");
        BatchFileRules.CheckOutputs(queue.Select(i => i.OutputFile), BatchItems.Select(i => i.SourceFile));
        foreach (var item in queue)
        {
            ReadBatchSource(item);
            if (item.Subtitle!.Paragraphs.Any(p => string.IsNullOrWhiteSpace(BatchText(p))))
                throw new InvalidDataException("SRT zawiera linię bez tekstu do syntezy: " + item.SourceFile);
            item.SourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.SourceFile)));
            SetBatchSubtitle(item.Subtitle);
            var context = new CastContext();
            var plan = _subtitle.Paragraphs.Select(p => ResolveVoiceForParagraph(p, context, SelectedEngine, SelectedVoice)).ToArray();
            using var job = new TtsJobStore(UsageJobsRoot, UsageFingerprint(plan));
            item.CachedClips = 0; item.NeededCharacters = 0;
            for (var i = 0; i < plan.Length; i++)
            {
                if (job.GetClip(i) != null) item.CachedClips++;
                else item.NeededCharacters += TtsUsageText.Count(plan[i].Text, SelectedEngine is AzureSpeech);
            }
            item.Status = item.CachedClips > 0 ? $"Gotowe fragmenty: {item.CachedClips}; zostaną użyte" : "Gotowy do startu";
        }
        RefreshUsageEstimate();
        var profile = UsageStore.Get(UsageAccount(SelectedEngine));
        var required = queue.Sum(i => i.NeededCharacters);
        if (profile.Enabled && required > 0 && (profile.ProviderQuotaExhausted || required > profile.AvailableCharacters))
            throw new InvalidOperationException($"Za mało znaków dla całej kolejki. Potrzeba: {required:N0}; dostępne: {profile.AvailableCharacters:N0}. Odznacz część plików i sprawdź ponownie. Kolejka nie została uruchomiona.");
    }

    [RelayCommand] private async Task CheckBatch()
    {
        if (IsGenerating) return;
        try { SaveSettings(); CheckBatchQueue(); BatchStatus = "Kontrola zakończona. Sprawdź tabelę i uruchom kolejkę."; }
        catch (Exception ex) { BatchStatus = ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand] private async Task GenerateBatch()
    {
        if (IsGenerating || Window == null) return;
        try
        {
            SaveSettings(); CheckBatchQueue();
            var engine = SelectedEngine!; var voice = SelectedVoice!;
            if (!await IsEngineInstalled(engine) || !await TtsVoiceInstaller.EnsureVoiceInstalled(engine, voice, Window, _windowService)
                || !await EnsureVoiceCloningConsentAsync(voice)) return;
            await Task.Run(() => StopOtherCrispAsrServers(engine));
            _cancellationTokenSource = new(); _cancellationToken = _cancellationTokenSource.Token;
            await ExecuteBatchQueueAsync(_cancellationToken);
        }
        catch (Exception ex) { BatchStatus = "Zatrzymano: " + ex.Message; }
    }

    // Uses the same synthesis, speed correction, post-processing and synchronized merger as
    // the single-file workflow; only selection, destination and queue lifecycle differ.
    internal async Task ExecuteBatchQueueAsync(CancellationToken token)
    {
        CheckBatchQueue();
        var queue = BatchItems.Where(i => i.Included && !i.Completed).ToArray();
        IsGenerating = true; _generateStopwatch.Restart(); ProgressOpacity = 1;
        try
        {
            for (var index = 0; index < queue.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var item = queue[index];
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.SourceFile))) != item.SourceHash)
                    throw new IOException("SRT zmienił się po kontroli: " + item.SourceFile);
                // Recheck the destination immediately before paid work as well as at preflight.
                BatchFileRules.CheckOutputs([item.OutputFile], BatchItems.Select(i => i.SourceFile));
                _batchActiveItem = item; SetBatchSubtitle(item.Subtitle!);
                item.Status = "Generowanie…";
                BatchStatus = $"Plik {index + 1}/{queue.Length}: {Path.GetFileName(item.SourceFile)}";
                var speech = await GenerateSpeech(token);
                if (speech == null) { item.Status = "Zatrzymany — postęp zachowany"; BatchStatus = "Kolejka zatrzymana. Kolejne SRT nie zostały wysłane."; return; }
                var fixedClips = await FixSpeed(speech, token);
                if (fixedClips == null || fixedClips.Length != speech.Length) throw new InvalidOperationException("Korekta dźwięku nie objęła wszystkich linii. Postęp TTS zachowany.");
                var processed = await ApplyPostProcessing(fixedClips, token);
                if (processed == null || processed.Length != speech.Length) throw new InvalidOperationException("Przetwarzanie dźwięku nie objęło wszystkich linii. Postęp TTS zachowany.");
                var merged = await MergeAudioParagraphs(processed, token);
                if (string.IsNullOrEmpty(merged)) throw new InvalidOperationException("Nie udało się połączyć WAV. Postęp TTS zachowany.");
                token.ThrowIfCancellationRequested();
                File.Move(merged, item.OutputFile, false);
                _lastSavedNarratorWave = item.OutputFile;
                item.Completed = true; item.Included = false; item.NeededCharacters = 0; item.Status = "Gotowe";
                _batchActiveItem = null; RefreshUsageEstimate();
            }
            BatchStatus = $"Zakończono: {queue.Length} plików WAV. Możesz dodać kolejne SRT.";
        }
        catch (OperationCanceledException) { BatchStatus = "Zatrzymano kolejkę. Gotowe WAV i postęp bieżącego SRT zachowano."; }
        catch
        {
            if (_batchActiveItem != null) _batchActiveItem.Status = "Błąd — postęp TTS zachowany";
            throw;
        }
        finally { _batchActiveItem = null; ResetGeneratingUiState(); RefreshUsageEstimate(); }
    }

    private string BatchRecoveryPath(string suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(_batchActiveItem?.SourceFile ?? _subtitle.FileName);
        var path = BatchFileRules.UnusedPath(Path.Combine(BatchOutputFolder, stem + suffix));
        var reserved = BatchItems.Select(i => i.OutputFile).Where(p => !string.IsNullOrWhiteSpace(p)).Select(BatchFileRules.FullPath).ToHashSet(BatchFileRules.Paths);
        for (var i = 2; reserved.Contains(BatchFileRules.FullPath(path)); i++)
            path = BatchFileRules.UnusedPath(Path.Combine(BatchOutputFolder, stem + "_" + i + suffix));
        return path;
    }
}
