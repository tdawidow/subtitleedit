using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using Nikse.SubtitleEdit.Logic.Media;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace UITests.Features.Video.TextToSpeech;

public class LektorExportTests
{
    [AvaloniaFact]
    public void ExportWindowConstructsAndShowsAllThreeActions()
    {
        var window = new LektorExportWindow(new FileHelper(), string.Empty, string.Empty);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var labels = window.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
            Assert.Contains("Miks + MKV", labels);
            Assert.Contains("Tylko miks", labels);
            Assert.Contains("MKV z istniejącego miksu…", labels);
            var preview = Environment.GetEnvironmentVariable("LEKTOR_PREVIEW_PATH");
            if (!string.IsNullOrEmpty(preview))
            {
                using var frame = window.CaptureRenderedFrame()!;
                using var file = File.Create(preview);
                frame.Save(file, PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    public LektorExportTests()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_TEST_PATH");
        if (!string.IsNullOrEmpty(ffmpeg)) Configuration.Settings.General.FFmpegLocation = ffmpeg;
    }

    [Fact]
    public void ProbeParsesLanguageGlobalIndexFlagsAndChapter()
    {
        var probe = LektorMediaProbe.Parse("""
            Duration: 01:02:03.45, start: 0.000000, bitrate: 400 kb/s
            Chapter #0:0: start 0.000000, end 5.000000
            Stream #0:0: Video: h264, yuv420p, 64x64
            Stream #0:2[0x3](eng): Audio: aac (LC), stereo (default)
            Metadata:
              title           : English original
            Stream #0:3(pol): Subtitle: subrip (default) (forced)
            Stream #0:4: Attachment: ttf
            """);
        Assert.Equal(3723.45, probe.Duration, 2);
        Assert.Equal(1, probe.Chapters);
        Assert.Equal(4, probe.Tracks.Count);
        Assert.Equal(2, probe.Tracks[1].Index);
        Assert.Equal("eng", probe.Tracks[1].Language);
        Assert.Equal("English original", probe.Tracks[1].Title);
        Assert.True(probe.Tracks[2].Default && probe.Tracks[2].Forced);
    }

    [Fact]
    public void InvalidBitrateAndNonFiniteGainAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new LektorExportOptions { Codec = LektorCodec.Mp3, BitrateKbps = 384 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new LektorExportOptions { OriginalPercent = double.NaN }.Validate());
    }

    [Fact]
    public async Task OutputCannotReplaceInputs()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => LektorExportService.ExportAsync(
            "source.mkv", "voice.wav", "voice.wav", null, 1, new(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => LektorExportService.ExportAsync(
            "source.mkv", "voice.wav", "mix.ac3", "source.mkv", 1, new(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(LektorCodec.Ac3, LektorMixMode.Fixed)]
    [InlineData(LektorCodec.Ac3, LektorMixMode.Dynamic)]
    [InlineData(LektorCodec.Aac, LektorMixMode.Fixed)]
    [InlineData(LektorCodec.Aac, LektorMixMode.Dynamic)]
    [InlineData(LektorCodec.Mp3, LektorMixMode.Fixed)]
    [InlineData(LektorCodec.Mp3, LektorMixMode.Dynamic)]
    [InlineData(LektorCodec.Flac, LektorMixMode.Fixed)]
    [InlineData(LektorCodec.Flac, LektorMixMode.Dynamic)]
    public async Task RealFfmpegPreservesOriginalStreamsAndIndependentOutputs(LektorCodec codec, LektorMixMode mode)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (video, wave) = await MakeFixture(folder);
            var sourceBytes = await File.ReadAllBytesAsync(video, TestContext.Current.CancellationToken);
            var waveBytes = await File.ReadAllBytesAsync(wave, TestContext.Current.CancellationToken);
            var options = new LektorExportOptions
            {
                Codec = codec, Mode = mode, BitrateKbps = codec == LektorCodec.Mp3 ? 320 : 384,
                OriginalPercent = mode == LektorMixMode.Fixed ? 25 : 100
            };
            var mix = Path.Combine(folder, "mix" + options.Extension);
            var mkv = Path.Combine(folder, "output.mkv");
            await LektorExportService.ExportAsync(video, wave, mix, mkv, 2, options, TestContext.Current.CancellationToken);
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(video, TestContext.Current.CancellationToken));
            Assert.Equal(waveBytes, await File.ReadAllBytesAsync(wave, TestContext.Current.CancellationToken));
            var info = await LektorMediaProbe.ReadAsync(mkv, TestContext.Current.CancellationToken);
            Assert.Equal(7, info.Tracks.Count); // 1 video, PL + 2 original audio, 2 subtitles, attachment
            Assert.Equal(1, info.Chapters);
            var audio = info.Tracks.Where(t => t.Type == "Audio").ToList();
            Assert.Equal("pol", audio[0].Language);
            Assert.Equal("jpn", audio[1].Language);
            Assert.Equal("eng", audio[2].Language);
            Assert.True(audio[0].Default);
            Assert.All(info.Tracks.Where(t => t.Type == "Subtitle"), t => Assert.False(t.Default || t.Forced));

            // Verify the selected SECOND audio, instead of accidentally using the first one.
            // In dynamic mode it returns to full volume in the narrator's silence; fixed mode stays quiet.
            if (codec == LektorCodec.Flac)
            {
                var rms = await MeasureRms(mix, 3.2, 0.4);
                // Mono -> stereo uses sqrt(0.5) per channel; sine RMS contributes another
                // sqrt(0.5), so the second source's 0.08 peak becomes 0.04 RMS per channel.
                var expectedDb = 20 * Math.Log10(0.04 * options.OriginalPercent / 100);
                Assert.InRange(rms, expectedDb - 0.25, expectedDb + 0.25);
                if (mode == LektorMixMode.Dynamic)
                {
                    var during = await MeasureRms(mix, 1.4, 0.4, "bandpass=f=220:t=h:w=20,");
                    var restored = await MeasureRms(mix, 3.2, 0.4, "bandpass=f=220:t=h:w=20,");
                    Assert.True(during < restored - 8, "Original audio should be reduced while the narrator speaks.");
                }
            }
            // Mux can be repeated without the WAV and without recoding the saved mix.
            File.Delete(wave);
            var mixBytes = await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken);
            await LektorExportService.ExportAsync(video, wave, mix, Path.Combine(folder, "remux.mkv"), -1,
                options, TestContext.Current.CancellationToken, useExistingMix: true);
            Assert.Equal(mixBytes, await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken));

            // Cancellation leaves an existing completed output untouched and cleans partial files.
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var oldMkv = await File.ReadAllBytesAsync(mkv, TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LektorExportService.ExportAsync(video,
                wave, mix, mkv, -1, options, cancelled.Token, useExistingMix: true));
            Assert.Equal(oldMkv, await File.ReadAllBytesAsync(mkv, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(folder, "*.partial-*"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task FailedMuxPreservesCompletedMixAndPreviouslySavedMkv()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v4-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (video, wave) = await MakeFixture(folder);
            // MP4 chapter data becomes a bin_data stream, which cannot be copied into MKV.
            // Export must report that incompatibility instead of silently dropping the stream.
            var unsupported = Path.Combine(folder, "source.mp4");
            await Run(["-v", "error", "-y", "-i", video, "-map", "0:v", "-map", "0:a", "-map", "0:s",
                "-c:v", "mpeg4", "-c:a", "aac", "-c:s", "mov_text", unsupported]);
            var mix = Path.Combine(folder, "mix.flac");
            await Run(["-v", "error", "-y", "-i", wave, "-c:a", "flac", mix]);
            var mkv = Path.Combine(folder, "output.mkv");
            await File.WriteAllTextAsync(mkv, "previous successful export", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => LektorExportService.ExportAsync(unsupported, wave,
                mix, mkv, -1, new(), TestContext.Current.CancellationToken, useExistingMix: true));
            Assert.Equal("previous successful export", await File.ReadAllTextAsync(mkv, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(wave) && File.Exists(mix));
            Assert.Empty(Directory.GetFiles(folder, "*.partial-*"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ShortNarratorIsPaddedWithoutTruncatingSource()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v4-short-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (video, wave) = await MakeFixture(folder);
            var shortWave = Path.Combine(folder, "short.wav");
            await Run(["-v", "error", "-y", "-i", wave, "-t", "1.5", shortWave]);
            var mix = Path.Combine(folder, "mix.flac");
            var mkv = Path.Combine(folder, "output.mkv");
            await LektorExportService.ExportAsync(video, shortWave, mix, mkv, 2,
                new LektorExportOptions { Codec = LektorCodec.Flac }, TestContext.Current.CancellationToken);
            var audio = await LektorMediaProbe.AuditAsync(mix, TestContext.Current.CancellationToken);
            Assert.InRange(audio[0].Seconds, 3.99, 4.01);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task<(string Video, string Wave)> MakeFixture(string folder)
    {
        var sub = Path.Combine(folder, "sub.srt"); var meta = Path.Combine(folder, "meta.txt");
        var attachment = Path.Combine(folder, "attachment.txt");
        await File.WriteAllTextAsync(sub, "1\n00:00:00,500 --> 00:00:02,000\nTest\n");
        await File.WriteAllTextAsync(meta, ";FFMETADATA1\ntitle=Lektor test\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=4000\ntitle=Chapter 1\n");
        await File.WriteAllTextAsync(attachment, "Attachment preserved");
        var video = Path.Combine(folder, "source.mkv"); var wave = Path.Combine(folder, "voice.wav");
        await Run(["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=64x64:r=10:d=4",
            "-f", "lavfi", "-i", "aevalsrc=0.002*sin(2*PI*330*t):s=48000:d=4",
            "-f", "lavfi", "-i", "aevalsrc=0.08*sin(2*PI*220*t):s=48000:d=4",
            "-i", sub, "-i", sub, "-i", meta, "-map", "0:v", "-map", "1:a", "-map", "2:a", "-map", "3:s", "-map", "4:s",
            "-c:v", "ffv1", "-c:a", "flac", "-c:s", "srt", "-map_metadata", "5", "-map_chapters", "5",
            "-metadata:s:a:0", "language=jpn", "-metadata:s:a:1", "language=eng", "-disposition:a:0", "default",
            "-disposition:s:0", "default+forced", "-attach", attachment, "-metadata:s:t:0", "mimetype=text/plain", video]);
        // The narrator outlasts the video, testing that the original video isn't cut and the
        // long narrator isn't truncated. Both short and long narration retain silence after speech.
        await Run(["-v", "error", "-y", "-f", "lavfi", "-i",
            @"aevalsrc=if(between(t\,1\,2)\,0.15*sin(2*PI*880*t)\,0):s=44100:d=6", "-c:a", "pcm_s16le", wave]);
        return (video, wave);
    }

    private static async Task<double> MeasureRms(string file, double start, double duration, string filters = "")
    {
        var log = new List<string>();
        await Run(["-hide_banner", "-nostdin", "-ss", start.ToString(CultureInfo.InvariantCulture), "-t",
            duration.ToString(CultureInfo.InvariantCulture), "-i", file, "-af", filters + "astats=metadata=0:reset=0", "-f", "null", "-"],
            line => { lock (log) log.Add(line); });
        var rms = log.Last(line => line.Contains("RMS level dB:", StringComparison.Ordinal));
        return double.Parse(rms[(rms.LastIndexOf(':') + 1)..].Trim(), CultureInfo.InvariantCulture);
    }

    private static async Task Run(string[] args, Action<string>? output = null)
    {
        var tail = new FfmpegOutputTail();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) =>
        {
            if (e.Data != null) { tail.Add(e.Data); output?.Invoke(e.Data); }
        });
        process.StartInfo.Arguments = string.Empty;
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        await process.StartAndWaitAsync(TestContext.Current.CancellationToken, TimeSpan.FromMinutes(2));
        Assert.True(process.ExitCode == 0, "FFmpeg test fixture failed: " + tail);
    }
}
