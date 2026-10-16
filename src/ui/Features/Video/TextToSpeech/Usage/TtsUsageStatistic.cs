using CommunityToolkit.Mvvm.ComponentModel;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

public partial class TtsUsageStatistic : ObservableObject
{
    public string Account { get; }
    public string Engine { get; }
    [ObservableProperty] private string _needed = "0";
    [ObservableProperty] private string _available = "—";
    [ObservableProperty] private string _after = "—";
    public TtsUsageStatistic(string account, string engine) { Account = account; Engine = engine; }
}
