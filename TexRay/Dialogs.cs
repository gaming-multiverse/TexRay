// Minimal dark message/confirm dialogs (Avalonia ships no MessageBox).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TexRay;

public static class Dialogs
{
    public static Task InfoAsync(Window owner, string title, string message) =>
        ShowAsync(owner, title, message, yesNo: false);

    public static Task<bool> ConfirmAsync(Window owner, string title, string message) =>
        ShowAsync(owner, title, message, yesNo: true);

    static async Task<bool> ShowAsync(Window owner, string title, string message, bool yesNo)
    {
        bool result = false;

        var dlg = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1E1313")),
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 20, 24, 14),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 0, 24, 20),
        };

        Button Make(string label)
        {
            var b = new Button
            {
                Content = label,
                Width = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(8),
            };
            buttons.Children.Add(b);
            return b;
        }

        if (yesNo)
        {
            Make("Yes").Click += (_, _) => { result = true; dlg.Close(); };
            Make("No").Click += (_, _) => dlg.Close();
        }
        else
        {
            Make("OK").Click += (_, _) => dlg.Close();
        }

        dlg.Content = new StackPanel { Children = { text, buttons } };
        await dlg.ShowDialog(owner);
        return result;
    }
}
