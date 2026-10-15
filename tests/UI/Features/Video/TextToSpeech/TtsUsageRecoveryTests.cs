using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Download;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

public class TtsUsageRecoveryTests
{
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }

    private sealed class FakeEngine : ITtsEngine
    {
        public string Name => "Fake metered TTS";
        public string Description => "No network or paid requests";
        public bool HasLanguageParameter => false;
        public bool HasApiKey => false;
        public bool HasRegion => false;
        public bool HasModel => false;
        public bool HasKeyFile => false;
        public bool SupportsVoiceCloning => false;
        public bool SupportsPerLineVoiceCloning => false;
        public bool RejectSecondLine { get; set; } = true;
        public Task? Pause { get; set; }
        public List<string> Sent { get; } = new();
        public override string ToString() => Name;
        public Task<bool> IsInstalled(string? region) => Task.FromResult(true);
        public Task<string[]> GetRegions() => Task.FromResult(Array.Empty<string>());
        public Task<string[]> GetModels() => Task.FromResult(Array.Empty<string>());
        public Task<TtsLanguage[]> GetLanguages(Voice voice, string? model) => Task.FromResult(Array.Empty<TtsLanguage>());
        public bool IsVoiceInstalled(Voice voice) => true;
        public bool ImportVoice(string path) => false;
        public Task<Voice[]> GetVoices(string language) => Task.FromResult(new[] { new Voice(new object(), "Marek test") });
        public Task<Voice[]> RefreshVoices(string language, CancellationToken token) => GetVoices(language);
        public async Task<TtsResult> Speak(string text, string outputFolder, Voice voice, TtsLanguage? language, string? region, string? model, CancellationToken token)
        {
            Sent.Add(text);
            if (Pause != null) await Pause.WaitAsync(token);
            if (RejectSecondLine && text == "Druga")
                return new TtsResult { Error = true, FailureKind = TtsFailureKind.ProviderQuota, ErrorMessage = "Wyczerpano miesięczny limit znaków." };
            var path = Path.Combine(outputFolder, Guid.NewGuid() + ".wav");
            WriteTone(path);
            return new TtsResult(path, text);
        }
    }

    public class SaveFiles : DispatchProxy
    {
        public Queue<string> Paths { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "PickSaveFile") return Task.FromResult(Paths.Dequeue());
            throw new InvalidOperationException("Unexpected picker: " + method?.Name);
        }
    }

    private static void WriteTone(string fileName)
    {
        const int sampleRate = 16000;
        const int samples = 4800;
        using var writer = new BinaryWriter(File.Create(fileName), Encoding.ASCII);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
    }

    private static TextToSpeechViewModel Build(string folder, IFileHelper files)
    {
        var vm = new TextToSpeechViewModel(new TtsDownloadService(new HttpClient()), new WindowService(new EmptyServices()),
            files, new FolderHelper(), new AceStepAudioCppDownloadService(new HttpClient()))
        {
            UsageStore = new TtsUsageStore(Path.Combine(folder, "tts-usage.json")), UsageJobsRoot = Path.Combine(folder, "jobs"),
        };
        return vm;
    }

    [AvaloniaFact]
    public async Task QuotaRecoveryPreservesTimelineAndResumeDoesNotResendOrChargeCachedLine()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tts-recovery-ąć-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_TEST_PATH") ?? "ffmpeg";
        using var settings = new SettingsScope("General.FfmpegPath", "Video.TextToSpeech.CustomAudioEncoding",
            "Video.TextToSpeech.CustomAudioStereo", "Video.TextToSpeech.VadSilenceCompressionEnabled", "Video.TextToSpeech.HighQualityTimeStretchEnabled");
        var oldLibFfmpeg = Configuration.Settings.General.FFmpegLocation;
        Se.Settings.General.FfmpegPath = ffmpeg;
        Configuration.Settings.General.FFmpegLocation = ffmpeg;
        Se.Settings.Video.TextToSpeech.CustomAudioEncoding = string.Empty;
        Se.Settings.Video.TextToSpeech.CustomAudioStereo = false;
        Se.Settings.Video.TextToSpeech.VadSilenceCompressionEnabled = false;
        Se.Settings.Video.TextToSpeech.HighQualityTimeStretchEnabled = false;
        var host = new Window();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        var messages = new List<string>();
        timer.Tick += (_, _) =>
        {
            foreach (var dialog in host.OwnedWindows.OfType<MessageBox>().ToArray())
            {
                messages.Add(dialog.Title ?? string.Empty);
                dialog.Close(dialog.Title is "Zatrzymano generowanie TTS" or "Zapisany postęp TTS" ? MessageBoxResult.Yes : MessageBoxResult.OK);
            }
        };
        TextToSpeechViewModel? vm = null;
        try
        {
            host.Show(); timer.Start();
            var files = DispatchProxy.Create<IFileHelper, SaveFiles>();
            var srt = Path.Combine(folder, "pozostale.srt");
            var partialWave = Path.Combine(folder, "czesciowy.wav");
            ((SaveFiles)(object)files).Paths.Enqueue(srt);
            ((SaveFiles)(object)files).Paths.Enqueue(partialWave);
            vm = Build(folder, files);
            vm.Window = host;
            var engine = new FakeEngine();
            vm.SelectedEngine = engine;
            vm.SelectedVoice = (await engine.GetVoices(""))[0];
            var subtitle = new Subtitle();
            subtitle.Paragraphs.Add(new Paragraph("<i>Pierwsza</i>", 1000, 1700));
            subtitle.Paragraphs.Add(new Paragraph("Druga", 4000, 4700));
            subtitle.Paragraphs.Add(new Paragraph("Trzecia", 6000, 6700));
            vm.Initialize(subtitle, null, string.Empty, null, folder);
            var account = TtsUsageAccount.ForEngine(engine);
            vm.UsageStore.Configure(account, true, 1, 100, null);

            var stopped = await vm.GenerateSpeech(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken);
            Assert.Null(stopped);
            Assert.Equal(new[] { "Pierwsza", "Druga" }, engine.Sent);
            Assert.Equal(92, vm.UsageStore.Get(account).RemainingCharacters);
            Assert.Equal(0, vm.UsageStore.Get(account).AvailableCharacters);
            Assert.Contains("Zatrzymano generowanie TTS", messages);
            Assert.Contains("Zapisano odzyskaną pracę", messages);
            Assert.True(TtsAudioValidation.IsUsable(partialWave));
            using (var stream = File.OpenRead(partialWave))
            {
                var header = new WaveHeader2(stream);
                Assert.InRange((double)header.DataChunkSize / header.ByteRate, 6.69, 6.75);
                // Before the first subtitle the resulting PCM WAV must still contain silence.
                stream.Position = header.DataStartPosition;
                var beforeFirstCue = new byte[header.ByteRate / 2];
                stream.ReadExactly(beforeFirstCue);
                Assert.All(beforeFirstCue, b => Assert.Equal(0, b));
                stream.Position = header.DataStartPosition + header.ByteRate + header.ByteRate / 10;
                var duringFirstCue = new byte[header.ByteRate / 10];
                stream.ReadExactly(duringFirstCue);
                Assert.Contains(duringFirstCue, b => b != 0);
            }
            var remaining = File.ReadAllText(srt);
            Assert.DoesNotContain("Pierwsza", remaining);
            Assert.Contains("00:00:04,000 --> 00:00:04,700", remaining);
            Assert.Contains("00:00:06,000 --> 00:00:06,700", remaining);

            engine.RejectSecondLine = false;
            vm.UsageStore.Configure(account, true, 1, 100, 92); // user reconciles the provider's renewed allowance
            var resumed = await vm.GenerateSpeech(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.NotNull(resumed);
            Assert.Equal(3, resumed.Length);
            Assert.Equal(new[] { "Pierwsza", "Druga", "Druga", "Trzecia" }, engine.Sent);
            Assert.Equal(80, vm.UsageStore.Get(account).AvailableCharacters);
            Assert.All(resumed, r => Assert.True(TtsAudioValidation.IsUsable(r.CurrentFileName)));
            Assert.Contains("Zapisany postęp TTS", messages);
        }
        finally
        {
            timer.Stop(); host.Close();
            Configuration.Settings.General.FFmpegLocation = oldLibFfmpeg;
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task ActiveReservationIsNotSubtractedTwiceFromProjectedBalance()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tts-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var host = new Window();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TtsStepResult[]?>? generation = null;
        try
        {
            host.Show();
            var vm = Build(folder, DispatchProxy.Create<IFileHelper, SaveFiles>()); vm.Window = host;
            var engine = new FakeEngine { RejectSecondLine = false, Pause = release.Task };
            vm.SelectedEngine = engine; vm.SelectedVoice = (await engine.GetVoices(""))[0];
            var subtitle = new Subtitle(); subtitle.Paragraphs.Add(new Paragraph("Pierwsza", 1000, 2000));
            vm.Initialize(subtitle, null, string.Empty, null, folder);
            var account = TtsUsageAccount.ForEngine(engine);
            vm.UsageStore.Configure(account, true, 1, 100, null);
            generation = vm.GenerateSpeech(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (engine.Sent.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Single(engine.Sent);
            vm.RefreshUsageEstimate();
            Assert.Contains("Dostępne: 92", vm.UsageSummary);
            Assert.Contains("Po generowaniu: 92", vm.UsageSummary);
            release.SetResult();
            Assert.NotNull(await generation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
            if (generation != null)
            {
                try { await generation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
                catch { /* teardown after a failed assertion */ }
            }
            host.Close(); Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task InsufficientBalanceBlocksEverySynthesisRequest()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tts-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var host = new Window();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { foreach (var dialog in host.OwnedWindows.OfType<MessageBox>().ToArray()) dialog.Close(MessageBoxResult.OK); };
        try
        {
            host.Show(); timer.Start();
            var files = DispatchProxy.Create<IFileHelper, SaveFiles>();
            var vm = Build(folder, files); vm.Window = host;
            var engine = new FakeEngine(); vm.SelectedEngine = engine; vm.SelectedVoice = (await engine.GetVoices(""))[0];
            var subtitle = new Subtitle(); subtitle.Paragraphs.Add(new Paragraph("Za dużo znaków", 1000, 2000));
            vm.Initialize(subtitle, null, string.Empty, null, folder);
            vm.UsageStore.Configure(TtsUsageAccount.ForEngine(engine), true, 1, 100, 1);
            Assert.Null(await vm.GenerateSpeech(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Empty(engine.Sent);
            Assert.Equal(1, vm.UsageStore.Get(TtsUsageAccount.ForEngine(engine)).AvailableCharacters);
        }
        finally { timer.Stop(); host.Close(); Directory.Delete(folder, true); }
    }
}
