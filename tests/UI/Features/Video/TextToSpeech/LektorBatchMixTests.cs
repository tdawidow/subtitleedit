using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

public class LektorBatchMixTests
{
    [Fact]
    public void IndependentColumnsSwapInsertDeleteAndRemoveKeepVisiblePairs()
    {
        using var files = new LektorAudioFixture();
        var a = files.Touch("a.mkv"); var b = files.Touch("b.mkv"); var c = files.Touch("c.mkv");
        var x = files.Touch("x.wav"); var y = files.Touch("y.wav"); var z = files.Touch("z.wav");
        var table = new BatchMixTable { OutputFolder = files.Folder };
        table.SetColumn(MixColumn.Video, [c, a, b]); table.SetColumn(MixColumn.Narrator, [z, x, y]);
        Assert.Equal(new[] { a, b, c }, table.Rows.Select(r => r.Video));
        Assert.Equal(new[] { x, y, z }, table.Rows.Select(r => r.Narrator));
        table.Swap(MixColumn.Narrator, 0, 2);
        Assert.Equal(new[] { z, y, x }, table.Rows.Select(r => r.Narrator));
        Assert.Equal(new[] { a, b, c }, table.Rows.Select(r => r.Video));
        table.InsertEmpty(MixColumn.Narrator, 1);
        Assert.Equal(new[] { z, "", y, x }, table.Rows.Select(r => r.Narrator));
        Assert.Equal(new[] { a, b, c, "" }, table.Rows.Select(r => r.Video));
        Assert.Throws<FileNotFoundException>(() => table.ValidatePaths());
        table.DeleteCell(MixColumn.Narrator, 1); table.RemoveRow(1);
        Assert.Equal(new[] { a, c }, table.Rows.Select(r => r.Video));
        Assert.Equal(new[] { z, x }, table.Rows.Select(r => r.Narrator));
        Assert.EndsWith("a_MiksPL.ac3", table.Rows[0].Output);
        Assert.Equal(2, table.ValidatePaths().Length);
        table.Clear(); Assert.Empty(table.Rows);
    }

    [Fact]
    public void DuplicateInputsMissingPairsAndWindowsOutputCollisionsBlockStart()
    {
        using var files = new LektorAudioFixture();
        var table = new BatchMixTable { OutputFolder = files.Folder };
        table.SetColumn(MixColumn.Video, [files.Touch("Film.mkv"), files.Touch("film.mp4")]);
        var a = files.Touch("a.wav"); var b = files.Touch("b.wav");
        table.SetColumn(MixColumn.Narrator, [a]); Assert.Throws<FileNotFoundException>(() => table.ValidatePaths());
        table.SetCell(MixColumn.Narrator, 1, a); Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
        table.SetCell(MixColumn.Narrator, 1, b); Assert.Throws<InvalidDataException>(() => table.ValidatePaths());
        table.SetCell(MixColumn.Video, 1, files.Touch("different.mkv"));
        var output = table.Rows[0].Output; File.WriteAllText(output, "existing");
        Assert.Throws<IOException>(() => table.ValidatePaths()); Assert.Equal("existing", File.ReadAllText(output));
    }

