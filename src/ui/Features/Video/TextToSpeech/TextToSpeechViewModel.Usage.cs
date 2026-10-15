using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.ActorVoices;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech;

public partial class TextToSpeechViewModel
{
    [ObservableProperty] private string _usageSummary = "Znaki TTS: wybierz silnik. Lokalny licznik można włączyć ikoną ustawień.";
    internal TtsUsageStore UsageStore { get; set; } = new();
    internal string UsageJobsRoot { get; set; } = Path.Combine(Se.DataFolder, "TtsJobs");
    private readonly HashSet<int> _usageCompleted = new();
    private ResolvedVoice[]? _usageResolvedPlan;
    private long _lastUsageRefresh;
    private readonly ConcurrentDictionary<string, long> _usageInFlight = new();
    private int _usageSendingIndex = -1;

    partial void OnSelectedEngineChanged(ITtsEngine? value) => InvalidateUsageEstimate();
    partial void OnApiKeyChanged(string value) => InvalidateUsageEstimate();

    private void InvalidateUsageEstimate()
    {
        _usageCompleted.Clear();
        _usageResolvedPlan = null;
        RefreshUsageEstimate();
    }

    private string UsageAccount(ITtsEngine engine) => TtsUsageAccount.ForEngine(engine,
        ReferenceEquals(engine, SelectedEngine) ? engine.HasApiKey ? ApiKey : engine.HasKeyFile ? KeyFile : null : null,
        ReferenceEquals(engine, SelectedEngine) ? SelectedRegion : null);

