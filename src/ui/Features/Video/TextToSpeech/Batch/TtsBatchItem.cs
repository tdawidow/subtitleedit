using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

public partial class TtsBatchItem : ObservableObject
{
    public string SourceFile { get; }
    public string SourceName => Path.GetFileName(SourceFile);
    public string OutputName => Path.GetFileName(OutputFile);
    partial void OnOutputFileChanged(string value) => OnPropertyChanged(nameof(OutputName));
    [ObservableProperty] private bool _included = true;
    [ObservableProperty] private int _number;
    [ObservableProperty] private long _totalCharacters;
    [ObservableProperty] private long _neededCharacters;
    [ObservableProperty] private int _cachedClips;
    [ObservableProperty] private string _outputFile = string.Empty;
    [ObservableProperty] private string _status = "Oczekuje";
    internal long PlainCharacters { get; set; }
    internal long AzureCharacters { get; set; }
    internal Subtitle? Subtitle { get; set; }
    internal string SourceHash { get; set; } = string.Empty;
    internal bool Completed { get; set; }
    public TtsBatchItem(string source) { SourceFile = source; }
}
