using System.Windows;
using System.Windows.Controls;

namespace DevTools.Wpf.Infrastructure.Dialogs;

public static class PresetNamePrompt
{
    public static string? Show(Window owner, string initialValue, string title, Action<string>? onValidationError = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Preset name"
        };

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = initialValue
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var saveButton = new Button { Content = "Save", Width = 74, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 74, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

        saveButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);

        Grid.SetRow(label, 0);
        Grid.SetRow(textBox, 1);
        Grid.SetRow(actions, 2);

        root.Children.Add(label);
        root.Children.Add(textBox);
        root.Children.Add(actions);
        dialog.Content = root;

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var value = textBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            onValidationError?.Invoke("Preset name is required.");
            return null;
        }

        return value;
    }
}