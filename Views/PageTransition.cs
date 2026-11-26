using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CSharpProject;

internal static class PageTransition
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PageTransition),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement page)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            page.IsVisibleChanged += OnIsVisibleChanged;
        }
        else
        {
            page.IsVisibleChanged -= OnIsVisibleChanged;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not UIElement { IsVisible: true } page)
        {
            return;
        }

        page.Opacity = 0;
        var transform = new TranslateTransform(0, 8);
        page.RenderTransform = transform;
        var duration = TimeSpan.FromMilliseconds(500);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var slideUp = new DoubleAnimation(8, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        fadeIn.Completed += (_, _) => page.Opacity = 1;
        slideUp.Completed += (_, _) => page.RenderTransform = Transform.Identity;
        page.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        transform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }
}
