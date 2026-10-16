using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using System.Threading.Tasks;
using System.IO;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

namespace Nikse.SubtitleEdit.Features.Main;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ShowBatchTts()
    {
        if (Window == null || !await RequireFfmpegOk()) return;
        await ShowDialogAsync<BatchTtsWindow, TextToSpeechViewModel>(vm => vm.Initialize(new Subtitle(), string.Empty, null, Path.GetTempPath()));
    }

    [RelayCommand]
    private async Task ShowBatchLektorMux()
    {
        if (Window == null || !await RequireFfmpegOk()) return;
        await new BatchMuxWindow(_fileHelper).ShowDialog<object?>(Window);
    }

    [RelayCommand]
    private async Task ShowLektorExport()
    {
        if (Window == null) return;
        await new LektorExportWindow(_fileHelper, _videoFileName ?? string.Empty, string.Empty)
            .ShowDialog<string?>(Window);
    }
}
