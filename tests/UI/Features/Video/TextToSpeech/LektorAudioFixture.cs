using System.Runtime.InteropServices;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Features.Video.TextToSpeech;

internal sealed class LektorAudioFixture : IDisposable
{
    internal string Folder { get; } = Path.Combine(Path.GetTempPath(), "lektor-v51 ąć-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsScope _settings = new("General.FfmpegPath", "Video.TextToSpeech.LektorMkvmergePath", "Video.TextToSpeech.LektorExport");
    private readonly string _ffmpeg = Configuration.Settings.General.FFmpegLocation;

    internal LektorAudioFixture()
    {
        Directory.CreateDirectory(Folder);
        Se.Settings.General.FfmpegPath = Environment.GetEnvironmentVariable("FFMPEG_TEST_PATH") ?? "ffmpeg";
        Configuration.Settings.General.FFmpegLocation = Se.Settings.General.FfmpegPath;
    }
    internal string FilePath(string name) => Path.Combine(Folder, name);
    internal string Touch(string name) { var file = FilePath(name); File.WriteAllText(file, "fixture"); return file; }

    internal string Wave(string name, double duration, double start, double end, double amplitude = 0.2,
        int sampleRate = 48000, int channels = 2)
    {
        var file = FilePath(name); var frames = (int)Math.Round(duration * sampleRate);
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write("RIFF"u8); writer.Write(36 + frames * channels * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels);
        writer.Write(sampleRate); writer.Write(sampleRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(frames * channels * 2);
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / sampleRate;
            var sample = t >= start && t < end ? (short)Math.Round(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * 400 * t)) : (short)0;
            for (var c = 0; c < channels; c++) writer.Write(sample);
        }
        return file;
    }

    internal async Task<string> Video(string name, bool multipleAudio = true)
    {
        var path = FilePath(name);
        var args = new List<string> { "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=64x64:r=10:d=2",
            "-f", "lavfi", "-i", "sine=frequency=300:sample_rate=48000:duration=2" };
        if (multipleAudio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=900:sample_rate=48000:duration=2"]);
        args.AddRange(["-map", "0:v", "-map", "1:a"]);
        if (multipleAudio) args.AddRange(["-map", "2:a"]);
        args.AddRange(["-c:v", "ffv1", "-c:a", "pcm_s16le", "-metadata:s:a:0", "language=jpn"]);
        if (multipleAudio) args.AddRange(["-metadata:s:a:1", "language=eng"]);
        args.Add(path); await Run(args); return path;
    }

    internal static async Task Run(IEnumerable<string> arguments)
    {
        var log = new System.Text.StringBuilder();
        using var process = FfmpegGenerator.GetProcess(string.Empty, (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); });
        process.StartInfo.Arguments = string.Empty;
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        await process.StartAndWaitAsync(TestContext.Current.CancellationToken, TimeSpan.FromSeconds(45));
        Assert.True(process.ExitCode == 0, log.ToString());
    }

    internal static short[] Pcm(string file)
    {
        using var stream = File.OpenRead(file); var header = new WaveHeader2(stream);
        stream.Position = header.DataStartPosition;
        var data = new byte[(int)header.DataChunkSize]; stream.ReadExactly(data);
        return MemoryMarshal.Cast<byte, short>(data).ToArray();
    }
    internal static double Rms(short[] samples, double start, double end)
    {
        var first = (int)(start * 48000) * 2; var last = Math.Min(samples.Length, (int)(end * 48000) * 2);
        return Math.Sqrt(samples[first..last].Average(s => Math.Pow(s / 32768.0, 2)));
    }
    public void Dispose()
    {
        Configuration.Settings.General.FFmpegLocation = _ffmpeg;
        _settings.Dispose(); Directory.Delete(Folder, true);
    }
}
