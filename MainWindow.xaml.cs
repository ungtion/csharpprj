using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CSharpProject;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _simulationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public MainWindow()
    {
        InitializeComponent();
        UpdateDisplay();
        ScanSerialPorts();
        _simulationTimer.Tick += (_, _) => UpdateDisplay();
        _simulationTimer.Start();
        Closed += (_, _) => _simulationTimer.Stop();
    }

    private void UpdateDisplay()
    {
        var now = DateTime.Now;
        var temperature = 42.6 + (Random.Shared.NextDouble() - 0.5) * 0.6;
        var pressure = 0.62 + (Random.Shared.NextDouble() - 0.5) * 0.02;
        var speed = 1480 + Random.Shared.Next(-4, 5);
        var efficiency = 98.6 + (Random.Shared.NextDouble() - 0.5) * 0.2;

        ClockText.Text = now.ToString("yyyy-MM-dd  HH:mm:ss");
        UpdateTimeText.Text = now.ToString("HH:mm:ss");
        TemperatureText.Text = temperature.ToString("F1");
        PressureText.Text = pressure.ToString("F2");
        SpeedText.Text = speed.ToString();
        EfficiencyText.Text = efficiency.ToString("F1");

    }

    private void ScanPortsClick(object sender, RoutedEventArgs e) => ScanSerialPorts();

    private void OverviewNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(OverviewPage, OverviewNavButton, "设备总览");

    private void RealtimeNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(RealtimePage, RealtimeNavButton, "实时监控");

    private void RecipeNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(RecipePage, RecipeNavButton, "bake");

    private void AlarmNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(AlarmPage, AlarmNavButton, "报警记录");

    private void SystemNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(SystemPage, SystemNavButton, "系统信息");

    private void ShowPage(UIElement page, Button activeButton, string title)
    {
        if (page.Visibility == Visibility.Visible)
        {
            return;
        }

        OverviewPage.Visibility = Visibility.Collapsed;
        RealtimePage.Visibility = Visibility.Collapsed;
        RecipePage.Visibility = Visibility.Collapsed;
        AlarmPage.Visibility = Visibility.Collapsed;
        SystemPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        AnimatePageEntry(page);

        foreach (var button in new[]
                 {
                     OverviewNavButton,
                     RealtimeNavButton,
                     RecipeNavButton,
                     AlarmNavButton,
                     SystemNavButton
                 })
        {
            var isActive = button == activeButton;
            button.Background = isActive
                ? System.Windows.Media.Brushes.DarkSlateGray
                : System.Windows.Media.Brushes.Transparent;
            button.Foreground = isActive
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(195, 203, 214));
        }

        PageBreadcrumbText.Text = title;
    }

    private static void AnimatePageEntry(UIElement page)
    {
        page.Opacity = 0;
        page.RenderTransform = new TranslateTransform(0, 8);

        var duration = TimeSpan.FromMilliseconds(1000);
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
        page.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void ScanSerialPorts()
    {
        try
        {
            using var serialPortsKey =
                Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");

            var ports = serialPortsKey?
                .GetValueNames()
                .Select(name => serialPortsKey.GetValue(name) as string)
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Select(port => port!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            PortComboBox.ItemsSource = ports;
            if (ports.Length > 0)
            {
                PortComboBox.SelectedIndex = 0;
                PortScanStatusText.Text = $"检测到 {ports.Length} 个串口。这里只枚举系统端口，尚未连接设备。";
                PortScanStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
            else
            {
                PortScanStatusText.Text = "未检测到串口。请检查设备、驱动或 USB 转串口连接后重新扫描。";
                PortScanStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            PortComboBox.ItemsSource = Array.Empty<string>();
            PortScanStatusText.Text = $"扫描串口失败：{exception.Message}";
            PortScanStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private static int GetComPortNumber(string port)
    {
        return port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(port.AsSpan(3), out var number)
                ? number
                : int.MaxValue;
    }
}