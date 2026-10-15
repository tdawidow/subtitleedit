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
            var subtitleFile = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "ForcedSubtitleFile");
            Assert.True(string.IsNullOrEmpty(subtitleFile.Text));
            subtitleFile.Text = "odcinek_WYMUSZONE_PL.srt";
            window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ClearForcedSubtitles")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(string.IsNullOrEmpty(subtitleFile.Text));
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
        var mkvmerge = Environment.GetEnvironmentVariable("MKVMERGE_TEST_PATH");
        if (!string.IsNullOrEmpty(mkvmerge))
            Nikse.SubtitleEdit.Logic.Config.Se.Settings.Video.TextToSpeech.LektorMkvmergePath = mkvmerge;
    }

    [Fact]
    public void AttachedPicturesAreNotFilmTracksAndCodecErrorsIdentifyTheStream()
    {
        var source = LektorMediaProbe.Parse("""
            Stream #0:0: Video: h264, yuv420p, 64x64
            Stream #0:1(eng): Audio: aac, stereo
            Stream #0:2: Video: png, rgb24, 64x64 (attached pic)
            Stream #0:3: Attachment: none
            """);
        var mix = LektorMediaProbe.Parse("Stream #0:0: Audio: ac3, stereo");
        var output = LektorMediaProbe.Parse("""
            Stream #0:0: Video: h264, yuv420p, 64x64
            Stream #0:1(pol): Audio: ac3, stereo (default)
            Stream #0:2(eng): Audio: aac, stereo
            Stream #0:3: Attachment: ttf
            Stream #0:4: Video: png, rgb24, 64x64 (attached pic)
            """);
        // v4.1 expected png at output #1 and rejected this correct media layout.
        Assert.True(source.Tracks[2].IsAttachedPicture);
        Assert.False(source.Tracks[2].IsVideo || source.Tracks[2].IsMedia);
        LektorExportService.ValidateTracks(source, mix, output);

        var wrongCodec = LektorMediaProbe.Parse("""
            Stream #0:0: Video: hevc, yuv420p, 64x64
            Stream #0:1(pol): Audio: ac3, stereo (default)
            Stream #0:2(eng): Audio: aac, stereo
            """);
        var error = Assert.Throws<InvalidOperationException>(() => LektorExportService.ValidateTracks(source, mix, wrongCodec));
        Assert.Contains("#0", error.Message);
        Assert.Contains("h264", error.Message);
        Assert.Contains("hevc", error.Message);
    }

    [Fact]
    public async Task ExportPreservesCoverArtAndFontAttachmentsWithoutTreatingThemAsVideo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v411-okładki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (original, wave) = await MakeFixture(folder);
            var files = Path.Combine(AppContext.BaseDirectory, "Files");
            var source = Path.Combine(folder, "film z okładkami.mkv");
            var jpeg = Path.Combine(folder, "cover.jpg");
            await Run(["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=64x64", "-frames:v", "1", "-update", "1", jpeg]);
            await Run(["-v", "error", "-y", "-i", original, "-map", "0", "-c", "copy",
                "-attach", Path.Combine(files, "SeTrimTest.ttf"), "-metadata:s:t:1", "mimetype=font/ttf",
                "-attach", Path.Combine(files, "00_02_09_640__00_02_16_920.png"), "-metadata:s:t:2", "mimetype=image/png",
                "-attach", Path.Combine(files, "SeTrimTestCff.otf"), "-metadata:s:t:3", "mimetype=application/vnd.ms-opentype",
                "-attach", jpeg, "-metadata:s:t:4", "mimetype=image/jpeg", source]);
            var before = await LektorMediaProbe.ReadAsync(source, TestContext.Current.CancellationToken);
            Assert.Equal(1, before.Tracks.Count(t => t.IsVideo));
            Assert.Equal(2, before.Tracks.Count(t => t.IsAttachedPicture));
            var mix = Path.Combine(folder, "mix.ac3");
            await Run(["-v", "error", "-y", "-i", wave, "-c:a", "ac3", "-b:a", "224k", mix]);
            File.Delete(wave);
            var output = Path.Combine(folder, "wynik.mkv");
            await LektorExportService.ExportAsync(source, wave, mix, output, -1, new(),
                TestContext.Current.CancellationToken, useExistingMix: true);
            var after = await LektorMediaProbe.ReadAsync(output, TestContext.Current.CancellationToken);
            Assert.Equal(1, after.Tracks.Count(t => t.IsVideo));
            Assert.Equal(2, after.Tracks.Count(t => t.IsAttachedPicture));
            Assert.Equal(5, after.Tracks.Count(t => !t.IsMedia));
            Assert.Equal(await DecodeVideo(source), await DecodeVideo(output));
            var packets = await LektorMediaProbe.AuditAsync(output, TestContext.Current.CancellationToken);
            Assert.Equal(4, packets.Count); // Real film + PL + two original audio, no cover packets.
            Assert.Equal(40, packets[0].Count);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentControlRejectsMissingAndSameSizeCorruptedData(bool sameSizeCorruption)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v411-attachments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (source, _) = await MakeFixture(folder);
            var output = Path.Combine(folder, "bad.mkv");
            var args = new List<string> { "-v", "error", "-y", "-i", source, "-map", "0:V", "-map", "0:a", "-map", "0:s", "-c", "copy" };
            if (sameSizeCorruption)
            {
                var attachment = Path.Combine(folder, "attachment.txt");
                await File.WriteAllTextAsync(attachment, "Attachment destroyed", TestContext.Current.CancellationToken);
                Assert.Equal("Attachment preserved".Length, "Attachment destroyed".Length);
                args.AddRange(["-attach", attachment, "-metadata:s:t:0", "mimetype=text/plain"]);
            }
            args.Add(output);
            await Run(args.ToArray());
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LektorMkvMuxer.VerifyAttachmentsAsync(
                LektorMkvMuxer.ResolveExecutable(), source, output, TestContext.Current.CancellationToken));
            Assert.Contains(sameSizeCorruption ? "SHA-256" : "załączników", error.Message);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task AttachmentControlAcceptsWindowsPathsNormalisedToBasename()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v411-names-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (source, _) = await MakeFixture(folder);
            var withPath = Path.Combine(folder, "windows-path.mkv");
            await Run(["-v", "error", "-y", "-i", source, "-map", "0:V", "-map", "0:a", "-map", "0:s", "-c", "copy",
                "-attach", Path.Combine(folder, "attachment.txt"), "-metadata:s:t:0", "mimetype=text/plain",
                "-metadata:s:t:0", @"filename=C:\anime\fonts\attachment.txt", withPath]);
            // These two files have identical attachment data, but one uses the full
            // Windows path and the other the basename, as mkvmerge normalizes it.
            await LektorMkvMuxer.VerifyAttachmentsAsync(LektorMkvMuxer.ResolveExecutable(), withPath, source,
                TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void VideoTimelineRejectsRepeatedOrShiftedFrameTimes()
    {
        var source = new LektorMediaProbe.PacketExtent { VideoTimestamps = [0, 0.04, 0.08, 0.12] };
        Assert.True(LektorMediaProbe.SameVideoTimeline(source,
            new() { VideoTimestamps = [0, 0.041, 0.08, 0.12] }));
        Assert.False(LektorMediaProbe.SameVideoTimeline(source,
            new() { VideoTimestamps = [0, 0, 0, 0.12] }));
        Assert.False(LektorMediaProbe.SameVideoTimeline(source,
            new() { VideoTimestamps = [0.1, 0.14, 0.18, 0.22] }));
    }

    [Theory]
    [InlineData("libx264", false, false)]
    [InlineData("libx265", false, false)]
    [InlineData("libx264", true, false)]
    [InlineData("libx264", false, true)]
    [InlineData("libx265", false, true)]
    public async Task MkvToolNixPreservesMovingBFramesTimestampsAndSeeking(string encoder, bool variableRate, bool tenBit)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v41-ruch-ąć-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (fixture, wave) = await MakeFixture(folder);
            var moving = Path.Combine(folder, "ruchome źródło.mkv");
            var args = new List<string> { "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=24000/1001:d=4",
                "-i", fixture, "-map", "0:v", "-map", "1:a", "-map", "1:s", "-map", "1:t", "-map_metadata", "1",
                "-map_chapters", "1", "-c", "copy", "-c:v", encoder, "-preset", "fast", "-bf", "3", "-g", "24",
                "-vf", variableRate ? "setpts=PTS+0.4/TB+if(gte(N\\,48)\\,0.2/TB\\,0)" : "setpts=PTS+0.4/TB",
                "-fps_mode", "passthrough" };
            if (tenBit) args.AddRange(["-pix_fmt", "yuv420p10le"]);
            if (encoder == "libx265") args.AddRange(["-x265-params", "pools=1:frame-threads=1:log-level=error"]);
            args.Add(moving);
            await Run(args.ToArray());
            var mix = Path.Combine(folder, "gotowy lektor.ac3");
            await Run(["-v", "error", "-y", "-i", wave, "-ar", "48000", "-ac", "2", "-c:a", "ac3", "-b:a", "384k", mix]);
            var mixBytes = await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken);
            File.Delete(wave); // The reported scenario: mux an already correct AC3, no TTS/mixing.
            var output = Path.Combine(folder, "film PL.mkv");
            await LektorExportService.ExportAsync(moving, wave, mix, output, -1, new(),
                TestContext.Current.CancellationToken, useExistingMix: true);
            Assert.Equal(mixBytes, await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken));

            var before = await DecodeVideo(moving);
            var after = await DecodeVideo(output);
            Assert.InRange(before.Count, 95, 97);
            Assert.True(before.Distinct().Count() > 50, "Fixture must contain motion, not a still frame.");
            Assert.Equal(before, after); // Decode EVERY frame; equal hashes prove the picture is intact.
            foreach (var seek in new[] { "0.8", "2.0", "3.8" })
                Assert.Equal(await DecodeVideo(moving, seek), await DecodeVideo(output, seek));
            var sourceTimes = await LektorMediaProbe.AuditAsync(moving, TestContext.Current.CancellationToken);
            var outputTimes = await LektorMediaProbe.AuditAsync(output, TestContext.Current.CancellationToken);
            Assert.True(LektorMediaProbe.SameVideoTimeline(sourceTimes[0], outputTimes[0]));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task<List<string>> DecodeVideo(string file, string? seek = null)
    {
        var errors = new FfmpegOutputTail();
        var frames = new List<string>();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) => { if (e.Data != null) errors.Add(e.Data); });
        process.StartInfo.Arguments = string.Empty;
        var args = new List<string> { "-v", "error", "-xerror" };
        if (seek != null) args.AddRange(["-ss", seek]);
        args.AddRange(["-i", file, "-map", "0:V:0", "-fps_mode", "passthrough"]);
        if (seek != null) args.AddRange(["-frames:v", "1"]);
        args.AddRange(["-f", "framemd5", "-"]);
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.RedirectStandardOutput = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && !e.Data.StartsWith('#')) frames.Add(e.Data.Split(',')[^1].Trim());
        };
        await process.StartAndWaitAsync(TestContext.Current.CancellationToken, TimeSpan.FromMinutes(2));
        Assert.True(process.ExitCode == 0, "Video decode failed: " + errors);
        Assert.NotEmpty(frames);
        return frames;
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
    [InlineData("")]
    [InlineData("To nie jest plik napisów")]
    [InlineData("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nTEST\n")]
    [InlineData("1\n00:00:02,000 --> 00:00:01,000\nTEST\n")]
    public async Task InvalidForcedSubtitlesAreRejectedBeforeReplacingAnyOutput(string content)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v42-bad-srt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var srt = Path.Combine(folder, "wymuszone.srt");
            var mix = Path.Combine(folder, "mix.ac3");
            var output = Path.Combine(folder, "output.mkv");
            await File.WriteAllTextAsync(srt, content, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(mix, "existing mix", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(output, "existing MKV", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => LektorExportService.ExportAsync(
                Path.Combine(folder, "source.mkv"), Path.Combine(folder, "voice.wav"), mix, output,
                1, new(), TestContext.Current.CancellationToken, forcedSubtitleFile: srt));
            Assert.Equal("existing mix", await File.ReadAllTextAsync(mix, TestContext.Current.CancellationToken));
            Assert.Equal("existing MKV", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(folder, "*.partial-*"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task MissingForcedSubtitleAndOutputCollisionAreRejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v42-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var srt = Path.Combine(folder, "missing.srt");
            await Assert.ThrowsAsync<FileNotFoundException>(() => LektorExportService.ExportAsync(
                "source.mkv", "voice.wav", Path.Combine(folder, "mix.ac3"), Path.Combine(folder, "output.mkv"),
                1, new(), TestContext.Current.CancellationToken, forcedSubtitleFile: srt));
            await File.WriteAllTextAsync(srt, "1\n00:00:00,750 --> 00:00:01,500\nSYSTEM VR\n", TestContext.Current.CancellationToken);
            var original = await File.ReadAllBytesAsync(srt, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => LektorExportService.ExportAsync(
                "source.mkv", "voice.wav", srt, Path.Combine(folder, "output.mkv"),
                1, new(), TestContext.Current.CancellationToken, forcedSubtitleFile: srt));
            Assert.Equal(original, await File.ReadAllBytesAsync(srt, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForcedPolishSrtPreservesContentTimesAndOriginalMedia(bool useExistingMix)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v42-napisy ąć-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (fixture, wave) = await MakeFixture(folder);
            var video = Path.Combine(folder, "ruchomy film.mkv");
            await Run(["-v", "error", "-y", "-i", fixture, "-map", "0", "-c", "copy",
                "-c:v", "libx264", "-preset", "fast", "-bf", "3", "-g", "10", video]);
            var srt = Path.Combine(folder, "odcinek WYMUSZONE_PL.SRT");
            // UTF-8 without BOM is especially important on Windows: mkvmerge's default
            // input charset otherwise follows the system code page instead of the SRT.
            await File.WriteAllTextAsync(srt, "1\r\n00:00:00,750 --> 00:00:01,500\r\nZAŻÓŁĆ GĘŚLĄ JAŹŃ\r\n\r\n" +
                "2\r\n00:00:02,250 --> 00:00:03,750\r\n<i>ZDOBYTO UMIEJĘTNOŚĆ</i>\r\nPoziom 12\r\n",
                new System.Text.UTF8Encoding(false), TestContext.Current.CancellationToken);
            var sourceBytes = await File.ReadAllBytesAsync(video, TestContext.Current.CancellationToken);
            var subtitleBytes = await File.ReadAllBytesAsync(srt, TestContext.Current.CancellationToken);
            var waveBytes = await File.ReadAllBytesAsync(wave, TestContext.Current.CancellationToken);
            var mix = Path.Combine(folder, "polski miks.ac3");
            byte[]? mixBytes = null;
            if (useExistingMix)
            {
                await Run(["-v", "error", "-y", "-i", wave, "-ar", "48000", "-ac", "2", "-c:a", "ac3", "-b:a", "224k", mix]);
                mixBytes = await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken);
                File.Delete(wave);
            }
            var output = Path.Combine(folder, "wynik PL.mkv");
            await LektorExportService.ExportAsync(video, wave, mix, output, useExistingMix ? -1 : 2,
                new LektorExportOptions { BitrateKbps = 224 }, TestContext.Current.CancellationToken,
                useExistingMix: useExistingMix, forcedSubtitleFile: srt);
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(video, TestContext.Current.CancellationToken));
            Assert.Equal(subtitleBytes, await File.ReadAllBytesAsync(srt, TestContext.Current.CancellationToken));
            if (useExistingMix) Assert.Equal(mixBytes, await File.ReadAllBytesAsync(mix, TestContext.Current.CancellationToken));
            else Assert.Equal(waveBytes, await File.ReadAllBytesAsync(wave, TestContext.Current.CancellationToken));
            var result = await LektorMediaProbe.ReadAsync(output, TestContext.Current.CancellationToken);
            var subs = result.Tracks.Where(t => t.Type == "Subtitle").ToList();
            Assert.Equal(3, subs.Count);
            Assert.All(subs.Take(2), t => Assert.False(t.Default || t.Forced));
            Assert.Equal("pol", subs[^1].Language);
            Assert.Equal(LektorMkvMuxer.ForcedSubtitleTitle, subs[^1].Title);
            Assert.True(subs[^1].Default && subs[^1].Forced);
            var audio = result.Tracks.Where(t => t.Type == "Audio").ToList();
            Assert.Equal(3, audio.Count);
            Assert.Equal("pol", audio[0].Language);
            Assert.True(audio[0].Default && !audio[0].Forced);
            Assert.All(audio.Skip(1), t => Assert.False(t.Default || t.Forced));
            Assert.Equal(1, result.Chapters);
            Assert.Single(result.Tracks.Where(t => !t.IsMedia));
            Assert.Equal(await DecodeVideo(video), await DecodeVideo(output));
            foreach (var seek in new[] { "0.8", "2.0", "3.8" })
                Assert.Equal(await DecodeVideo(video, seek), await DecodeVideo(output, seek));

            var extracted = Path.Combine(folder, "extracted.srt");
            await Run(["-v", "error", "-y", "-copyts", "-i", output, "-map", $"0:{subs[^1].Index}", "-c:s", "copy", extracted]);
            var expected = new Subtitle(); var actual = new Subtitle();
            var format = new Nikse.SubtitleEdit.Core.SubtitleFormats.SubRip();
            format.LoadSubtitle(expected, (await File.ReadAllLinesAsync(srt, TestContext.Current.CancellationToken)).ToList(), srt);
            format.LoadSubtitle(actual, (await File.ReadAllLinesAsync(extracted, TestContext.Current.CancellationToken)).ToList(), extracted);
            Assert.Equal(expected.Paragraphs.Select(p => (p.StartTime.TotalMilliseconds, p.EndTime.TotalMilliseconds, p.Text)),
                actual.Paragraphs.Select(p => (p.StartTime.TotalMilliseconds, p.EndTime.TotalMilliseconds, p.Text)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task MixOnlyIgnoresTheOptionalSubtitleFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lektor-v42-mix-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (video, wave) = await MakeFixture(folder);
            var mix = Path.Combine(folder, "mix.ac3");
            var output = await LektorExportService.ExportAsync(video, wave, mix, null, 2,
                new LektorExportOptions { BitrateKbps = 224 }, TestContext.Current.CancellationToken,
                forcedSubtitleFile: Path.Combine(folder, "missing.srt"));
            Assert.Equal(mix, output);
            var info = await LektorMediaProbe.ReadAsync(mix, TestContext.Current.CancellationToken);
            Assert.Single(info.Tracks);
            Assert.Equal("Audio", info.Tracks[0].Type);
        }
        finally { Directory.Delete(folder, true); }
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
        await Run(["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=64x64:r=10:d=4",
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
