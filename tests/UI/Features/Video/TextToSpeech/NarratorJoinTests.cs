using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

public class NarratorJoinTests
{
    [Theory]
    [InlineData("S02E25_LEKTOR_PL_czesciowy.wav", "S02E25.wav")]
    [InlineData("S02E25_LEKTOR_PL_pozostale.ac3", "S02E25.wav")]
    [InlineData("Odcinek ąć.wav", "Odcinek ąć.wav")]
    public void ProposedNameEndsBeforeFirstUnderscore(string input, string expected) =>
        Assert.Equal(expected, NarratorJoinService.SuggestedName(input));

    [Theory]
    [InlineData("../source.wav")]
    [InlineData("folder\\source.wav")]
    [InlineData("C:source.wav")]
    [InlineData("source.ac3")]
    public void OutputNameCannotEscapeDestination(string name)
    {
        using var files = new LektorAudioFixture();
        Assert.Throws<ArgumentException>(() => NarratorJoinService.OutputPath(files.Folder, name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComplementaryPartsKeepTimelineSilenceLevelsAndLongestDuration(bool ac3First)
    {
        using var files = new LektorAudioFixture();
        var first = files.Wave("S02E25_czesciowy.wav", 2, 0.4, 0.8);
        if (ac3First)
        {
            var ac3 = files.FilePath("S02E25_czesciowy.ac3");
            await LektorAudioFixture.Run(["-v", "error", "-i", first, "-c:a", "ac3", "-b:a", "224k", ac3]); first = ac3;
        }
        // Mono and a different sample rate must retain voice level when converted to stereo.
        var second = files.Wave("S02E25_pozostale.wav", 2.5, 1.2, 1.6, 0.2, 44100, 1);
        var firstBytes = await File.ReadAllBytesAsync(first, TestContext.Current.CancellationToken);
        var secondBytes = await File.ReadAllBytesAsync(second, TestContext.Current.CancellationToken);
        var output = files.FilePath("S02E25.wav");
        await NarratorJoinService.JoinAsync(first, second, output, TestContext.Current.CancellationToken);
        var info = await LektorMediaProbe.ReadAsync(output, TestContext.Current.CancellationToken);
        Assert.InRange(info.Duration, 2.49, 2.51);
        Assert.Contains("pcm_s16le", Assert.Single(info.Tracks).Info);
        Assert.Contains("48000 Hz", info.Tracks[0].Info);
        Assert.Matches(@",\s*(stereo|2 channels?),", info.Tracks[0].Info);
        var samples = LektorAudioFixture.Pcm(output);
        Assert.Equal(240000, samples.Length);
        Assert.InRange(LektorAudioFixture.Rms(samples, 0, 0.3), 0, 0.0001);
        Assert.InRange(LektorAudioFixture.Rms(samples, 0.5, 0.7), 0.135, 0.148);
        Assert.InRange(LektorAudioFixture.Rms(samples, 0.9, 1.1), 0, 0.0001);
        Assert.InRange(LektorAudioFixture.Rms(samples, 1.3, 1.5), 0.135, 0.148);
        Assert.InRange(LektorAudioFixture.Rms(samples, 1.8, 2.4), 0, 0.0001);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first, TestContext.Current.CancellationToken));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(second, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(files.Folder, ".lektor-join-*"));
    }

    [Fact]
    public async Task SimultaneousAudioIsRejectedInsteadOfDuplicatingTheNarrator()
    {
        using var files = new LektorAudioFixture();
        var first = files.Wave("part1.wav", 2, 0.4, 0.9);
        var second = files.Wave("part2.wav", 2, 0.5, 1.0);
        var output = files.FilePath("combined.wav");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            NarratorJoinService.JoinAsync(first, second, output, TestContext.Current.CancellationToken));
        Assert.Contains("jednoczesne audio", error.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task SameInputExistingOutputAndCancellationNeverOverwriteFiles()
    {
        using var files = new LektorAudioFixture();
        var first = files.Wave("first.wav", 2, 0.4, 0.8);
        var second = files.Wave("second.wav", 2, 1.2, 1.6);
        var output = files.FilePath("combined.wav");
        await Assert.ThrowsAsync<ArgumentException>(() => NarratorJoinService.JoinAsync(first, first, output, TestContext.Current.CancellationToken));
        files.Touch("combined.wav");
        await Assert.ThrowsAsync<IOException>(() => NarratorJoinService.JoinAsync(first, second, output, TestContext.Current.CancellationToken));
        Assert.Equal("fixture", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => NarratorJoinService.JoinAsync(first, second, first, TestContext.Current.CancellationToken));
        File.Delete(output);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NarratorJoinService.JoinAsync(first, second, output, canceled.Token));
        Assert.False(File.Exists(output)); Assert.Empty(Directory.GetFiles(files.Folder, ".lektor-join-*"));
        using var duringJoin = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NarratorJoinService.JoinAsync(first, second, output, duringJoin.Token,
            text => { if (text.StartsWith("Łączenie części", StringComparison.Ordinal)) duringJoin.Cancel(); }));
        Assert.False(File.Exists(output)); Assert.Empty(Directory.GetFiles(files.Folder, ".lektor-join-*"));
    }

    [AvaloniaFact]
    public async Task JoinWindowUsesVisibleInputsAndRemainsReadyAfterCompletion()
    {
        using var files = new LektorAudioFixture();
        var window = new NarratorJoinWindow(new FileHelper()); window.Show();
        try
        {
            TextBox Field(string name) => window.GetLogicalDescendants().OfType<TextBox>().Single(c => c.Name == name);
            Field("NarratorPart1").Text = files.Wave("S02E25_czesciowy.wav", 2, 0.3, 0.7);
            Field("NarratorPart2").Text = files.Wave("S02E25_pozostale.wav", 2, 1.3, 1.7);
            Field("NarratorJoinFolder").Text = files.Folder;
            Assert.Equal("S02E25.wav", Field("NarratorJoinName").Text);
            await window.RunAsync(true);
            Assert.True(File.Exists(files.FilePath("S02E25.wav")));
            Assert.True(window.IsVisible); Assert.True(Field("NarratorPart1").IsEffectivelyEnabled);
            Assert.True(window.GetLogicalDescendants().OfType<Button>().Single(c => c.Name == "StartNarratorJoin").IsEnabled);
            var preview = Environment.GetEnvironmentVariable("LEKTOR_V51_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(preview, "narrator-join.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }
}
