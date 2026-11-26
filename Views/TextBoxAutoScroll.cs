using System.Windows;
using System.Windows.Controls;

namespace CSharpProject;

internal static class TextBoxAutoScroll
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TextBoxAutoScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBox textBox)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            textBox.TextChanged += OnTextChanged;
        }
        else
        {
            textBox.TextChanged -= OnTextChanged;
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }
}