    [Fact]
    public async Task OriginalTrackSelectionFollowsItsVideoDuringReorderingAndResetsForReplacement()
    {
        using var files = new LektorAudioFixture();
        var a = await files.Video("a.mkv"); var b = await files.Video("b.mkv"); var c = await files.Video("c.mkv");
        var table = new BatchMixTable(); table.SetColumn(MixColumn.Video, [a, b]);
        await table.ReadTracksAsync(TestContext.Current.CancellationToken);
        Assert.All(table.Rows, r => Assert.Null(r.SelectedTrack));
        table.Rows[0].SelectedTrack = table.Rows[0].AudioTracks[1];
        table.Rows[1].SelectedTrack = table.Rows[1].AudioTracks[0];
        table.Swap(MixColumn.Video, 0, 1);
        Assert.Equal(b, table.Rows[0].Video); Assert.Equal("jpn", table.Rows[0].SelectedTrack!.Language);
        Assert.Equal(a, table.Rows[1].Video); Assert.Equal("eng", table.Rows[1].SelectedTrack!.Language);
        table.SetCell(MixColumn.Video, 1, c);
        Assert.Null(table.Rows[1].SelectedTrack); Assert.Empty(table.Rows[1].AudioTracks);
        await table.ReadTracksAsync(TestContext.Current.CancellationToken);
        Assert.Null(table.Rows[1].SelectedTrack);
        // Replacing a file at the same path must also invalidate its old selection.
        table.Rows[1].SelectedTrack = table.Rows[1].AudioTracks[1];
        File.SetLastWriteTimeUtc(c, File.GetLastWriteTimeUtc(c).AddSeconds(5));
        table.Rebuild(); Assert.Null(table.Rows[1].SelectedTrack);
    }

