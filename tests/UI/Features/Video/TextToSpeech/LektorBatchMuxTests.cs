using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

public class LektorBatchMuxTests
{
    private static void Render()
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static TextBox Field(Window window, string name) =>
        window.GetLogicalDescendants().OfType<TextBox>().Single(c => c.Name == name);
    private static Button Button(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(c => c.Name == name);
    private static string Status(Window window) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Single(c => c.Name == "BatchMuxStatus").Text ?? "";
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Load(BatchMuxWindow window, MuxColumn column, string folder)
    {
        Field(window, "BatchSource" + column).Text = folder;
        Click(Button(window, "LoadBatchMux" + column)); Render();
    }
    private static void CellAction(BatchMuxWindow window, MuxColumn column, int number, string startsWith)
    {
        var cell = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "BatchMuxCell" + column + number);
        var action = cell.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Header!.ToString()!.StartsWith(startsWith, StringComparison.Ordinal));
        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Render();
    }
    private static void ConfigureMuxer() => Se.Settings.Video.TextToSpeech.LektorMkvmergePath =
        Environment.GetEnvironmentVariable("MKVMERGE_TEST_PATH") ?? "mkvmerge";
    private static void NoTemporaryFiles(string folder) =>
        Assert.DoesNotContain(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), p => Path.GetFileName(p).StartsWith('.'));

    [AvaloniaFact]
    public void RenderedMkvTableCanLoadAndEditColumnsLikeTheWorkingMixTable()
    {
        using var files = new LektorAudioFixture();
        var a = files.Touch("a.mkv"); var b = files.Touch("b.mkv");
        var x = files.Touch("x.wav"); var y = files.Touch("y.wav");
        var mix = new BatchMixWindow(new FileHelper()); mix.Show();
        var mux = new BatchMuxWindow(new FileHelper()); mux.Show();
        try
        {
            mix.Table.SetColumn(MixColumn.Video, [b, a]); Render();
            mix.Table.SetColumn(MixColumn.Narrator, [y, x]); Render();
            mix.Table.Swap(MixColumn.Narrator, 0, 1); Render();
            Assert.Equal(new[] { y, x }, mix.Table.Rows.Select(r => r.Narrator));

            mux.Table.SetColumn(MuxColumn.Video, [b, a]); Render();
            Assert.Contains(mux.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "a.mkv");
            // The old template receives null while a rendered container is being recycled.
            // This exact sequence failed even though model-only column tests passed.
            var failure = Record.Exception(() => mux.Table.SetColumn(MuxColumn.Audio, [y, x]));
            Assert.Null(failure); Render();
            mux.Table.Swap(MuxColumn.Audio, 0, 1); Render();
            Assert.Equal(new[] { y, x }, mux.Table.Rows.Select(r => r.Audio));
            mux.Table.InsertEmpty(MuxColumn.Audio, 1); Render();
            Assert.Equal(new[] { y, "", x }, mux.Table.Rows.Select(r => r.Audio));
            mux.Table.DeleteCell(MuxColumn.Audio, 1); Render();
            mux.Table.RemoveRow(1); Render(); Assert.Single(mux.Table.Rows);
            mux.Table.Clear(); Render(); Assert.Empty(mux.Table.Rows);
            mux.Table.SetColumn(MuxColumn.Video, [b]); Render();
            mux.Table.SetColumn(MuxColumn.Audio, [x]); Render();
            Assert.Single(mux.Table.Rows); Assert.True(mux.IsVisible);
        }
        finally { mux.Close(); mix.Close(); }
    }

    [AvaloniaFact]
    public void VisibleDestinationUpdatesImmediatelyAndClearingItRemovesOldOutputPaths()
    {
        using var files = new LektorAudioFixture();
        var window = new BatchMuxWindow(new FileHelper()); window.Show();
        try
        {
            var destination = Field(window, "BatchMkvOutputFolder");
            destination.Text = "\"" + files.Folder + "\"";
            // No dispatcher round trip: a paste followed immediately by Start must use it.
            Assert.Equal(files.Folder, window.Table.OutputFolder);
            window.Table.SetColumn(MuxColumn.Video, [files.Touch("film.mkv")]); Render();
            window.Table.SetColumn(MuxColumn.Audio, [files.Touch("mix.ac3")]); Render();
            var second = Path.Combine(files.Folder, "next"); Directory.CreateDirectory(second);
            destination.Text = second;
            Assert.Equal(Path.Combine(second, "film_PL.mkv"), Assert.Single(window.Table.Rows).Output);
            destination.Text = string.Empty;
            Assert.Empty(window.Table.OutputFolder); Assert.Empty(Assert.Single(window.Table.Rows).Output);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FolderButtonsAndCellMenusEditOnlyTheirColumnWithoutBreakingRenderedRows()
    {
        using var files = new LektorAudioFixture();
        var videos = Directory.CreateDirectory(files.FilePath("video")).FullName;
        var audio = Directory.CreateDirectory(files.FilePath("audio")).FullName;
        var subs = Directory.CreateDirectory(files.FilePath("subs")).FullName;
        var a = files.Touch("video/a.mkv"); var b = files.Touch("video/B.MKV"); files.Touch("video/ignore.txt");
        var x = files.Touch("audio/x.AC3"); var y = files.Touch("audio/y.flac"); files.Touch("audio/ignore.srt");
        var srt = files.Touch("subs/forced.srt");
        var window = new BatchMuxWindow(new FileHelper()); window.Show();
        try
        {
            Load(window, MuxColumn.Video, videos);
            Assert.Equal(new[] { a, b }, window.Table.Rows.Select(r => r.Video));
            Load(window, MuxColumn.Audio, audio);
            Load(window, MuxColumn.Subtitles, subs);
            Assert.Equal(new[] { x, y }, window.Table.Rows.Select(r => r.Audio));
            Assert.Equal(new[] { srt, "" }, window.Table.Rows.Select(r => r.Subtitles));

            CellAction(window, MuxColumn.Audio, 1, "Zamień z komórką niżej");
            Assert.Equal(new[] { y, x }, window.Table.Rows.Select(r => r.Audio));
            CellAction(window, MuxColumn.Audio, 2, "Wstaw pustą");
            Assert.Equal(new[] { y, "", x }, window.Table.Rows.Select(r => r.Audio));
            Assert.Equal(new[] { a, b, "" }, window.Table.Rows.Select(r => r.Video));
            CellAction(window, MuxColumn.Audio, 2, "Usuń komórkę");
            Assert.Equal(new[] { y, x }, window.Table.Rows.Select(r => r.Audio));
            CellAction(window, MuxColumn.Subtitles, 1, "Wyczyść komórkę");
            Assert.All(window.Table.Rows, r => Assert.Empty(r.Subtitles));
            Assert.Equal(new[] { a, b }, window.Table.Rows.Select(r => r.Video));
            window.GetLogicalDescendants().OfType<ListBox>().Single(c => c.Name == "BatchMuxTable").SelectedItem = window.Table.Rows[0];
            Click(Button(window, "RemoveBatchMuxRow")); Render();
            var remaining = Assert.Single(window.Table.Rows);
            Assert.Equal(b, remaining.Video); Assert.Equal(x, remaining.Audio); Assert.Equal(1, remaining.Number);
            Click(Button(window, "ClearBatchMux")); Render(); Assert.Empty(window.Table.Rows);
            Load(window, MuxColumn.Video, videos); Load(window, MuxColumn.Audio, audio);
            Assert.Equal(2, window.Table.Rows.Count); Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TwoRenderedQueuesPreserveOriginalTracksVideoTimestampsAndForcedSubtitleFlags()
    {
        using var files = new LektorAudioFixture(); ConfigureMuxer();
        var videos = Directory.CreateDirectory(files.FilePath("video")).FullName;
        var audio = Directory.CreateDirectory(files.FilePath("audio")).FullName;
        var subs = Directory.CreateDirectory(files.FilePath("subs")).FullName;
        var output = Directory.CreateDirectory(files.FilePath("result")).FullName;
        var output2 = Directory.CreateDirectory(files.FilePath("result2")).FullName;
        await files.Video("video/a.mkv");
        await LektorAudioFixture.Run(["-v", "error", "-f", "lavfi", "-i", "testsrc2=s=64x64:r=25:d=2",
            "-f", "lavfi", "-i", "sine=frequency=300:sample_rate=48000:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=900:sample_rate=48000:duration=2",
            "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "libx264", "-bf", "3", "-g", "50",
            "-c:a", "pcm_s16le", "-metadata:s:a:0", "language=jpn", "-metadata:s:a:1", "language=eng", files.FilePath("video/b.mkv")]);
        var wave = files.Wave("narrator.wav", 2.4, 0.5, 1.0);
        await LektorAudioFixture.Run(["-v", "error", "-i", wave, "-c:a", "ac3", "-b:a", "224k", files.FilePath("audio/a.ac3")]);
        await LektorAudioFixture.Run(["-v", "error", "-i", wave, "-c:a", "flac", files.FilePath("audio/b.flac")]);
        File.WriteAllText(files.FilePath("subs/a.srt"), "1\n00:00:00,400 --> 00:00:01,400\nWymuszone napisy PL\n");
        var inputs = Directory.GetFiles(videos).Concat(Directory.GetFiles(audio)).Concat(Directory.GetFiles(subs))
            .ToDictionary(p => p, p => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
        var window = new BatchMuxWindow(System.Reflection.DispatchProxy.Create<IFileHelper, LektorBatchTests.NoPickers>());
        Assert.Equal(WindowState.Maximized, window.WindowState); window.Show();
        try
        {
            Load(window, MuxColumn.Video, videos); Load(window, MuxColumn.Audio, audio); Load(window, MuxColumn.Subtitles, subs);
            // Start immediately after editing the visible output field.
            Field(window, "BatchMkvOutputFolder").Text = output;
            var run = window.RunAsync(true);
            Assert.False(Button(window, "StartBatchMux").IsEnabled);
            Assert.Equal("Stop", Button(window, "StopBatchMux").Content);
            await run;
            Assert.StartsWith("Zakończono: 2", Status(window));
            foreach (var row in window.Table.Rows)
            {
                Assert.Equal("Gotowe", row.Status); Assert.True(File.Exists(row.Output));
                var probe = await LektorMediaProbe.ReadAsync(row.Output, TestContext.Current.CancellationToken);
                var tracks = probe.Tracks.Where(t => t.Type == "Audio").ToArray();
                Assert.Equal(3, tracks.Length);
                Assert.Equal("pol", tracks[0].Language); Assert.True(tracks[0].Default);
                Assert.Equal(new[] { "jpn", "eng" }, tracks.Skip(1).Select(t => t.Language));
                Assert.All(tracks.Skip(1), t => Assert.False(t.Default));
                var before = await LektorMediaProbe.AuditAsync(row.Video, TestContext.Current.CancellationToken);
                var after = await LektorMediaProbe.AuditAsync(row.Output, TestContext.Current.CancellationToken);
                Assert.True(LektorMediaProbe.SameVideoTimeline(before[0], after[0]));
                Assert.Equal(before.Skip(1).Select(t => t.Count), after.Skip(2).Select(t => t.Count));
                if (row.Number == 1)
                {
                    var subtitle = Assert.Single(probe.Tracks, t => t.Type == "Subtitle");
                    Assert.Equal("pol", subtitle.Language); Assert.True(subtitle.Default && subtitle.Forced);
                }
                else Assert.DoesNotContain(probe.Tracks, t => t.Type == "Subtitle");
            }
            Assert.True(window.IsVisible); Assert.True(Button(window, "StartBatchMux").IsEnabled);
            var preview = Environment.GetEnvironmentVariable("LEKTOR_V52_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview); Render();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(preview, "batch-mkv.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            Click(Button(window, "ClearBatchMux")); Render();
            Load(window, MuxColumn.Video, videos); Load(window, MuxColumn.Audio, audio);
            Field(window, "BatchMkvOutputFolder").Text = output2;
            await window.RunAsync(true);
            Assert.StartsWith("Zakończono: 2", Status(window));
            Assert.All(window.Table.Rows, row => { Assert.Equal("Gotowe", row.Status); Assert.StartsWith(output2, row.Output); });
            Assert.Equal(2, Directory.GetFiles(output, "*.mkv").Length);
            Assert.Equal(2, Directory.GetFiles(output2, "*.mkv").Length);
            Assert.True(window.IsVisible); Assert.True(Button(window, "StartBatchMux").IsEnabled);
            foreach (var (path, hash) in inputs) Assert.Equal(hash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            NoTemporaryFiles(files.Folder);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ErrorKeepsWindowOpenAndRestoresControlsWithoutReusingAnInvisibleDestination()
    {
        using var files = new LektorAudioFixture();
        var window = new BatchMuxWindow(new FileHelper()); window.Show();
        try
        {
            window.Table.SetColumn(MuxColumn.Video, [files.Touch("film.mkv")]); Render();
            window.Table.SetColumn(MuxColumn.Audio, [files.Touch("mix.ac3")]); Render();
            // Simulate a stale model value; the visible, empty field must win at Start.
            window.Table.OutputFolder = files.Folder; window.Table.Rebuild(); Render();
            await window.RunAsync(true);
            Assert.StartsWith("Zatrzymano:", Status(window)); Assert.Contains("katalog", Status(window));
            Assert.Empty(window.Table.OutputFolder); Assert.Empty(Assert.Single(window.Table.Rows).Output);
            Assert.True(window.IsVisible); Assert.True(Button(window, "StartBatchMux").IsEnabled);
            Assert.True(Button(window, "CheckBatchMux").IsEnabled); Assert.Equal("Zamknij", Button(window, "StopBatchMux").Content);
            Assert.True(Field(window, "BatchSourceVideo").IsEffectivelyEnabled);
            Assert.False(File.Exists(files.FilePath("film_PL.mkv")));
        }
        finally { window.Close(); }
    }

    private static async Task<BatchMuxTable> TwoRows(LektorAudioFixture files)
    {
        ConfigureMuxer(); var table = new BatchMuxTable { OutputFolder = files.Folder };
        table.SetColumn(MuxColumn.Video, [await files.Video("a.mkv", false), await files.Video("b.mkv", false)]);
        table.SetColumn(MuxColumn.Audio, [files.Wave("a.wav", 2.4, 0.5, 1.0), files.Wave("b.wav", 2.4, 0.5, 1.0)]);
        return table;
    }

    [Fact]
    public async Task StopAfterFirstMkvPreservesCompletedFileAndMarksNextRowInterrupted()
    {
        using var files = new LektorAudioFixture(); var table = await TwoRows(files);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => table.RunAsync(cancel.Token, text =>
        {
            if (text.StartsWith("1/2: zapisano", StringComparison.Ordinal)) cancel.Cancel();
        }));
        Assert.Equal("Gotowe", table.Rows[0].Status); Assert.True(File.Exists(table.Rows[0].Output));
        Assert.Equal("Przerwano", table.Rows[1].Status); Assert.False(File.Exists(table.Rows[1].Output));
        NoTemporaryFiles(files.Folder);
    }

    [Fact]
    public async Task StopDuringVerificationRemovesPartialMkvAndDoesNotStartNextRow()
    {
        using var files = new LektorAudioFixture(); var table = await TwoRows(files);
        using var cancel = new CancellationTokenSource(); var reachedVerification = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => table.RunAsync(cancel.Token, text =>
        {
            if (text.StartsWith("1/2: Kontrola ścieżek", StringComparison.Ordinal)) { reachedVerification = true; cancel.Cancel(); }
        }));
        Assert.True(reachedVerification); Assert.Equal("Przerwano", table.Rows[0].Status);
        Assert.All(table.Rows, row => Assert.False(File.Exists(row.Output))); NoTemporaryFiles(files.Folder);
    }

    [Fact]
    public async Task OutputCreatedAfterPreflightIsPreservedAndStopsRemainingQueue()
    {
        using var files = new LektorAudioFixture(); var table = await TwoRows(files); var collided = false;
        await Assert.ThrowsAsync<IOException>(() => table.RunAsync(TestContext.Current.CancellationToken, text =>
        {
            if (!collided && text.StartsWith("1/2: Tworzenie MKV", StringComparison.Ordinal))
            { File.WriteAllText(table.Rows[0].Output, "Nie nadpisuj"); collided = true; }
        }));
        Assert.True(collided); Assert.Equal("Nie nadpisuj", File.ReadAllText(table.Rows[0].Output));
        Assert.Equal("Błąd — kolejka zatrzymana", table.Rows[0].Status);
        Assert.False(File.Exists(table.Rows[1].Output)); NoTemporaryFiles(files.Folder);
    }

    [Fact]
    public async Task InvalidLaterMixIsIdentifiedDuringPreflightBeforeAnyMkvIsCreated()
    {
        using var files = new LektorAudioFixture(); var table = await TwoRows(files);
        table.SetCell(MuxColumn.Audio, 1, files.Touch("broken.ac3"));
        var progress = new List<string>();
        await Assert.ThrowsAsync<InvalidDataException>(() => table.RunAsync(TestContext.Current.CancellationToken, progress.Add));
        Assert.Contains(progress, text => text.StartsWith("2/2: Kontrola plików", StringComparison.Ordinal));
        Assert.StartsWith("Błąd kontroli:", table.Rows[1].Status);
        Assert.All(table.Rows, row => Assert.False(File.Exists(row.Output))); NoTemporaryFiles(files.Folder);
    }
}
