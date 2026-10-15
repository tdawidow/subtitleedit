using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Voices;
using Nikse.SubtitleEdit.Logic.Download;

namespace UITests.Features.Video.TextToSpeech;

public class TtsUsageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tts-usage-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _today = new(2026, 10, 15);
    private string Ledger => Path.Combine(_folder, "tts-usage.json");
    private TtsUsageStore Store() => new(Ledger, () => _today);
    public TtsUsageTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { Directory.Delete(_folder, true); }

    [Theory]
    [InlineData(2027, 2, 27, 31, 2027, 1, 31)]
    [InlineData(2027, 2, 28, 31, 2027, 2, 28)]
    [InlineData(2028, 2, 29, 31, 2028, 2, 29)]
    [InlineData(2026, 10, 15, 1, 2026, 10, 1)]
    public void ResetUsesLastDayInShortMonth(int y, int m, int d, int reset, int ey, int em, int ed) =>
        Assert.Equal(new DateTime(ey, em, ed), TtsUsageStore.CycleStart(new DateTime(y, m, d), reset));

    [Fact]
    public void ReservationSurvivesRestartAndCanOnlyBeSettledOnce()
    {
        Store().Configure("a", true, 1, 100, 60);
        var id = Store().Reserve("a", 15);
        Assert.Equal(45, Store().Get("a").AvailableCharacters);
        Store().Resolve("a", id, true);
        Store().Resolve("a", id, true);
        Assert.Equal(45, Store().Get("a").RemainingCharacters);
        Assert.Equal(0, Store().Get("a").ReservedCharacters);
        var rejected = Store().Reserve("a", 10);
        Store().Resolve("a", rejected, false);
        Assert.Equal(45, Store().Get("a").AvailableCharacters);
    }

    [Fact]
    public void ResetRunsOnceAndOldUncertainRequestDoesNotConsumeNewMonth()
    {
        Store().Configure("a", true, 1, 100, 20);
        var old = Store().Reserve("a", 10);
        _today = new DateTime(2026, 12, 4); // includes a month when app was not running
        Assert.Equal(100, Store().Get("a").AvailableCharacters);
        var current = Store().Reserve("a", 7);
        Store().Resolve("a", current, true);
        Store().Resolve("a", old, true);
        Assert.Equal(93, Store().Get("a").AvailableCharacters);
        _today = _today.AddDays(-1); // clock moving backwards must not refill the allowance
        Assert.Equal(93, Store().Get("a").AvailableCharacters);
    }

    [Fact]
    public void ZeroOverrideAndEditedLimitDoNotRefillCurrentBalance()
    {
        Store().Configure("a", true, 1, 100, 0);
        Store().Configure("a", true, 20, 1000, null);
        Assert.Equal(0, Store().Get("a").AvailableCharacters);
        Assert.Throws<TtsServiceException>(() => Store().Reserve("a", 1));
        _today = new DateTime(2026, 10, 20);
        Assert.Equal(1000, Store().Get("a").AvailableCharacters);
    }

    [Fact]
    public async Task MultipleInstancesCannotOverReserve()
    {
        Store().Configure("a", true, 1, 10, null);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            try { Store().Reserve("a", 3); return true; }
            catch (TtsServiceException) { return false; }
        })));
        Assert.Equal(3, outcomes.Count(x => x));
        Assert.Equal(1, Store().Get("a").AvailableCharacters);
    }

    [Fact]
    public void AccountsDoNotShareBalancesOrPersistCredentials()
    {
        var first = TtsUsageStore.AccountId("AzureSpeech", "very-secret-key", "westeurope");
        var second = TtsUsageStore.AccountId("AzureSpeech", "other-secret-key", "westeurope");
        Store().Configure(first, true, 1, 100, 5);
        Store().Configure(second, true, 1, 100, 40);
        Assert.Equal(5, Store().Get(first).AvailableCharacters);
        Assert.Equal(40, Store().Get(second).AvailableCharacters);
        Assert.DoesNotContain("secret-key", File.ReadAllText(Ledger));
    }

    [Fact]
    public void CorruptLedgerIsRetainedAndFailsClosed()
    {
        File.WriteAllText(Ledger, "{invalid");
        Assert.ThrowsAny<Exception>(() => Store().Reserve("a", 1));
        Assert.Equal("{invalid", File.ReadAllText(Ledger));
    }

    [Theory]
    [InlineData("Zażółć gęślą jaźń.", false, 18)]
    [InlineData("A😀 B", false, 4)]
    [InlineData("中文", true, 4)]
    [InlineData("中文", false, 2)]
    public void CountsUnicodeRatherThanBytesOrUtf16(string text, bool azure, long expected) =>
        Assert.Equal(expected, TtsUsageText.Count(text, azure));

    [Fact]
    public void JobKeepsAudioAfterTemporaryFolderDeletionAndDetectsModifiedClip()
    {
        var temporary = Path.Combine(_folder, "temporary.wav");
        File.WriteAllBytes(temporary, new byte[] { 1, 2, 3 });
        string durable;
        using (var job = new TtsJobStore(Path.Combine(_folder, "jobs"), "same-settings-and-srt"))
        {
            job.Complete(0, temporary);
            durable = job.GetClip(0)!;
        }
        File.Delete(temporary);
        using var resumed = new TtsJobStore(Path.Combine(_folder, "jobs"), "same-settings-and-srt");
        Assert.Equal(durable, resumed.GetClip(0));
        File.WriteAllBytes(durable, new byte[] { 4 });
        Assert.Null(resumed.GetClip(0));
        using var changed = new TtsJobStore(Path.Combine(_folder, "jobs"), "changed-voice");
        Assert.Null(changed.GetClip(0));
    }

    [Fact]
    public void RemainingSrtIncludesFailedSentLineAndUnsentLinesWithOriginalTimes()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("Gotowe", 1200, 1800));
        subtitle.Paragraphs.Add(new Paragraph("Wysłane, bez audio", 5200, 6000));
        subtitle.Paragraphs.Add(new Paragraph("Jeszcze niewysłane", 9200, 10000));
        using var job = new TtsJobStore(Path.Combine(_folder, "jobs"), "x");
        var path = job.SaveRemainingSrt(subtitle, new HashSet<int> { 0 }, new HashSet<Paragraph>(), Path.Combine(_folder, "reszta.srt"));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("Gotowe", text);
        Assert.Contains("1\n00:00:05,200 --> 00:00:06,000", text.Replace("\r\n", "\n"));
        Assert.Contains("2\n00:00:09,200 --> 00:00:10,000", text.Replace("\r\n", "\n"));
        Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return handle(request, cancellationToken); }
    }

    private async Task<(Handler Handler, TtsDownloadService Service)> Azure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
    {
        await Task.CompletedTask;
        var handler = new Handler(callback);
        var service = new TtsDownloadService(new HttpClient(handler)) { UsageStoreOverride = Store(), AzureRetryDelay = (_, _) => Task.CompletedTask };
        Store().Configure(TtsUsageAccount.Azure("test-key", "westeurope"), true, 1, 100, null);
        return (handler, service);
    }

    private static Task<bool> Request(TtsDownloadService service, MemoryStream audio) => service.DownloadAzureVoiceSpeak(
        "Żółw & pies.", new AzureVoice { Locale = "pl-PL", ShortName = "pl-PL-MarekNeural", Gender = "Male" }, "", "test-key", "pl", "westeurope", audio, null, CancellationToken.None);

    [Fact]
    public async Task AzureSuccessCountsDecodedTextAndPreservesExistingSsmlAndAudioFormat()
    {
        string? payload = null;
        var (_, service) = await Azure(async (request, _) =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            Assert.Equal("audio-16khz-32kbitrate-mono-mp3", request.Headers.GetValues("X-Microsoft-OutputFormat").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
        });
        using var audio = new MemoryStream();
        Assert.True(await Request(service, audio));
        Assert.Contains("pl-PL-MarekNeural", payload);
        Assert.Equal(88, Store().Get(TtsUsageAccount.Azure("test-key", "westeurope")).AvailableCharacters);
    }

    [Fact]
    public async Task AzureTemporary429RetriesAreBoundedAndDoNotSpendCharacters()
    {
        var (handler, service) = await Azure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            { Content = new StringContent("rate limit exceeded") }));
        using var audio = new MemoryStream();
        var error = await Assert.ThrowsAsync<TtsServiceException>(() => Request(service, audio));
        Assert.Equal(TtsFailureKind.Throttled, error.Kind);
        Assert.Equal(5, handler.Calls);
        Assert.Equal(100, Store().Get(TtsUsageAccount.Azure("test-key", "westeurope")).AvailableCharacters);
    }

    [Fact]
    public async Task AzureExplicitQuotaStopsImmediately()
    {
        var (handler, service) = await Azure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            { Content = new StringContent("Monthly character quota exceeded") }));
        using var audio = new MemoryStream();
        var error = await Assert.ThrowsAsync<TtsServiceException>(() => Request(service, audio));
        Assert.Equal(TtsFailureKind.ProviderQuota, error.Kind);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, Store().Get(TtsUsageAccount.Azure("test-key", "westeurope")).AvailableCharacters);
        Assert.True(Store().Get(TtsUsageAccount.Azure("test-key", "westeurope")).ProviderQuotaExhausted);
    }

    [Fact]
    public async Task AzureNetworkFailureKeepsUnknownReservation()
    {
        var (_, service) = await Azure((_, _) => throw new HttpRequestException("connection lost"));
        using var audio = new MemoryStream();
        await Assert.ThrowsAsync<HttpRequestException>(() => Request(service, audio));
        var profile = Store().Get(TtsUsageAccount.Azure("test-key", "westeurope"));
        Assert.Equal(88, profile.AvailableCharacters);
        Assert.Equal(12, profile.ReservedCharacters);
    }

    [Fact]
    public async Task AzureProcessedButEmptyAudioStillConsumesCharacters()
    {
        var (_, service) = await Azure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Array.Empty<byte>()) }));
        using var audio = new MemoryStream();
        await Assert.ThrowsAsync<TtsServiceException>(() => Request(service, audio));
        var profile = Store().Get(TtsUsageAccount.Azure("test-key", "westeurope"));
        Assert.Equal(88, profile.AvailableCharacters);
        Assert.Equal(0, profile.ReservedCharacters);
    }

    [AvaloniaFact]
    public void SettingsWindowHasThreeRequiredFieldsAndBlankOptionalOverride()
    {
        var window = new TtsUsageSettingsWindow(Store(), "a", "AzureSpeech");
        try
        {
            window.Show();
            var controls = window.GetLogicalDescendants().ToArray();
            Assert.Equal(1, controls.OfType<NumericUpDown>().Single(c => c.Name == "TtsUsageResetDay").Value);
            Assert.Null(controls.OfType<TextBox>().Single(c => c.Name == "TtsUsageAvailableOverride").Text);
            Assert.NotNull(controls.OfType<NumericUpDown>().Single(c => c.Name == "TtsUsageMonthly"));
        }
        finally { window.Close(); }
    }
}
