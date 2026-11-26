using System.Windows;

namespace CSharpProject;

/// <summary>
/// MVVM 的 View 层入口。
/// <see cref="Window"/> 是 WPF 基类，<see cref="MainWindow"/> 负责加载 XAML、
/// 创建 ViewModel 并在窗口关闭时释放其资源；页面状态和交互逻辑由 ViewModel 管理。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(new WpfInteractionService(this));
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}
