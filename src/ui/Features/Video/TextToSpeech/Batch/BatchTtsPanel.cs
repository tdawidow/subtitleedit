using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Batch;

internal sealed class BatchTtsPanel : Grid
{
    internal BatchTtsPanel(TextToSpeechViewModel vm, Control engine, Control settings, Control progress)
    {
        Margin = new Thickness(16); RowSpacing = 10;
        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto");
        var shared = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        shared.Children.Add(engine); Grid.SetColumn(settings, 1); shared.Children.Add(settings);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Bind(IsEnabledProperty, new Binding("!" + nameof(vm.IsGenerating)));
        controls.Children.Add(UiUtil.MakeButton("Dodaj SRT…", vm.AddBatchSrtCommand));
        controls.Children.Add(UiUtil.MakeButton("↑", vm.BatchUpCommand));
        controls.Children.Add(UiUtil.MakeButton("↓", vm.BatchDownCommand));
        controls.Children.Add(UiUtil.MakeButton("Usuń wiersz", vm.RemoveBatchItemCommand));
        controls.Children.Add(UiUtil.MakeButton("Sprawdź kolejkę", vm.CheckBatchCommand));
        var output = new TextBox { Name = "BatchWavOutput", MinWidth = 320, PlaceholderText = "Wspólny katalog wynikowy" };
        output.Bind(TextBox.TextProperty, new Binding(nameof(vm.BatchOutputFolder)) { Mode = BindingMode.TwoWay });
        controls.Children.Add(output); controls.Children.Add(UiUtil.MakeButton("Katalog WAV…", vm.PickBatchOutputCommand));
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 5 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("40,40,3*,100,100,90,3*,2*") };
        var labels = new[] { "Użyj", "Lp.", "SRT", "Wszystkie znaki", "Do wysłania", "Gotowe linie", "Wynikowy WAV", "Stan" };
        for (var i = 0; i < labels.Length; i++) header.Add(new TextBlock { Text = labels[i], FontSize = 12, FontWeight = FontWeight.SemiBold }, 0, i);
        var list = new ListBox
        {
            Name = "BatchSrtQueue", ItemsSource = vm.BatchItems,
            ItemTemplate = new FuncDataTemplate<TtsBatchItem>((item, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,40,3*,100,100,90,3*,2*"), MinHeight = 36, DataContext = item };
                var check = new CheckBox(); check.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(item.Included)) { Mode = BindingMode.TwoWay });
                row.Children.Add(check);
                var props = new[] { nameof(item.Number), nameof(item.SourceName), nameof(item.TotalCharacters), nameof(item.NeededCharacters), nameof(item.CachedClips), nameof(item.OutputName), nameof(item.Status) };
                for (var i = 0; i < props.Length; i++)
                {
                    var text = new TextBlock { Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    text.Bind(TextBlock.TextProperty, new Binding(props[i]));
                    text.Bind(ToolTip.TipProperty, new Binding(i == 1 ? nameof(item.SourceFile) : i == 5 ? nameof(item.OutputFile) : props[i]));
                    row.Add(text, 0, i + 1);
                }
                return row;
            })
        };
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(vm.SelectedBatchItem)) { Mode = BindingMode.TwoWay });
        list.Bind(IsEnabledProperty, new Binding("!" + nameof(vm.IsGenerating)));
        table.Add(header, 0, 0); table.Add(list, 1, 0);
        var total = new TextBlock { Height = 25 }; total.Bind(TextBlock.TextProperty, new Binding(nameof(vm.BatchQueueInfo))); table.Add(total, 2, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var start = UiUtil.MakeButton("Generuj kolejkę WAV", vm.GenerateBatchCommand);
        start.Name = "StartBatchWav"; start.Classes.Add("accent"); start.Bind(IsEnabledProperty, new Binding("!" + nameof(vm.IsGenerating)));
        actions.Children.Add(UiUtil.MakeButton("Stop / Zamknij", vm.CancelCommand)); actions.Children.Add(start);
        var footer = new StackPanel { Spacing = 6 };
        var state = new TextBlock { Height = 48, TextWrapping = TextWrapping.Wrap }; state.Bind(TextBlock.TextProperty, new Binding(nameof(vm.BatchStatus)));
        footer.Children.Add(state); footer.Children.Add(progress);
        Add(new ScrollViewer { Content = shared, MaxHeight = 420 }, 0);
        Add(new ScrollViewer { Content = controls, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, 1);
        Add(table, 2); Add(footer, 3); Add(actions, 4);
    }
    private void Add(Control control, int row) { Grid.SetRow(control, row); Children.Add(control); }
}