    internal void RefreshUsageEstimate()
    {
        if (SelectedEngine == null) return;
        try
        {
            var groups = new Dictionary<string, (string Name, long Needed)>();
            for (var i = 0; i < _subtitle.Paragraphs.Count; i++)
            {
                if (_usageCompleted.Contains(i) || _skipNoiseParagraphs.Contains(_subtitle.Paragraphs[i])) continue;
                var paragraph = _subtitle.Paragraphs[i];
                var engine = _usageResolvedPlan != null && i < _usageResolvedPlan.Length ? _usageResolvedPlan[i].Engine : SelectedEngine;
                var text = _usageResolvedPlan != null && i < _usageResolvedPlan.Length ? _usageResolvedPlan[i].Text
                    : Utilities.UnbreakLine(HtmlUtil.RemoveHtmlTags(ActorVoiceDetector.StripWebVttVoiceTags(paragraph.Text), true));
                if (_usageResolvedPlan == null)
                {
                    var actor = ActorVoiceDetector.GetParagraphActor(paragraph, _castKind);
                    var mapping = _actorVoiceMappings.FirstOrDefault(m => string.Equals(m.Actor, actor, StringComparison.OrdinalIgnoreCase));
                    if (mapping != null) engine = Engines.FirstOrDefault(e => e.Name == mapping.EngineName) ?? engine;
                }
                var account = UsageAccount(engine);
                groups.TryGetValue(account, out var group);
                groups[account] = (engine.Name, group.Needed + TtsUsageText.Count(text, engine is AzureSpeech));
            }
            if (groups.Count == 0) groups[UsageAccount(SelectedEngine)] = (SelectedEngine.Name, 0);
            UsageSummary = string.Join(Environment.NewLine, groups.Select(g =>
            {
                var p = UsageStore.Get(g.Key);
                var prefix = groups.Count > 1 ? g.Value.Name + ": " : string.Empty;
                _usageInFlight.TryGetValue(g.Key, out var inFlight);
                if (_usageSendingIndex < 0 || _usageCompleted.Contains(_usageSendingIndex)) inFlight = 0;
                // Available already subtracts the active reservation. Do not also subtract
                // that same request a second time when projecting the remaining run.
                var after = p.AvailableCharacters - g.Value.Needed + Math.Min(inFlight, g.Value.Needed);
                return p.Enabled
                    ? $"{prefix}Do wysłania: {g.Value.Needed:N0} | Dostępne: {p.AvailableCharacters:N0} | Po generowaniu: {after:N0}" +
                      (p.ProviderQuotaExhausted ? "\nUsługa zgłosiła wyczerpanie limitu. Wymagany reset lub korekta salda w ustawieniach." : string.Empty) +
                      (p.ReservedCharacters > 0 ? $"\nRezerwacje / niepotwierdzone żądania: {p.ReservedCharacters:N0}. Możliwa korekta salda w ustawieniach." : string.Empty)
                    : $"{prefix}Do wysłania: {g.Value.Needed:N0} | Dostępne: kontrola wyłączona | Po generowaniu: —";
            })) + "\nLokalne oszacowanie; użycie w innych programach wymaga korekty salda.";
        }
        catch (Exception ex) { UsageSummary = "Nie można odczytać licznika TTS: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ShowUsageSettings()
    {
        if (SelectedEngine == null || Window == null || IsGenerating) return;
        try
        {
            var dialog = new TtsUsageSettingsWindow(UsageStore, UsageAccount(SelectedEngine), SelectedEngine.Name);
            await dialog.ShowDialog<bool>(Window);
            RefreshUsageEstimate();
        }
        catch (Exception ex)
        {
            await MessageBox.Show(Window, "Licznik znaków TTS", ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<TtsResult> SpeakWithUsageAsync(ITtsEngine engine, string text, Voice voice,
        TtsLanguage? language, string? region, string? model, CancellationToken token)
    {
        using var progress = new TtsRequestProgress(message => Dispatcher.UIThread.Post(() =>
        {
            ProgressText = message;
            RefreshUsageEstimate();
        }), (accountId, characters) =>
        {
            if (characters > 0) _usageInFlight[accountId] = characters;
            else _usageInFlight.TryRemove(accountId, out _);
            Dispatcher.UIThread.Post(RefreshUsageEstimate);
        });
        // Azure accounts per network attempt inside the transport, including voice previews
        // and HTTP retries. Wrapping it here as well would double-charge every successful line.
        var account = UsageAccount(engine);
        var tracked = engine is not AzureSpeech && UsageStore.Get(account).Enabled;
        string? reservation = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (tracked)
            {
                var count = TtsUsageText.Count(text, false);
                reservation = UsageStore.Reserve(account, count);
                TtsRequestProgress.Reservation(account, reservation != null ? count : 0);
            }
            var result = await engine.Speak(text, _waveFolder, voice, language, region, model, token);
            var valid = !result.Error && TtsAudioValidation.IsUsable(result.FileName);
            if (tracked)
            {
                if (valid) UsageStore.Resolve(account, reservation, true);
                else
                {
                    var kind = result.FailureKind != TtsFailureKind.None ? result.FailureKind
                        : TtsUsageText.Classify(System.Net.HttpStatusCode.BadRequest, result.ErrorMessage, false);
                    if (kind == TtsFailureKind.ProviderQuota || result.ErrorMessage.Contains("401") || result.ErrorMessage.Contains("403"))
                        UsageStore.Resolve(account, reservation, false);
                    if (kind == TtsFailureKind.ProviderQuota) UsageStore.MarkProviderQuotaExhausted(account);
                    // Unknown failures retain a reservation and stop instead of blindly retrying.
                    return result with { Error = true, FailureKind = kind == TtsFailureKind.ProviderQuota ? kind : TtsFailureKind.Uncertain,
                        ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage) ? "Brak poprawnego audio. Zużycie znaków jest niepotwierdzone." : result.ErrorMessage };
                }
            }
            if (!valid && TtsUsageText.Classify(System.Net.HttpStatusCode.BadRequest, result.ErrorMessage, false) == TtsFailureKind.ProviderQuota)
            {
                UsageStore.MarkProviderQuotaExhausted(account);
                return result with { Error = true, FailureKind = TtsFailureKind.ProviderQuota };
            }
            if (!valid && !result.Error && !string.IsNullOrEmpty(result.FileName))
                return result with { Error = true, FileName = string.Empty, FailureKind = TtsFailureKind.Uncertain,
                    ErrorMessage = "Silnik zwrócił nieprawidłowe audio. Przetworzone żądanie mogło zużyć znaki." };
            return result;
        }
        catch (TtsServiceException ex) { return new TtsResult { Text = text, Error = true, ErrorMessage = ex.Message, FailureKind = ex.Kind }; }
        catch (OperationCanceledException) { throw; }
        catch (System.Net.Http.HttpRequestException ex) when (tracked && ex.StatusCode.HasValue && (int)ex.StatusCode.Value is >= 400 and < 500)
        {
            UsageStore.Resolve(account, reservation, false);
            return new TtsResult { Text = text, Error = true, ErrorMessage = ex.Message,
                FailureKind = TtsUsageText.Classify(ex.StatusCode.Value, ex.Message, false) };
        }
        catch (Exception ex) when (tracked || engine is AzureSpeech)
        {
            return new TtsResult { Text = text, Error = true, ErrorMessage = ex.Message, FailureKind = TtsFailureKind.Uncertain };
        }
        finally { Dispatcher.UIThread.Post(RefreshUsageEstimate); }
    }

    private async Task<(TtsJobStore Job, bool Resume)?> PrepareUsageRunAsync(ResolvedVoice[] plan)
    {
        _usageResolvedPlan = plan;
        _usageCompleted.Clear();
        // Persist only the hash. This input includes credentials and is never written/logged.
        var fingerprint = TtsUsageStore.Hash(JsonSerializer.Serialize(new
        {
            Source = _subtitle.FileName,
            Lines = _subtitle.Paragraphs.Select((p, i) => new { Start = p.StartTime.TotalMilliseconds, End = p.EndTime.TotalMilliseconds,
                Original = p.Text, plan[i].Text, Engine = plan[i].Engine.Name, Voice = plan[i].Voice.Name,
                plan[i].Model, plan[i].Instruction, Account = UsageAccount(plan[i].Engine), Skip = _skipNoiseParagraphs.Contains(p) }),
            Settings = Se.Settings.Video.TextToSpeech,
        }));
        var job = new TtsJobStore(UsageJobsRoot, fingerprint);
        try
        {
            var cached = Enumerable.Range(0, plan.Length).Where(i => !_skipNoiseParagraphs.Contains(_subtitle.Paragraphs[i]) && job.GetClip(i) != null).ToArray();
            var resume = false;
            if (cached.Length > 0)
            {
                var answer = await MessageBox.Show(Window!, "Zapisany postęp TTS",
                    $"Znaleziono {cached.Length} gotowych fragmentów dla tych napisów i ustawień.\n\n" +
                    "Tak — wykorzystaj je bez ponownego TTS.\nNie — wygeneruj wszystkie fragmenty ponownie.\nAnuluj — wróć do okna.",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == MessageBoxResult.Cancel) { job.Dispose(); return null; }
                resume = answer == MessageBoxResult.Yes;
            }
            if (resume) _usageCompleted.UnionWith(cached);
            var required = new Dictionary<string, long>();
            for (var i = 0; i < plan.Length; i++)
            {
                if (_usageCompleted.Contains(i) || _skipNoiseParagraphs.Contains(_subtitle.Paragraphs[i])) continue;
                var account = UsageAccount(plan[i].Engine);
                required.TryGetValue(account, out var n);
                required[account] = n + TtsUsageText.Count(plan[i].Text, plan[i].Engine is AzureSpeech);
            }
            RefreshUsageEstimate();
            foreach (var item in required)
            {
                var p = UsageStore.Get(item.Key);
                if (p.Enabled && item.Value > 0 && item.Value > p.AvailableCharacters)
                {
                    await MessageBox.Show(Window!, "Za mało znaków TTS",
                        $"Potrzeba {item.Value:N0} znaków, dostępne {p.AvailableCharacters:N0}. Brakuje {item.Value - p.AvailableCharacters:N0}.\n" +
                        "Generowanie nie zostało uruchomione. Sprawdź saldo w ustawieniach licznika.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    job.Dispose();
                    return null;
                }
            }
            return (job, resume);
        }
        catch { job.Dispose(); throw; }
    }

    private async Task RecoverUsageRunAsync(TtsJobStore job, List<TtsStepResult> results, string reason)
    {
        if (Window == null || !Window.IsVisible) return;
        var answer = await MessageBox.Show(Window, "Zatrzymano generowanie TTS",
            reason + "\n\nGotowe fragmenty i postęp zostały zachowane.\n\n" +
            "Czy zapisać nowy SRT z niewygenerowanymi liniami oraz połączyć gotowe fragmenty w częściowy, zsynchronizowany WAV?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var suggested = Path.GetFileNameWithoutExtension(GetSuggestedMergedAudioFileName()) + "_pozostale.srt";
        var srt = await _fileHelper.PickSaveFile(Window, ".srt", suggested, "Zapisz pozostałe linie SRT");
        if (string.IsNullOrWhiteSpace(srt)) return;
        job.SaveRemainingSrt(_subtitle, _usageCompleted, _skipNoiseParagraphs, srt);
        var readyList = results.Where(r => !string.IsNullOrWhiteSpace(r.CurrentFileName) && File.Exists(r.CurrentFileName)).ToList();
        // A resumed run can fail before reaching later cached lines. They are still finished
        // work and must be included in the partial WAV, even though this loop never visited them.
        foreach (var i in _usageCompleted.OrderBy(i => i))
        {
            if (readyList.Any(r => ReferenceEquals(r.Paragraph, _subtitle.Paragraphs[i]))) continue;
            var clip = job.GetClip(i);
            if (clip == null) continue;
            var copy = Path.Combine(_waveFolder, Guid.NewGuid().ToString("N") + Path.GetExtension(clip));
            File.Copy(clip, copy);
            readyList.Add(new TtsStepResult { Paragraph = _subtitle.Paragraphs[i], Text = _usageResolvedPlan?[i].Text ?? _subtitle.Paragraphs[i].Text,
                CurrentFileName = copy, Voice = _usageResolvedPlan?[i].Voice ?? SelectedVoice,
                EngineName = _usageResolvedPlan?[i].Engine.Name ?? SelectedEngine?.Name ?? string.Empty });
        }
        var ready = readyList.OrderBy(r => r.Paragraph.StartTime.TotalMilliseconds).ToArray();
        if (ready.Length == 0)
        {
            await MessageBox.Show(Window, "Zapisano pozostały SRT", srt + "\nBrak gotowych fragmentów do połączenia.", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var wave = await _fileHelper.PickSaveFile(Window, ".wav", Path.Combine(Path.GetDirectoryName(srt)!, Path.GetFileNameWithoutExtension(srt) + "_czesciowy.wav"), "Zapisz częściowy WAV lektora");
        if (string.IsNullOrWhiteSpace(wave)) return;
        // Only local stages: no calls to Speak. A fresh token permits recovery after Stop.
        var fixedClips = await FixSpeed(ready, CancellationToken.None);
        if (fixedClips == null) return;
        var processed = await ApplyPostProcessing(fixedClips, CancellationToken.None);
        if (processed == null) return;
        var merged = await MergeAudioParagraphs(processed, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(merged)) return;
        File.Move(merged, wave, true);
        _lastSavedNarratorWave = wave;
        await MessageBox.Show(Window, "Zapisano odzyskaną pracę", $"SRT: {srt}\nWAV: {wave}\n\nZachowano oryginalne czasy i ciszę w przerwach.", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
