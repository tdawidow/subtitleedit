using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Engines;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

internal static class TtsUsageAccount
{
    public static string Azure(string apiKey, string region) => TtsUsageStore.AccountId("AzureSpeech", apiKey, region);

    public static string ForEngine(ITtsEngine engine, string? selectedKey = null, string? selectedRegion = null)
    {
        var s = Se.Settings.Video.TextToSpeech;
        if (engine is AzureSpeech) return Azure(selectedKey ?? s.AzureApiKey, selectedRegion ?? s.AzureRegion);
        if (engine is OpenAiCompatibleSpeech)
        {
            var provider = selectedRegion ?? OpenAiCompatibleSpeech.SavedProvider;
            return TtsUsageStore.AccountId(engine.Name, selectedKey ?? OpenAiCompatibleSpeech.GetApiKey(provider),
                provider + (provider == "Custom" ? "\n" + s.OpenAiCompatibleCustomUrl : string.Empty));
        }
        var credential = engine.Name switch
        {
            "ElevenLabs" => s.ElevenLabsApiKey,
            "Murf" => s.MurfApiKey,
            "MistralSpeech" => Se.Settings.Providers.MistralApiKey,
            "GoogleSpeech" => s.GoogleKeyFile,
            _ => string.Empty,
        };
        // Cloud engines without an exposed credential mapping still get a separate local
        // engine/provider profile; no raw API key is ever written to the usage ledger.
        return TtsUsageStore.AccountId(engine.Name, selectedKey ?? credential);
    }
}
