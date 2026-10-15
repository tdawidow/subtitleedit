using System;
using System.Collections.Generic;
using System.Threading;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

internal sealed class TtsRequestProgress : IDisposable
{
    private static readonly AsyncLocal<TtsRequestProgress?> Current = new();
    private readonly TtsRequestProgress? _previous;
    private readonly Action<string> _report;
    private readonly Action<string, long>? _reservation;
    private readonly HashSet<string> _accounts = new();
    public TtsRequestProgress(Action<string> report, Action<string, long>? reservation = null)
    {
        _previous = Current.Value; _report = report; _reservation = reservation; Current.Value = this;
    }
    public static void Report(string message) => Current.Value?._report(message);
    public static void Reservation(string account, long characters)
    {
        var current = Current.Value;
        if (current == null) return;
        current._accounts.Add(account);
        current._reservation?.Invoke(account, characters);
    }
    public void Dispose()
    {
        foreach (var account in _accounts) _reservation?.Invoke(account, 0);
        Current.Value = _previous;
    }
}
