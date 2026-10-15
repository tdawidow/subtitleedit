using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Video.TextToSpeech.LektorPl;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ShowLektorExport()
    {
        if (Window == null) return;
        await new LektorExportWindow(_fileHelper, _videoFileName ?? string.Empty, string.Empty)
            .ShowDialog<string?>(Window);
    }
}
