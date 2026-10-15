using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.Usage;

public sealed class TtsUsageSettingsWindow : Window
{
    public TtsUsageSettingsWindow(TtsUsageStore store, string account, string engineName)
    {
        Title = "Limit znaków TTS — " + engineName;
        Width = 620;
        Height = 560;
        MinWidth = 500;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var profile = store.Get(account);
        var enabled = new CheckBox { Name = "TtsUsageEnabled", Content = "Kontroluj lokalny limit znaków tego konta / zasobu", IsChecked = profile.Enabled };
        var day = new NumericUpDown { Name = "TtsUsageResetDay", Minimum = 1, Maximum = 31, Value = profile.ResetDay, FormatString = "0" };
        var monthly = new NumericUpDown { Name = "TtsUsageMonthly", Minimum = 0, Maximum = 1_000_000_000_000m, Value = profile.MonthlyCharacters, FormatString = "0" };
        var available = new TextBox { Name = "TtsUsageAvailableOverride", PlaceholderText = "Opcjonalnie — pozostaw puste, aby zachować obecne saldo" };
        var error = new TextBlock { Name = "TtsUsageSettingsError", Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Name = "TtsUsageSave", Content = "Zapisz", IsDefault = true };
        save.Classes.Add("accent");
        save.Click += (_, _) =>
        {
            try
            {
                if ((day.Value ?? 1) != decimal.Truncate(day.Value ?? 1) || (monthly.Value ?? 0) != decimal.Truncate(monthly.Value ?? 0))
                    throw new ArgumentException("Dzień resetu i liczba znaków muszą być liczbami całkowitymi.");
                long? balance = null;
                if (!string.IsNullOrWhiteSpace(available.Text))
                {
                    if (!long.TryParse(available.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0)
                        throw new ArgumentException("Dostępne znaki: wpisz nieujemną liczbę całkowitą.");
                    balance = n;
                }
                store.Configure(account, enabled.IsChecked == true, (int)(day.Value ?? 1), (long)(monthly.Value ?? 0), balance);
                Close(true);
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var cancel = new Button { Content = "Anuluj", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 9,
                Children =
                {
                    enabled,
                    new TextBlock { Text = "Dzień miesiąca, w którym odnawia się limit:" }, day,
                    new TextBlock { Text = "Liczba znaków dostępnych w pełnym miesiącu:" }, monthly,
                    new TextBlock { Text = "Dostępne znaki od teraz do najbliższego resetu (opcjonalnie):", TextWrapping = TextWrapping.Wrap }, available,
                    new TextBlock { Text = $"Obecne saldo: {profile.AvailableCharacters:N0}. Rezerwacje: {profile.ReservedCharacters:N0}.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = "To lokalne oszacowanie, wspólne dla głosów tego konta. Uwzględnij użycie w innych programach. " +
                               "Wpisanie dostępnego salda rozlicza również niepotwierdzone żądania. " +
                               "Puste pole zachowuje saldo; przy pierwszym włączeniu przyjmowany jest limit miesięczny. " +
                               "Reset odbywa się według daty komputera; dzień 29–31 w krótszym miesiącu oznacza jego ostatni dzień.",
                        TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
                    },
                    error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } },
                },
            },
        };
    }
}
