using System.Net.Http;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Download;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

public class LektorBatchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lektor-v50 ąć-" + Guid.NewGuid().ToString("N"));
    public LektorBatchTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { Directory.Delete(_folder, true); }
    private string FilePath(string name) => Path.Combine(_folder, name);
    private string Touch(string name) { var path = FilePath(name); File.WriteAllText(path, "fixture"); return path; }
    private string Srt(string name, params (string Text, int Start, int End)[] lines)
    {
        var subtitle = new Subtitle();
        foreach (var (text, start, end) in lines) subtitle.Paragraphs.Add(new Paragraph(text, start, end));
        subtitle.Renumber(); var path = FilePath(name);
        File.WriteAllText(path, new SubRip().ToText(subtitle, string.Empty), new UTF8Encoding(false)); return path;
    }
    private BatchMuxTable Table(params string[] videos)
    {
        var table = new BatchMuxTable { OutputFolder = _folder };
        table.SetColumn(MuxColumn.Video, videos); return table;
    }

    [Fact]
    public void IndependentAlphabeticColumnsKeepPairingDuringSwapInsertAndDelete()
    {
        var a = Touch("a.mkv"); var b = Touch("b.mkv"); var c = Touch("c.mkv");
        var x = Touch("x.ac3"); var y = Touch("y.ac3"); var z = Touch("z.ac3");
        var sub = Srt("only.srt", ("PL", 100, 400));
        var table = Table(c, a, b);
        table.SetColumn(MuxColumn.Audio, new[] { z, x, y });
        table.SetColumn(MuxColumn.Subtitles, new[] { sub });
        Assert.Equal(new[] { a, b, c }, table.Rows.Select(r => r.Video));
        Assert.Equal(new[] { x, y, z }, table.Rows.Select(r => r.Audio));
        table.Swap(MuxColumn.Audio, 0, 2);
        Assert.Equal(new[] { z, y, x }, table.Rows.Select(r => r.Audio));
        Assert.Equal(new[] { a, b, c }, table.Rows.Select(r => r.Video));
        table.InsertEmpty(MuxColumn.Audio, 1);
        Assert.Equal(new[] { z, "", y, x }, table.Rows.Select(r => r.Audio));
        Assert.Equal(new[] { a, b, c, "" }, table.Rows.Select(r => r.Video));
        Assert.Equal(new[] { sub, "", "", "" }, table.Rows.Select(r => r.Subtitles));
        table.DeleteCell(MuxColumn.Audio, 1);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(new[] { z, y, x }, table.Rows.Select(r => r.Audio));
        Assert.Equal(new[] { a, b, c }, table.Rows.Select(r => r.Video));
        Assert.Equal(Enumerable.Range(1, 3), table.Rows.Select(r => r.Number));
        Assert.EndsWith("b_PL.mkv", table.Rows[1].Output);
        Assert.Equal(3, table.ValidatePaths().Length); // Different basenames are intentionally valid.
    }

    [Fact]
    public void MissingRequiredFilesDuplicatesAndOutputCollisionsBlockWholeQueue()
    {
        var a = Touch("a.mkv"); var b = Touch("b.mkv"); var audio = Touch("mix.ac3");
        var table = Table(a, b); table.SetColumn(MuxColumn.Audio, [audio]);
        Assert.Throws<FileNotFoundException>(() => table.ValidatePaths());
        table.SetCell(MuxColumn.Audio, 1, audio);
        Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
        table.SetCell(MuxColumn.Audio, 1, Touch("mix2.flac"));
        File.WriteAllText(table.Rows[1].Output, "existing");
        Assert.Throws<IOException>(() => table.ValidatePaths());
        Assert.False(File.Exists(table.Rows[0].Output));
        Assert.Equal("existing", File.ReadAllText(table.Rows[1].Output));
    }

    [Fact]
    public async Task InvalidLaterMuxRowPreventsCreatingAnyEarlierResult()
    {
        var table = Table(Touch("a.mkv"), Touch("b.mkv"));
        table.SetColumn(MuxColumn.Audio, [Touch("a.ac3")]);
        await Assert.ThrowsAsync<FileNotFoundException>(() => table.RunAsync(TestContext.Current.CancellationToken));
        Assert.All(table.Rows, row => Assert.False(File.Exists(row.Output)));
    }

    [Fact]
    public void RemovingWholeRowsAndClearingKeepTheRemainingPairsTogether()
    {
        var table = Table(Touch("a.mkv"), Touch("b.mkv"));
        var audioA = Touch("a.ac3"); var audioB = Touch("b.ac3");
        table.SetColumn(MuxColumn.Audio, [audioA, audioB]);
        table.RemoveRow(0);
        var remaining = Assert.Single(table.Rows);
        Assert.EndsWith("b.mkv", remaining.Video); Assert.Equal(audioB, remaining.Audio); Assert.Equal(1, remaining.Number);
        table.Clear(); Assert.Empty(table.Rows);
        table.SetColumn(MuxColumn.Video, [Touch("new.mkv")]); Assert.Single(table.Rows); Assert.Empty(table.Rows[0].Audio);
    }

    [Fact]
    public void WindowsCaseInsensitiveResultNamesAndOverwritingAnySourceAreRejected()
    {
        var table = Table(Touch("Film.mkv"), Touch("film.mp4"));
        table.SetColumn(MuxColumn.Audio, [Touch("a.ac3"), Touch("b.ac3")]);
        Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
        table = Table(Touch("movie.mkv"));
        table.SetColumn(MuxColumn.Audio, [Touch("movie_PL.mkv")]);
        Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
    }

    [Theory]
    [InlineData("1\n00:00:00,100 --> 00:00:00,500\nPL\n", true)]
    [InlineData("not an SRT", false)]
    [InlineData("1\n00:00:00,500 --> 00:00:00,100\nPL\n", false)]
    public void OptionalSubtitleCellIsValidatedBeforeProcessing(string text, bool valid)
    {
        var table = Table(Touch("video.mkv")); table.SetColumn(MuxColumn.Audio, [Touch("mix.ac3")]);
        Assert.Single(table.ValidatePaths()); // Empty subtitle cell is allowed.
        var path = FilePath("forced.srt"); File.WriteAllText(path, text);
        table.SetColumn(MuxColumn.Subtitles, [path]);
        if (valid) Assert.Single(table.ValidatePaths());
        else Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
    }

    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }
    public class NoPickers : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Unexpected file dialog: " + targetMethod?.Name);
    }
    private TextToSpeechViewModel Build(FakeEngine engine, Window? host = null)
    {
        var vm = new TextToSpeechViewModel(new TtsDownloadService(new HttpClient()), new WindowService(new EmptyServices()),
            DispatchProxy.Create<IFileHelper, NoPickers>(), new FolderHelper(), new AceStepAudioCppDownloadService(new HttpClient()));
        vm.Initialize(new Subtitle(), string.Empty, null, _folder);
        vm.IsBatchMode = true; vm.Window = host;
        vm.UsageStore = new TtsUsageStore(FilePath("usage.json")); vm.UsageJobsRoot = FilePath("jobs");
        vm.Engines.Clear(); vm.Engines.Add(engine); vm.Voices.Add(new Voice(new object(), "Test shared voice"));
        vm.SelectedEngine = engine; vm.SelectedVoice = vm.Voices[0]; vm.BatchOutputFolder = _folder;
        return vm;
    }
    private sealed class FakeEngine : ITtsEngine
    {
        public string Name => "Batch test TTS";
        public string Description => "No network";
        public override string ToString() => Name;
        public bool HasLanguageParameter => false;
        public bool HasApiKey => false;
        public bool HasRegion => false;
        public bool HasModel => false;
        public bool HasKeyFile => false;
        public bool SupportsVoiceCloning => false;
        public bool SupportsPerLineVoiceCloning => false;
        public string? Reject { get; set; }
        public List<string> Sent { get; } = new();
        public Task<bool> IsInstalled(string? region) => Task.FromResult(true);
        public Task<string[]> GetRegions() => Task.FromResult(Array.Empty<string>());
        public Task<string[]> GetModels() => Task.FromResult(Array.Empty<string>());
        public Task<TtsLanguage[]> GetLanguages(Voice voice, string? model) => Task.FromResult(Array.Empty<TtsLanguage>());
        public bool IsVoiceInstalled(Voice voice) => true;
        public bool ImportVoice(string path) => false;
        public Task<Voice[]> GetVoices(string language) => Task.FromResult(new[] { new Voice(new object(), "Test shared voice") });
        public Task<Voice[]> RefreshVoices(string language, CancellationToken token) => GetVoices(language);
        public Task<TtsResult> Speak(string text, string folder, Voice voice, TtsLanguage? language, string? region, string? model, CancellationToken token)
        {
            Sent.Add(text);
            if (text == Reject) return Task.FromResult(new TtsResult { Error = true, FailureKind = TtsFailureKind.ProviderQuota, ErrorMessage = "Wyczerpano miesięczny limit znaków." });
            var file = Path.Combine(folder, Guid.NewGuid() + ".wav"); WriteTone(file); return Task.FromResult(new TtsResult(file, text));
        }
    }
    private static void WriteTone(string file)
    {
        const int rate = 16000, samples = 4800;
        using var w = new BinaryWriter(File.Create(file));
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        for (var i = 0; i < samples; i++) w.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
    }

    private static SettingsScope AudioSettings()
    {
        var scope = new SettingsScope("General.FfmpegPath", "Video.TextToSpeech.CustomAudioEncoding", "Video.TextToSpeech.CustomAudioStereo",
            "Video.TextToSpeech.VadSilenceCompressionEnabled", "Video.TextToSpeech.HighQualityTimeStretchEnabled", "Video.TextToSpeech.GenerationFolder",
            "Video.TextToSpeech.ProAudioChainEnabled", "Video.TextToSpeech.OutputSampleRate", "Video.TextToSpeech.SilencePaddingMs");
        Se.Settings.General.FfmpegPath = Environment.GetEnvironmentVariable("FFMPEG_TEST_PATH") ?? "ffmpeg";
        Configuration.Settings.General.FFmpegLocation = Se.Settings.General.FfmpegPath;
        var settings = Se.Settings.Video.TextToSpeech;
        settings.CustomAudioEncoding = string.Empty; settings.CustomAudioStereo = false; settings.VadSilenceCompressionEnabled = false;
        settings.HighQualityTimeStretchEnabled = false; settings.GenerationFolder = string.Empty; settings.ProAudioChainEnabled = false;
        settings.OutputSampleRate = 0; settings.SilencePaddingMs = 0;
        return scope;
    }

    [AvaloniaFact]
    public async Task WholeWavQueueBudgetBlocksRequestsUntilUserReducesQueue()
    {
        using var settings = AudioSettings(); var engine = new FakeEngine(); var vm = Build(engine);
        vm.AddBatchFiles([Srt("a.srt", ("Pierwsza", 1000, 1700)), Srt("b.srt", ("Druga", 3000, 3700))]);
        vm.UsageStore.Configure(TtsUsageAccount.ForEngine(engine), true, 1, 100, 8);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ExecuteBatchQueueAsync(TestContext.Current.CancellationToken));
        Assert.Empty(engine.Sent); Assert.False(File.Exists(FilePath("a.wav")));
        vm.BatchItems[1].Included = false;
        await vm.ExecuteBatchQueueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Pierwsza" }, engine.Sent);
        Assert.True(TtsAudioValidation.IsUsable(FilePath("a.wav")));
        Assert.False(File.Exists(FilePath("b.wav")));
        Assert.Equal(0, vm.UsageStore.Get(TtsUsageAccount.ForEngine(engine)).RemainingCharacters);
        Assert.False(vm.IsGenerating);
    }

    [AvaloniaFact]
    public async Task InvalidLaterSrtPreventsSendingAnyEarlierFile()
    {
        var engine = new FakeEngine(); var vm = Build(engine);
        var bad = Touch("bad.srt");
        vm.AddBatchFiles([Srt("good.srt", ("Poprawny", 100, 400)), bad]);
        await Assert.ThrowsAsync<InvalidDataException>(() => vm.ExecuteBatchQueueAsync(TestContext.Current.CancellationToken));
        Assert.Empty(engine.Sent); Assert.False(File.Exists(FilePath("good.wav")));
    }

    [AvaloniaFact]
    public async Task QuotaStopsFollowingFilesRecoversCurrentTimelineAndAutomaticallyReusesClips()
    {
        using var settings = AudioSettings();
        var host = new Window(); host.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        var messages = new List<string>();
        timer.Tick += (_, _) => { foreach (var box in host.OwnedWindows.OfType<MessageBox>().ToArray()) { messages.Add(box.Title ?? ""); box.Close(box.Title == "Zatrzymano generowanie TTS" ? MessageBoxResult.Yes : MessageBoxResult.OK); } };
        timer.Start();
        try
        {
            var engine = new FakeEngine { Reject = "STOP" }; var vm = Build(engine, host);
            vm.AddBatchFiles([Srt("episode.srt", ("<i>Pierwsza</i>", 1000, 1700), ("STOP", 4000, 4700), ("Trzecia", 6000, 6700)), Srt("next.srt", ("Następny", 2000, 2700))]);
            var account = TtsUsageAccount.ForEngine(engine); vm.UsageStore.Configure(account, true, 1, 100, null);
            await vm.ExecuteBatchQueueAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "Pierwsza", "STOP" }, engine.Sent);
            Assert.Contains("Zatrzymano generowanie TTS", messages);
            Assert.False(File.Exists(FilePath("next.wav"))); Assert.False(File.Exists(FilePath("episode.wav")));
            var remaining = BatchFileRules.ReadSrt(FilePath("episode_pozostale.srt"));
            Assert.Equal(new[] { "STOP", "Trzecia" }, remaining.Paragraphs.Select(p => p.Text));
            Assert.Equal(4000, remaining.Paragraphs[0].StartTime.TotalMilliseconds);
            AssertWaveTimeline(FilePath("episode_czesciowy.wav"), 6.7, 1.1);
            vm.UsageStore.Configure(account, true, 1, 100, 92); engine.Reject = null;
            vm.CheckBatchQueue(); Assert.Equal(1, vm.BatchItems[0].CachedClips);
            vm.SelectedVoice = new Voice(new object(), "Different voice");
            vm.RefreshUsageEstimate();
            Assert.Equal(0, vm.BatchItems[0].CachedClips);
            Assert.Equal(vm.BatchItems[0].TotalCharacters, vm.BatchItems[0].NeededCharacters);
            vm.SelectedVoice = new Voice(new object(), "Test shared voice"); vm.CheckBatchQueue();
            Assert.Equal(11, vm.BatchItems[0].NeededCharacters); // STOP + Trzecia, no second charge for Pierwsza.
            await vm.ExecuteBatchQueueAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
            Assert.Equal(1, engine.Sent.Count(s => s == "Pierwsza"));
            Assert.Equal(new[] { "Pierwsza", "STOP", "STOP", "Trzecia", "Następny" }, engine.Sent);
            AssertWaveTimeline(FilePath("episode.wav"), 6.7, 1.1);
            AssertWaveTimeline(FilePath("next.wav"), 2.7, 2.1);
            Assert.All(vm.BatchItems, row => Assert.True(row.Completed)); Assert.False(vm.IsGenerating); Assert.True(host.IsVisible);
            Assert.Equal(73, vm.UsageStore.Get(account).RemainingCharacters);
        }
        finally { timer.Stop(); host.Close(); }
    }

    private static void AssertWaveTimeline(string path, double seconds, double audibleTime)
    {
        Assert.True(TtsAudioValidation.IsUsable(path));
        using var stream = File.OpenRead(path); var header = new WaveHeader2(stream);
        Assert.InRange((double)header.DataChunkSize / header.ByteRate, seconds - 0.1, seconds + 0.1);
        // Standard merger output is signed 16-bit PCM in this fixture.
        stream.Position = header.DataStartPosition; var bytes = new byte[320]; stream.ReadExactly(bytes);
        Assert.All(bytes, b => Assert.Equal(0, b));
        stream.Position = header.DataStartPosition + (long)(audibleTime * header.ByteRate);
        stream.ReadExactly(bytes); Assert.Contains(bytes, b => b != 0);
    }

    [AvaloniaFact]
    public void WavReorderingAndOutputCollisionsAreVisibleBeforeStart()
    {
        var vm = Build(new FakeEngine());
        vm.AddBatchFiles([Srt("a.srt", ("A", 100, 400)), Srt("b.srt", ("B", 100, 400))]);
        vm.SelectedBatchItem = vm.BatchItems[1]; vm.BatchUpCommand.Execute(null);
        Assert.EndsWith("b.srt", vm.BatchItems[0].SourceFile);
        Assert.Equal(new[] { 1, 2 }, vm.BatchItems.Select(r => r.Number));
        Touch("b.wav"); Assert.Throws<IOException>(() => vm.CheckBatchQueue());
    }

    [AvaloniaFact]
    public void StatisticsDoNotResizePanelWhenBalanceAndWarningsChangeAndBatchWindowsAreMaximized()
    {
        var engine = new FakeEngine(); var vm = Build(engine); vm.IsBatchMode = false;
        var source = new Subtitle(); source.Paragraphs.Add(new Paragraph("Test", 100, 500)); vm.Initialize(source, string.Empty, null, _folder);
        var tts = new TextToSpeechWindow(vm); var content = (Control)tts.Content!; tts.Content = null;
        var host = new Window { Content = content, DataContext = vm, Width = 1400, Height = 1000 }; host.Show();
        try
        {
            vm.RefreshUsageEstimate(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var stats = content.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == "TtsUsageStatistics");
            var before = stats.Bounds; var panelBefore = content.Bounds;
            vm.UsageStore.Configure(TtsUsageAccount.ForEngine(engine), true, 1, 999999, 0);
            vm.UsageStore.MarkProviderQuotaExhausted(TtsUsageAccount.ForEngine(engine)); vm.RefreshUsageEstimate();
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(before, stats.Bounds); Assert.Equal(panelBefore, content.Bounds);
            Assert.Same(engine, vm.SelectedEngine);
            Assert.Equal(TtsUsageAccount.ForEngine(engine), vm.UsageStatistics.Single().Account);
            Assert.Equal("0", vm.UsageStatistics.Single().Available); Assert.Contains("wyczerpany", vm.UsageNotice);
            var batchVm = Build(new FakeEngine());
            batchVm.AddBatchFiles([Srt("podglad.srt", ("Test podglądu", 1000, 2000))]);
            var batch = new BatchTtsWindow(batchVm);
            var mux = new BatchMuxWindow(new FileHelper());
            var single = new LektorExportWindow(new FileHelper(), "", "");
            Assert.Equal(WindowState.Maximized, batch.WindowState); Assert.Equal(WindowState.Maximized, mux.WindowState); Assert.Equal(WindowState.Maximized, single.WindowState);
            var preview = Environment.GetEnvironmentVariable("LEKTOR_BATCH_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview);
                using var saved = new SettingsScope("Video.TextToSpeech.Engine", "Video.TextToSpeech.Voice");
                Se.Settings.Video.TextToSpeech.Engine = batchVm.SelectedEngine!.Name;
                Se.Settings.Video.TextToSpeech.Voice = batchVm.SelectedVoice!.Name;
                batch.Show(); mux.Show();
                mux.Table.OutputFolder = _folder;
                mux.Table.SetColumn(MuxColumn.Video, [Touch("01 Film.mkv"), Touch("02 Film.mkv")]);
                mux.Table.SetColumn(MuxColumn.Audio, [Touch("01 Miks PL.ac3"), Touch("02 Miks PL.ac3")]);
                mux.Table.SetColumn(MuxColumn.Subtitles, [Srt("01 wymuszone.srt", ("PL", 100, 400))]);
                Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var frame = batch.CaptureRenderedFrame()) frame!.Save(Path.Combine(preview, "batch-wav.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                using (var frame = mux.CaptureRenderedFrame()) frame!.Save(Path.Combine(preview, "batch-mkv.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                batch.Close(); mux.Close();
            }
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public async Task RealBatchMkvUsesReadyMixesPreservesVideoAndForcedFlagsAndKeepsWindowOpen()
    {
        using var settings = AudioSettings();
        using var muxSettings = new SettingsScope("Video.TextToSpeech.LektorMkvmergePath");
        Se.Settings.Video.TextToSpeech.LektorMkvmergePath = Environment.GetEnvironmentVariable("MKVMERGE_TEST_PATH") ?? "mkvmerge";
        var video = FilePath("video-a.mkv"); var video2 = FilePath("video-b.mkv"); var mix = FilePath("mix-a.ac3"); var mix2 = FilePath("mix-b.ac3");
        await RunFfmpeg(["-v", "error", "-f", "lavfi", "-i", "testsrc2=s=64x64:r=10:d=2", "-c:v", "ffv1", video]);
        File.Copy(video, video2);
        await RunFfmpeg(["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-c:a", "ac3", "-b:a", "224k", mix]);
        File.Copy(mix, mix2);
        var forced = Srt("forced.srt", ("Wymuszone PL", 500, 1500));
        var window = new BatchMuxWindow(DispatchProxy.Create<IFileHelper, NoPickers>()); window.Show();
        try
        {
            window.GetLogicalDescendants().OfType<TextBox>().Single(c => c.Name == "BatchMkvOutputFolder").Text = _folder;
            window.Table.SetColumn(MuxColumn.Video, [video2, video]);
            window.Table.SetColumn(MuxColumn.Audio, [mix2, mix]); window.Table.SetColumn(MuxColumn.Subtitles, [forced]);
            await window.RunAsync(true).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            foreach (var row in window.Table.Rows)
            {
                Assert.True(File.Exists(row.Output), row.Status);
                var output = await LektorMediaProbe.ReadAsync(row.Output, TestContext.Current.CancellationToken);
                var audio = Assert.Single(output.Tracks.Where(t => t.Type == "Audio"));
                Assert.Equal("pol", audio.Language); Assert.True(audio.Default);
                var before = await LektorMediaProbe.AuditAsync(row.Video, TestContext.Current.CancellationToken);
                var after = await LektorMediaProbe.AuditAsync(row.Output, TestContext.Current.CancellationToken);
                Assert.True(LektorMediaProbe.SameVideoTimeline(before[0], after[0]));
                if (row.Number == 1)
                {
                    var subtitle = Assert.Single(output.Tracks.Where(t => t.Type == "Subtitle"));
                    Assert.Equal("pol", subtitle.Language); Assert.True(subtitle.Forced && subtitle.Default);
                }
                else Assert.DoesNotContain(output.Tracks, t => t.Type == "Subtitle");
                Assert.Equal("Gotowe", row.Status);
            }
            Assert.True(window.IsVisible);
            Assert.True(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "StartBatchMux").IsEnabled);
        }
        finally { window.Close(); }
    }
    private static async Task RunFfmpeg(string[] args)
    {
        using var process = FfmpegGenerator.GetProcess(string.Empty, null);
        process.StartInfo.Arguments = string.Empty; foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        await process.StartAndWaitAsync(TestContext.Current.CancellationToken, TimeSpan.FromSeconds(30)); Assert.Equal(0, process.ExitCode);
    }
}
