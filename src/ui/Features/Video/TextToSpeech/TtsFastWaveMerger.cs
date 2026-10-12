using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech;

/// <summary>
/// Mixes TTS clips in 60-second timeline windows instead of re-encoding the
/// entire growing WAV for every subtitle. Overlapping clips, including clips
/// crossing window boundaries, are mixed additively at their original offsets.
/// All windows use 44.1 kHz stereo PCM so their final concat is stream-copy only.
/// </summary>
internal static class TtsFastWaveMerger
{
    private const double WindowSeconds = 60.0;
    private const int SampleRate = 44100;

    private sealed record Clip(string FileName, double Start, double Duration)
    {
        public double End => Start + Duration;
    }

    internal static async Task<string> MergeAsync(
        IReadOnlyList<TtsStepResult> stepResults,
        string silenceFileName,
        string waveFolder,
        CancellationToken cancellationToken,
        Action<int, int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Probe only once per source file. WAV is the usual input, but a failed
        // speed-adjustment may legitimately leave an MP3 or another audio format.
        var clips = await Task.Run(() => GetClips(stepResults, cancellationToken), cancellationToken);
        if (clips.Count == 0)
        {
            return silenceFileName;
        }

        var baseDuration = FfmpegMediaInfo.Parse(silenceFileName).Duration?.TotalSeconds ?? 0;
        if (baseDuration <= 0)
        {
            throw new InvalidOperationException("Unable to read the silent base WAV duration.");
        }

        // The legacy amix uses 'longest', so a clip that ends beyond the video
        // must not be truncated at the end of the silent base track.
        var duration = Math.Max(baseDuration, clips.Max(c => c.End));
        var windowCount = checked((int)Math.Ceiling(duration / WindowSeconds));
        if (windowCount == 0 || windowCount > 20000)
        {
            throw new InvalidOperationException("Invalid duration for TTS WAV merge.");
        }

        var scratchFolder = Path.Combine(waveFolder, "tts-merge-v3-" + Guid.NewGuid().ToString("N"));
        var finalFile = Path.Combine(waveFolder, "tts-merge-v3-" + Guid.NewGuid().ToString("N") + ".wav");
        Directory.CreateDirectory(scratchFolder);

        try
        {
            var windows = new List<string>(windowCount);
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < windowCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var windowStart = i * WindowSeconds;
                var windowDuration = Math.Min(WindowSeconds, duration - windowStart);
                var windowEnd = windowStart + windowDuration;

                // A clip started in a previous window may still be speaking.
                // Include it again, with atrim starting at the correct sample.
                var active = clips.Where(c => c.Start < windowEnd - 0.0001
                                            && c.End > windowStart + 0.0001).ToList();

                // Avoid Windows' 32767-character process-command limit and huge
                // filter graphs; unexpectedly dense subtitles fall back to the
                // original merger rather than silently dropping any clips.
                if (active.Count > 96)
                {
                    throw new InvalidOperationException(
                        $"TTS merge window {i + 1} has {active.Count} overlapping clips; the safe per-window limit is 96.");
                }

                var outputFile = Path.Combine(scratchFolder, $"part-{i:D5}.wav");
                var filterScript = Path.Combine(scratchFolder, $"filter-{i:D5}.txt");
                var args = new List<string>
                {
                    "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-t", Format(windowDuration), "-i",
                    $"anullsrc=r={SampleRate}:cl=stereo"
                };

                foreach (var clip in active)
                {
                    args.Add("-i");
                    args.Add(clip.FileName);
                }

                if (active.Count > 0)
                {
                    var graph = new StringBuilder("[0:a]asetpts=PTS-STARTPTS[base];");
                    var sources = new StringBuilder("[base]");
                    for (var j = 0; j < active.Count; j++)
                    {
                        var clip = active[j];
                        var sourceOffset = Math.Max(0.0, windowStart - clip.Start);
                        var delaySeconds = Math.Max(0.0, clip.Start - windowStart);
                        var remainingSeconds = Math.Min(
                            clip.Duration - sourceOffset,
                            windowDuration - delaySeconds);
                        if (remainingSeconds <= 0)
                        {
                            throw new InvalidOperationException("An audio clip has a non-positive trimmed duration.");
                        }

                        var delayMs = (long)Math.Round(delaySeconds * 1000.0, MidpointRounding.AwayFromZero);
                        graph.Append($"[{j + 1}:a]atrim=start={Format(sourceOffset)}:duration={Format(remainingSeconds)},");
                        graph.Append($"asetpts=PTS-STARTPTS,aformat=sample_rates={SampleRate}:channel_layouts=stereo,");
                        graph.Append($"adelay={delayMs}:all=1[clip{j}];");
                        sources.Append($"[clip{j}]");
                    }

                    graph.Append(sources);
                    graph.Append($"amix=inputs={active.Count + 1}:duration=first:normalize=0,");
                    graph.Append($"atrim=duration={Format(windowDuration)}[out]");
                    File.WriteAllText(filterScript, graph.ToString(), new UTF8Encoding(false));

                    args.AddRange(["-filter_complex_script", filterScript, "-map", "[out]"]);
                }
                else
                {
                    args.AddRange(["-map", "0:a"]);
                }

                args.AddRange(["-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                               "-ac", "2", "-c:a", "pcm_s16le", outputFile]);

                await RunFfmpegAsync(args, cancellationToken);
                VerifyOutput(outputFile);
                windows.Add(outputFile);
                progress?.Invoke(i + 1, windowCount);
            }

            // The windows are exactly adjacent in time and have the same PCM
            // format; concatenate their packets without decoding/re-encoding.
            var concatListFile = Path.Combine(scratchFolder, "windows.txt");
            File.WriteAllLines(concatListFile,
                windows.Select(f => $"file '{Path.GetFileName(f)}'"), new UTF8Encoding(false));

            await RunFfmpegAsync(
                ["-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                 "-f", "concat", "-safe", "0", "-i", concatListFile,
                 "-c:a", "copy", finalFile], cancellationToken);
            VerifyOutput(finalFile);

            Se.WriteToolsLog(
                $"TTS fast WAV merge v3: {clips.Count} clips in {windows.Count} 60s windows, " +
                $"audio duration {duration:0.0}s, elapsed {stopwatch.Elapsed.TotalSeconds:0.0}s");
            return finalFile;
        }
        catch
        {
            try { File.Delete(finalFile); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            // All temporary window WAVs and filter scripts are internal to this
            // run; keep only the final WAV, which the caller will move to export.
            try { Directory.Delete(scratchFolder, true); } catch { /* best effort */ }
        }
    }

    private static List<Clip> GetClips(
        IReadOnlyList<TtsStepResult> stepResults, CancellationToken cancellationToken)
    {
        var clips = new List<Clip>(stepResults.Count);
        var durationCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < stepResults.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = stepResults[i];
            var name = item.CurrentFileName;
            if (string.IsNullOrEmpty(name) || !File.Exists(name))
            {
                Se.WriteToolsLog($"TTS merge: segment {i + 1} has no audio file - leaving silence", true);
                continue;
            }

            if (!durationCache.TryGetValue(name, out var clipDuration))
            {
                clipDuration = FfmpegMediaInfo.Parse(name).Duration?.TotalSeconds ?? 0;
                if (clipDuration <= 0 || double.IsNaN(clipDuration) || double.IsInfinity(clipDuration))
                {
                    // Do not silently lose audio when the metadata is unreadable.
                    throw new InvalidOperationException($"Cannot determine audio duration: {name}");
                }
                durationCache.Add(name, clipDuration);
            }

            var start = Math.Max(0.0, item.Paragraph.StartTime.TotalSeconds);
            clips.Add(new Clip(name, start, clipDuration));
        }
        return clips;
    }

    private static string Format(double seconds) =>
        seconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static void VerifyOutput(string filename)
    {
        if (!File.Exists(filename) || new FileInfo(filename).Length <= 44)
        {
            throw new InvalidOperationException($"FFmpeg produced no WAV audio: {filename}");
        }
    }

    private static async Task RunFfmpegAsync(
        IEnumerable<string> args, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = FfmpegHelper.GetFfmpegLocation();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start FFmpeg for TTS WAV merge.");
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token);
            var errorText = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg TTS merge exited with code {process.ExitCode}: {errorText}");
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* best effort */ }

            if (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("FFmpeg TTS merge exceeded the five-minute per-window limit.");
            }
            throw;
        }
    }
}
