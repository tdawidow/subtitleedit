using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

public sealed class TtsUsageProfile
{
    public bool Enabled { get; set; }
    public int ResetDay { get; set; } = 1;
    public long MonthlyCharacters { get; set; } = 500000;
    public long RemainingCharacters { get; set; } = 500000;
    public DateTime CycleStart { get; set; }
    public bool ProviderQuotaExhausted { get; set; }
    public List<TtsUsageReservation> Reservations { get; set; } = new();
    public long ReservedCharacters => Reservations.Where(r => r.CycleStart == CycleStart).Sum(r => r.Characters);
    public long AvailableCharacters => ProviderQuotaExhausted ? Math.Min(0, RemainingCharacters - ReservedCharacters) : RemainingCharacters - ReservedCharacters;
}

public sealed class TtsUsageReservation
{
    public string Id { get; set; } = string.Empty;
    public long Characters { get; set; }
    public DateTime CycleStart { get; set; }
}

public sealed class TtsUsageDocument
{
    public int Version { get; set; } = 1;
    public Dictionary<string, TtsUsageProfile> Profiles { get; set; } = new();
}

/// <summary>
/// Local estimate, never a substitute for the provider's billing ledger. A persisted reservation
/// survives a timeout/crash: absence of a local audio file is not evidence of a free request.
/// A separate exclusive file lock protects multiple program instances and atomic replacement
/// ensures that interruption cannot expose a partially written JSON document.
/// </summary>
public sealed class TtsUsageStore
{
    private static readonly object Gate = new();
    private readonly string _fileName;
    private readonly Func<DateTime> _today;
    public static string DefaultFileName => Path.Combine(Se.DataFolder, "tts-usage.json");

    public TtsUsageStore(string? fileName = null, Func<DateTime>? today = null)
    {
        _fileName = fileName ?? DefaultFileName;
        _today = today ?? (() => DateTime.Today);
    }

    public static string AccountId(string engine, string credential, string resource = "") =>
        engine + ":" + Hash(engine + "\n" + resource.Trim().ToLowerInvariant() + "\n" + credential.Trim());

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static DateTime CycleStart(DateTime today, int day)
    {
        if (day < 1 || day > 31) throw new ArgumentOutOfRangeException(nameof(day));
        var reset = new DateTime(today.Year, today.Month, Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month)));
        if (today.Date >= reset) return reset;
        var previous = today.AddMonths(-1);
        return new DateTime(previous.Year, previous.Month, Math.Min(day, DateTime.DaysInMonth(previous.Year, previous.Month)));
    }

    public TtsUsageProfile Get(string account) => Access(account, p => p);

    public void Configure(string account, bool enabled, int resetDay, long monthly, long? available)
    {
        if (resetDay < 1 || resetDay > 31 || monthly < 0 || monthly > 1_000_000_000_000L || available < 0 || available > 1_000_000_000_000L)
            throw new ArgumentOutOfRangeException(nameof(monthly), "Sprawdź dzień resetu i liczbę znaków.");
        Access(account, p =>
        {
            var wasEnabled = p.Enabled;
            var oldDay = p.ResetDay;
            p.Enabled = enabled;
            p.ResetDay = resetDay;
            p.MonthlyCharacters = monthly;
            // Explicit reconciliation also resolves unknown requests. Merely changing a day or
            // monthly allowance never silently credits this month's already consumed characters.
            if (available.HasValue)
            {
                p.RemainingCharacters = available.Value;
                p.Reservations.Clear();
                p.ProviderQuotaExhausted = false;
            }
            else if (!wasEnabled && enabled)
                p.RemainingCharacters = monthly;
            if (oldDay != resetDay)
            {
                p.CycleStart = CycleStart(_today(), resetDay);
                foreach (var r in p.Reservations) r.CycleStart = p.CycleStart;
            }
            return true;
        });
    }

    public string? Reserve(string account, long characters) => Access(account, p =>
    {
        if (characters < 0) throw new ArgumentOutOfRangeException(nameof(characters));
        if (!p.Enabled) return null;
        if (p.AvailableCharacters < characters)
            throw new TtsServiceException(TtsFailureKind.LocalLimit, $"Brak znaków TTS. Potrzeba {characters:N0}, dostępne {p.AvailableCharacters:N0}.");
        var id = Guid.NewGuid().ToString("N");
        p.Reservations.Add(new TtsUsageReservation { Id = id, Characters = characters, CycleStart = p.CycleStart });
        return id;
    });

    public void MarkProviderQuotaExhausted(string account) => Access(account, p =>
    {
        p.ProviderQuotaExhausted = true;
        return true;
    });

    public void Resolve(string account, string? reservationId, bool processed)
    {
        if (reservationId == null) return;
        Access(account, p =>
        {
            var r = p.Reservations.FirstOrDefault(r => r.Id == reservationId);
            if (r == null) return false; // idempotent; a manual reconciliation may already settle it
            if (processed && r.CycleStart == p.CycleStart) p.RemainingCharacters -= r.Characters;
            p.Reservations.Remove(r);
            return true;
        });
    }

    private T Access<T>(string account, Func<TtsUsageProfile, T> action)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_fileName))!);
            using var fileLock = new FileStream(_fileName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // Corrupt/unknown data fails closed. Never silently replace a damaged ledger with a
            // fresh full monthly allowance. The original is retained for diagnosis/recovery.
            var doc = File.Exists(_fileName)
                ? JsonSerializer.Deserialize<TtsUsageDocument>(File.ReadAllText(_fileName)) ?? throw new InvalidDataException("Nieprawidłowy plik tts-usage.json.")
                : new TtsUsageDocument();
            if (doc.Version != 1 || doc.Profiles == null) throw new InvalidDataException("Nieobsługiwana wersja tts-usage.json.");
            var dirty = false;
            if (!doc.Profiles.TryGetValue(account, out var profile))
            {
                profile = new TtsUsageProfile { CycleStart = CycleStart(_today(), 1) };
                doc.Profiles.Add(account, profile);
                dirty = true;
            }
            if (profile.ResetDay is < 1 or > 31 || profile.Reservations == null || profile.MonthlyCharacters < 0 || profile.Reservations.Any(r => r.Characters < 0))
                throw new InvalidDataException("Nieprawidłowe ustawienia licznika TTS.");
            var cycle = CycleStart(_today(), profile.ResetDay);
            if (cycle > profile.CycleStart)
            {
                profile.RemainingCharacters = profile.MonthlyCharacters;
                profile.CycleStart = cycle;
                profile.ProviderQuotaExhausted = false;
                // Unknown old-cycle requests stay recorded, but do not consume a new allowance.
                dirty = true;
            }
            var before = JsonSerializer.Serialize(profile);
            var result = action(profile);
            if (dirty || before != JsonSerializer.Serialize(profile))
                WriteAtomically(_fileName, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
    }

    internal static void WriteAtomically(string fileName, string text)
    {
        var temporary = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, fileName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