    [Theory]
    [InlineData(LektorCodec.Ac3)]
    [InlineData(LektorCodec.Flac)]
    public async Task BatchMatchesTheExistingSingleMixerWithExplicitSecondAudioTrackAndNoMkvToolNix(LektorCodec codec)
    {
        using var files = new LektorAudioFixture();
        Se.Settings.Video.TextToSpeech.LektorMkvmergePath = files.FilePath("no-such-mkvmerge.exe");
        var a = await files.Video("a.mkv"); var b = await files.Video("b.mkv");
        var narrator = files.Wave("lektor.wav", 2.4, 0.7, 1.2);
        var narrator2 = files.Wave("lektor2.wav", 2.4, 0.7, 1.2);
        var table = new BatchMixTable { OutputFolder = files.Folder,
            Options = new LektorExportOptions { Codec = codec, BitrateKbps = 224, Mode = LektorMixMode.Dynamic } };
        table.SetColumn(MixColumn.Video, [b, a]); table.SetColumn(MixColumn.Narrator, [narrator2, narrator]);
        await table.ReadTracksAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => table.PreflightAsync(TestContext.Current.CancellationToken));
        foreach (var row in table.Rows) row.SelectedTrack = row.AudioTracks.Single(t => t.Language == "eng");
        var expected = files.FilePath("single" + table.Options.Extension);
        await LektorExportService.ExportAsync(a, narrator, expected, null, 2, table.Options, TestContext.Current.CancellationToken);
        await table.RunAsync(TestContext.Current.CancellationToken);
        var single = await File.ReadAllBytesAsync(expected, TestContext.Current.CancellationToken);
        foreach (var row in table.Rows)
        {
            Assert.Equal("Gotowe", row.Status);
            Assert.Equal(single, await File.ReadAllBytesAsync(row.Output, TestContext.Current.CancellationToken));
            var info = await LektorMediaProbe.ReadAsync(row.Output, TestContext.Current.CancellationToken);
            Assert.Equal("Audio", Assert.Single(info.Tracks).Type);
            Assert.InRange(info.Duration, 2.39, 2.45);
        }
        Assert.DoesNotContain(Directory.GetFiles(files.Folder, "*.mkv"), f => f != a && f != b);
        Assert.Empty(Directory.GetFiles(files.Folder, ".batch-mix-*"));
    }

    [Fact]
    public async Task InvalidLaterNarratorPreventsAnyEarlierMix()
    {
        using var files = new LektorAudioFixture();
        var table = new BatchMixTable { OutputFolder = files.Folder };
        table.SetColumn(MixColumn.Video, [await files.Video("a.mkv", false), await files.Video("b.mkv", false)]);
        table.SetColumn(MixColumn.Narrator, [files.Wave("a.wav", 2, 0.5, 0.9), files.Touch("b.wav")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => table.RunAsync(TestContext.Current.CancellationToken));
        Assert.All(table.Rows, row => Assert.False(File.Exists(row.Output)));
        Assert.Empty(Directory.GetFiles(files.Folder, ".batch-mix-*"));
    }

    [Fact]
    public async Task StopPreservesCompletedMixAndDoesNotStartNextRow()
    {
        using var files = new LektorAudioFixture();
        var table = new BatchMixTable { OutputFolder = files.Folder };
        table.SetColumn(MixColumn.Video, [await files.Video("a.mkv", false), await files.Video("b.mkv", false)]);
        table.SetColumn(MixColumn.Narrator, [files.Wave("a.wav", 2, 0.5, 0.9), files.Wave("b.wav", 2, 0.5, 0.9)]);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => table.RunAsync(cancel.Token, text =>
        {
            if (text.StartsWith("1/2: zapisano", StringComparison.Ordinal)) cancel.Cancel();
        }));
        Assert.Equal("Gotowe", table.Rows[0].Status); Assert.True(File.Exists(table.Rows[0].Output));
        Assert.Equal("Przerwano", table.Rows[1].Status); Assert.False(File.Exists(table.Rows[1].Output));
        Assert.Empty(Directory.GetFiles(files.Folder, ".batch-mix-*"));
    }

    [AvaloniaFact]
    public async Task BatchMixWindowIsMaximizedRetainsSelectionAndStaysOpenAfterSuccess()
    {
        using var files = new LektorAudioFixture();
        Se.Settings.Video.TextToSpeech.LektorExport = new LektorExportOptions { BitrateKbps = 224 };
        var window = new BatchMixWindow(new FileHelper());
        Assert.Equal(WindowState.Maximized, window.WindowState); window.Show();
        try
        {
            window.GetLogicalDescendants().OfType<TextBox>().Single(c => c.Name == "BatchMixOutputFolder").Text = files.Folder;
            window.Table.SetColumn(MixColumn.Video, [await files.Video("film.mkv")]);
            window.Table.SetColumn(MixColumn.Narrator, [files.Wave("lektor.wav", 2, 0.5, 1.0)]);
            await window.RunAsync(false, tracksOnly: true);
            var row = Assert.Single(window.Table.Rows); row.SelectedTrack = row.AudioTracks[1];
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var visibleCombos = window.GetVisualDescendants().OfType<ComboBox>().Where(c => c.Name == "BatchMixAudioTrack").ToArray();
            Assert.True(visibleCombos.Length == 1, $"Expected the row's audio selector; window bounds: {window.Bounds}, row count: {window.Table.Rows.Count}");
            var combo = visibleCombos[0];
            Assert.Equal(row.SelectedTrack, combo.SelectedItem);
            var parameters = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Assert.Equal(6, parameters.Length);
            foreach (var parameter in parameters)
            {
                var viewport = parameter.GetVisualAncestors().OfType<ScrollViewer>().First();
                var point = parameter.TranslatePoint(default, viewport);
                Assert.True(point.HasValue && point.Value.Y >= 0 && point.Value.Y + parameter.Bounds.Height <= viewport.Bounds.Height + 1,
                    "All shared mixing parameters should fit in the full-size window.");
            }
            await window.RunAsync(true);
            Assert.True(window.Table.Rows.Count == 1,
                window.GetLogicalDescendants().OfType<TextBlock>().Single(c => c.Name == "BatchMixStatus").Text);
            row = Assert.Single(window.Table.Rows);
            Assert.Equal("Gotowe", row.Status); Assert.True(File.Exists(row.Output));
            Assert.Equal("eng", row.SelectedTrack!.Language); Assert.True(window.IsVisible);
            Assert.True(window.GetLogicalDescendants().OfType<Button>().Single(c => c.Name == "StartBatchMix").IsEnabled);
            var preview = Environment.GetEnvironmentVariable("LEKTOR_V51_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(preview, "batch-mix.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }
}
