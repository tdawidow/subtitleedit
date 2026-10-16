using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

public sealed class TtsJobClip
{
    public int Index { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class TtsJobDocument
{
    public int Version { get; set; } = 1;
    public string Fingerprint { get; set; } = string.Empty;
    public List<TtsJobClip> Clips { get; set; } = new();
}

/// <summary>Durable raw clips live outside the disposable TtsRunFolder. Settings/text/times
/// participate in the fingerprint, so a voice or source edit cannot reuse the wrong speech.</summary>
public sealed class TtsJobStore : IDisposable
{
    private readonly string _folder;
    private readonly string _manifest;
    private readonly TtsJobDocument _document;
    private readonly FileStream _lock;
    public string Folder => _folder;

    public TtsJobStore(string root, string fingerprint)
    {
        _folder = Path.Combine(root, TtsUsageStore.Hash(fingerprint));
        Directory.CreateDirectory(_folder);
        _manifest = Path.Combine(_folder, "progress.json");
        _lock = new FileStream(Path.Combine(_folder, "job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _document = File.Exists(_manifest)
                ? JsonSerializer.Deserialize<TtsJobDocument>(File.ReadAllText(_manifest)) ?? throw new InvalidDataException("Nieprawidłowy zapis postępu TTS.")
                : new TtsJobDocument { Fingerprint = fingerprint };
            if (_document.Version != 1 || _document.Fingerprint != fingerprint || _document.Clips == null)
                throw new InvalidDataException("Nieprawidłowy zapis postępu TTS.");
        }
        catch { _lock.Dispose(); throw; }
    }

    public string? GetClip(int index)
    {
        var clip = _document.Clips.FirstOrDefault(c => c.Index == index);
        if (clip == null || Path.GetFileName(clip.FileName) != clip.FileName) return null;
        var path = Path.Combine(_folder, clip.FileName);
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return null;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) == clip.Sha256 ? path : null;
    }

    public void Complete(int index, string audio)
    {
        if (!File.Exists(audio) || new FileInfo(audio).Length == 0) throw new InvalidDataException("Brak poprawnego pliku audio TTS.");
        var name = index + "_" + Guid.NewGuid().ToString("N") + Path.GetExtension(audio);
        var destination = Path.Combine(_folder, name);
        File.Copy(audio, destination);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(destination)));
        _document.Clips.RemoveAll(c => c.Index == index);
        _document.Clips.Add(new TtsJobClip { Index = index, FileName = name, Sha256 = hash });
        TtsUsageStore.WriteAtomically(_manifest, JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string SaveRemainingSrt(Subtitle source, IReadOnlySet<int> completed, IReadOnlySet<Paragraph> skipped, string output)
    {
        var remaining = new Subtitle();
        for (var i = 0; i < source.Paragraphs.Count; i++)
            if (!completed.Contains(i) && !skipped.Contains(source.Paragraphs[i]))
                remaining.Paragraphs.Add(new Paragraph(source.Paragraphs[i]));
        remaining.Renumber();
        File.WriteAllText(output, new SubRip().ToText(remaining, string.Empty), new UTF8Encoding(false));
        return output;
    }

    public void Dispose() => _lock.Dispose();
}
