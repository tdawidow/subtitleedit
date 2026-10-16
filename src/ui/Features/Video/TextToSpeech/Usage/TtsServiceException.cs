using System;
using System.Net;
using System.Text;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

public enum TtsFailureKind { None, LocalLimit, ProviderQuota, Throttled, Rejected, Uncertain }

public sealed class TtsServiceException : Exception
{
    public TtsFailureKind Kind { get; }
    public TtsServiceException(TtsFailureKind kind, string message) : base(message) => Kind = kind;
}

public static class TtsUsageText
{
    // The same plain text Azure serializes inside its speak/voice wrappers. Count Unicode
    // code points, not UTF-8 bytes or UTF-16 code units; Azure counts CJK ideographs twice.
    public static string Prepare(string text) => Utilities.UnbreakLine(text);
    public static long Count(string text, bool azure)
    {
        long count = 0;
        foreach (var rune in Prepare(text).EnumerateRunes())
        {
            var c = rune.Value;
            var cjk = c is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
                or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF;
            count += azure && cjk ? 2 : 1;
        }
        return count;
    }

    public static TtsFailureKind Classify(HttpStatusCode status, string body, bool azure)
    {
        var s = body.ToLowerInvariant();
        var explicitQuota = azure
            ? (s.Contains("quota") || s.Contains("limit") || s.Contains("balance")) &&
              (s.Contains("character") || s.Contains("monthly") || s.Contains("outofquota") || s.Contains("out of quota"))
            : s.Contains("quota_exceeded") || s.Contains("insufficient_credits") || s.Contains("insufficient credits");
        if (explicitQuota) return TtsFailureKind.ProviderQuota;
        return status == HttpStatusCode.TooManyRequests ? TtsFailureKind.Throttled : TtsFailureKind.Rejected;
    }
}
