下面按“控件家族”分类，把 WPF 常用控件**逐一给出可运行的最小示例**（XAML 为主，必要处补 C#）。先给一张继承体系图，理解了它你就知道“为什么 Button 里能塞图片”“为什么 ComboBox 能自定义每一项”。

## 0. 控件继承体系（理解一切的前提）

```
DependencyObject
 └ FrameworkElement        ← 有 Width/Height/Margin/Resources/Style
    ├ Panel                ← 纯布局容器：Grid/StackPanel/DockPanel/WrapPanel/Canvas/UniformGrid
    ├ Shape                ← 图形：Rectangle/Ellipse/Line/Polyline/Polygon/Path
    ├ TextBlock            ← 轻量文本（不是 Control！）
    └ Control              ← 有 Template/FontSize/Background/Foreground/Padding
       ├ ContentControl    ← 有唯一 Content：Button/CheckBox/RadioButton/Label/ToolTip/Window/UserControl/GroupBox/Expander
       ├ ItemsControl      ← 有 Items/ItemsSource/ItemTemplate：ListBox/ListView/ComboBox/TreeView/TabControl/Menu/DataGrid
       ├ RangeBase         ← 有 Minimum/Maximum/Value：Slider/ProgressBar/ScrollBar
       └ TextBoxBase       ← TextBox/RichTextBox
```

两条最有用的推论：
1. **凡是 `ContentControl`，`Content` 都能是任意 UI**（按钮里放图标+文字、`ToolTip` 里放整个 Grid）。
2. **凡是 `ItemsControl`，都支持 `ItemsSource` + `ItemTemplate`**（ListBox / ComboBox / DataGrid / TreeView 全是这个套路）。

---

# 一、布局面板（Panel 家族）

## 1.1 Grid —— 行列网格（最重要的布局）

```xml
<Grid ShowGridLines="False">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />   <!-- 随内容 -->
        <RowDefinition Height="*" />      <!-- 占剩余 -->
        <RowDefinition Height="2*" />     <!-- 剩余空间的两份 -->
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="200" />  <!-- 固定 -->
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <TextBlock Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" Text="横跨两列的标题" />
    <Button   Grid.Row="1" Grid.Column="1" Content="按钮" />
</Grid>
```

| 关键点 | 说明 |
|---|---|
| `Grid.Row` / `Grid.Column` | 不写默认 `0` |
| `Grid.RowSpan` / `ColumnSpan` | 跨行 / 跨列 |
| 同一格多个元素 | 后写的盖住先写的（你们做页面切换就靠这个） |
| `GridSplitter` | 可拖动的分隔条（见 1.8） |

## 1.2 StackPanel —— 单向顺序排列

```xml
<StackPanel Orientation="Vertical" Spacing="8">   <!-- Spacing 需 .NET 9+ / WPF 新版才有 -->
    <TextBlock Text="第一行" />
    <TextBlock Text="第二行" />
</StackPanel>
<StackPanel Orientation="Horizontal">
    <Button Content="确定" Margin="0,0,8,0" />
    <Button Content="取消" />
</StackPanel>
```

> 旧版 WPF 没有 `Spacing`，用 `Margin` 代替（你项目里全是这么写的）。

## 1.3 DockPanel —— 贴边停靠

```xml
<DockPanel LastChildFill="True">
    <Border DockPanel.Dock="Top"    Height="50" Background="#EEE" />
    <Border DockPanel.Dock="Bottom" Height="30" Background="#DDD" />
    <Border DockPanel.Dock="Left"   Width="200" Background="#CCC" />
    <Border Background="#FFF" />   <!-- 最后一个自动填满剩余 -->
</DockPanel>
```

顺序很重要：**先写的先占位置**。你项目 `MainWindow.xaml:80` 的侧边栏就是这个。

## 1.4 WrapPanel —— 排不下自动换行

```xml
<WrapPanel Orientation="Horizontal" ItemWidth="120">
    <Button Content="1" Margin="4" />
    <Button Content="2" Margin="4" />
    <Button Content="3" Margin="4" />
    <!-- 宽度不够时自动折到下一行 -->
</WrapPanel>
```

## 1.5 UniformGrid —— 等分格子

```xml
<UniformGrid Columns="3" Rows="2">   <!-- Columns/Rows 只写一个，另一个自动推算 -->
    <Button Content="A" /> <Button Content="B" /> <Button Content="C" />
    <Button Content="D" /> <Button Content="E" /> <Button Content="F" />
</UniformGrid>
```

你项目 `MainWindow.xaml:223` 的四个指标卡就是它。

## 1.6 Canvas —— 绝对定位（不参与自动布局）

```xml
<Canvas Background="#F8F8F8">
    <Rectangle Canvas.Left="20" Canvas.Top="30" Width="80" Height="40" Fill="SteelBlue" />
    <TextBlock Canvas.Left="30" Canvas.Top="40" Text="绝对定位" Foreground="White" />
</Canvas>
```

适合画图、仪表盘指针；不适合做常规界面（不会自适应）。

## 1.7 Border —— 圆角/描边/背景（你项目用得最多）

```xml
<Border Background="White" BorderBrush="#E5EAF0" BorderThickness="1"
        CornerRadius="8" Padding="18">
    <TextBlock Text="卡片内容" />
</Border>
```

## 1.8 GridSplitter —— 可拖动分隔条

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="250" MinWidth="150" />
        <ColumnDefinition Width="5" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <Border Grid.Column="0" Background="#172337" />
    <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch"
                  Background="#CCC" ResizeBehavior="PreviousAndNext" />
    <Border Grid.Column="2" Background="#F1F4F8" />
</Grid>
```

## 1.9 ScrollViewer —— 滚动容器

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled">
    <StackPanel> ...很长的内容... </StackPanel>
</ScrollViewer>
```

## 1.10 Viewbox —— 整体等比缩放

```xml
<Viewbox Stretch="Uniform" MaxWidth="200">
    <TextBlock Text="大标题自动缩放到容器大小" FontSize="48" />
</Viewbox>
```

## 1.11 GroupBox / Expander —— 分组与折叠

```xml
<GroupBox Header="串口设置" Margin="8">
    <StackPanel>
        <TextBox Text="COM3" />
        <TextBlock Text="波特率 9600" />
    </StackPanel>
</GroupBox>

<Expander Header="高级选项" IsExpanded="False">
    <TextBlock Text="展开后才显示的内容" />
</Expander>
```

---

# 二、文本与图片

## 2.1 TextBlock —— 显示文本（不是控件，最轻量）

```xml
<TextBlock Text="温度：42.6 °C" FontSize="26" FontWeight="SemiBold"
           Foreground="#26364A" TextWrapping="Wrap" TextTrimming="CharacterEllipsis" />

<!-- 富文本内联 -->
<TextBlock>
    当前状态：<Run Text="运行中" Foreground="SeaGreen" FontWeight="Bold" />
    <LineBreak />
    详见 <Hyperlink NavigateUri="https://example.com">文档</Hyperlink>
</TextBlock>
```

## 2.2 Label —— 带访问键（Alt+字母）的标签

```xml
<Label Content="_名称" Target="{Binding ElementName=NameBox}" />
<TextBox x:Name="NameBox" />
<!-- 按 Alt+N 会聚焦到 NameBox -->
```

## 2.3 TextBox / PasswordBox / RichTextBox —— 输入

```xml
<TextBox x:Name="CommentBox" Text="默认值" MaxLength="100"
         AcceptsReturn="True" TextWrapping="Wrap" Height="60" />

<PasswordBox x:Name="PwdBox" PasswordChar="●" />

<RichTextBox Height="100">
    <FlowDocument>
        <Paragraph><Bold>粗体</Bold> 与 <Italic>斜体</Italic></Paragraph>
    </FlowDocument>
</RichTextBox>
```

```csharp
// TextBox：立刻拿到内容
string text = CommentBox.Text;
// PasswordBox 出于安全不能双向绑定，只能读
string pwd = PwdBox.Password;
```

## 2.4 Image —— 显示图片

```xml
<Image Source="/Assets/logo.png" Width="32" Height="32" Stretch="Uniform" />

<!-- 代码里加载 -->
<!--
image.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
-->
```

---

# 三、按钮家族（都继承 ButtonBase）

## 3.1 Button —— 普通按钮

```xml
<Button Content="保存" Width="80" Height="32" Click="SaveClick"
        Background="#16866F" Foreground="White" BorderThickness="0" Cursor="Hand" />

<!-- 按钮里放任意内容（因为 Content 是 object） -->
<Button Padding="10">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="⚙" Margin="0,0,6,0" />
        <TextBlock Text="设置" />
    </StackPanel>
</Button>
```

## 3.2 ToggleButton —— 可保持按下状态

```xml
<ToggleButton x:Name="PumpToggle" Content="启动泵" IsChecked="False"
              Checked="PumpOn" Unchecked="PumpOff" />
```

```csharp
private void PumpOn(object sender, RoutedEventArgs e)  => Debug.WriteLine("启动");
private void PumpOff(object sender, RoutedEventArgs e) => Debug.WriteLine("停止");
```

## 3.3 RepeatButton —— 按住连续触发（做 +/- 微调）

```xml
<RepeatButton Content="＋" Delay="400" Interval="80" Click="IncreaseClick" />
<!-- Delay：首次延迟；Interval：之后每次间隔(ms) -->
```

## 3.4 RadioButton / CheckBox —— 单选 / 多选

```xml
<RadioButton GroupName="Mode" Content="自动模式" IsChecked="True" />
<RadioButton GroupName="Mode" Content="手动模式" />

<CheckBox Content="记录日志" IsChecked="True" />
<CheckBox Content="三态示例" IsThreeState="True" IsChecked="{x:Null}" />
```

```csharp
// IsChecked 是 bool?，判空很重要
if (LogCheck.IsChecked == true) { /* 勾选 */ }
```

> 注意：`RadioButton` 放在**同一个父容器**里默认就互斥，跨容器要用 `GroupName`。

---

# 四、选择类控件（ItemsControl 家族）

## 4.1 ComboBox —— 下拉选择

```xml
<ComboBox x:Name="PortComboBox" Width="120" SelectedIndex="0"
          SelectionChanged="PortChanged"
          DisplayMemberPath="Name" />   <!-- 或直接放字符串 -->
```

```csharp
PortComboBox.ItemsSource = new[] { "COM1", "COM3", "COM5" };   // 你项目里就是这么做的
string? port = PortComboBox.SelectedItem as string;
```

可编辑版本（能输入新值）：`IsEditable="True" IsTextSearchEnabled="True"`。

## 4.2 ListBox —— 列表单选/多选

```xml
<ListBox x:Name="AlarmList" SelectionMode="Extended" Height="150">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Time}" Width="90" />
                <TextBlock Text="{Binding Message}" Foreground="Firebrick" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

```csharp
AlarmList.ItemsSource = new List<Alarm>
{
    new() { Time = "10:20", Message = "温度超限" },
    new() { Time = "10:31", Message = "压力异常" },
};
public record Alarm(string Time, string Message);   // 简化写法
```

## 4.3 ListView + GridView —— 带表头的列表

```xml
<ListView Height="160">
    <ListView.View>
        <GridView>
            <GridViewColumn Header="时间" Width="100" DisplayMemberBinding="{Binding Time}" />
            <GridViewColumn Header="内容" Width="200" DisplayMemberBinding="{Binding Message}" />
            <GridViewColumn Header="状态" Width="80">
                <GridViewColumn.CellTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding State}" Foreground="SeaGreen" />
                    </DataTemplate>
                </GridViewColumn.CellTemplate>
            </GridViewColumn>
        </GridView>
    </ListView.View>
</ListView>
```

## 4.4 DataGrid —— 可编辑表格（报警记录最合适）

```xml
<DataGrid x:Name="AlarmGrid" AutoGenerateColumns="False" IsReadOnly="True"
          CanUserAddRows="False" HeadersVisibility="Column"
          GridLinesVisibility="Horizontal" AlternatingRowBackground="#F7F9FB">
    <DataGrid.Columns>
        <DataGridTextColumn  Header="时间"  Binding="{Binding Time}"  Width="120" />
        <DataGridTextColumn  Header="内容"  Binding="{Binding Message}" Width="*" />
        <DataGridCheckBoxColumn Header="已确认" Binding="{Binding Ack}"  Width="70" />
        <DataGridTemplateColumn Header="操作" Width="90">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Button Content="确认" Click="AckClick" />
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
    </DataGrid.Columns>
</DataGrid>
```

## 4.5 TreeView —— 树形结构

```xml
<TreeView Height="160">
    <TreeViewItem Header="设备 1" IsExpanded="True">
        <TreeViewItem Header="进料泵" />
        <TreeViewItem Header="处理单元" />
    </TreeViewItem>
    <TreeViewItem Header="设备 2" />
</TreeView>
```

数据绑定时用 `HierarchicalDataTemplate`（`ItemsSource="{Binding Children}"`）。

## 4.6 TabControl —— 分页标签

```xml
<TabControl>
    <TabItem Header="实时数据">
        <TextBlock Text="第一页内容" />
    </TabItem>
    <TabItem Header="历史记录">
        <TextBlock Text="第二页内容" />
    </TabItem>
</TabControl>
```

## 4.7 Slider —— 拖动选值（RangeBase）

```xml
<Slider x:Name="SpeedSlider" Minimum="0" Maximum="2000" Value="1480"
        TickFrequency="100" IsSnapToTickEnabled="True"
        ValueChanged="SpeedChanged" />
```

## 4.8 DatePicker / Calendar —— 日期

```xml
<DatePicker SelectedDate="{Binding StartDate}" DisplayDateStart="2020-01-01" />

<Calendar SelectionMode="SingleRange" />
```

## 4.9 数值微调（NumericUpDown）

**WPF 没有内置 NumericUpDown**，三种做法：

```xml
<!-- A. Slider + TextBox 组合 -->
<Slider x:Name="S" Minimum="0" Maximum="100" Value="50" Width="140" />
<TextBox Text="{Binding Value, ElementName=S, StringFormat=F1}" Width="60" />

<!-- B. 两个 RepeatButton 夹一个 TextBox（自制） -->
<StackPanel Orientation="Horizontal">
    <RepeatButton Content="－" Click="DecClick" />
    <TextBox x:Name="NumBox" Text="0" Width="50" TextAlignment="Center" />
    <RepeatButton Content="＋" Click="IncClick" />
</StackPanel>
```

C 方案：NuGet 装 `Xceed.Wpf.Toolkit` 直接用官方 `IntegerUpDown` / `DoubleUpDown`。

---

# 五、进度与状态显示

```xml
<!-- 确定进度 -->
<ProgressBar Value="62" Maximum="100" Height="8" Foreground="#159B78" />

<!-- 不确定进度（转圈） -->
<ProgressBar IsIndeterminate="True" Height="8" />
```

```csharp
// 用定时器或 Task 更新
Progress.Value = 62;
```

---

# 六、菜单 / 工具栏 / 状态栏 / 右键菜单

## 6.1 Menu

```xml
<Menu DockPanel.Dock="Top">
    <MenuItem Header="文件(_F)">
        <MenuItem Header="打开" InputGestureText="Ctrl+O" Click="OpenClick" />
        <MenuItem Header="保存" InputGestureText="Ctrl+S" />
        <Separator />
        <MenuItem Header="退出" Click="ExitClick" />
    </MenuItem>
    <MenuItem Header="帮助">
        <MenuItem Header="关于" />
    </MenuItem>
</Menu>
```

## 6.2 ContextMenu（右键菜单，任何元素都能加）

```xml
<TextBox Text="在这里右键">
    <TextBox.ContextMenu>
        <ContextMenu>
            <MenuItem Header="复制" Command="ApplicationCommands.Copy" />
            <MenuItem Header="粘贴" Command="ApplicationCommands.Paste" />
            <Separator />
            <MenuItem Header="清空" Click="ClearClick" />
        </ContextMenu>
    </TextBox.ContextMenu>
</TextBox>
```

## 6.3 ToolBar

```xml
<ToolBarTray DockPanel.Dock="Top">
    <ToolBar>
        <Button Content="▶ 启动" />
        <Button Content="⏸ 暂停" />
        <Separator />
        <ComboBox Width="100"><ComboBoxItem>COM3</ComboBoxItem></ComboBox>
    </ToolBar>
</ToolBarTray>
```

## 6.4 StatusBar

```xml
<StatusBar DockPanel.Dock="Bottom">
    <StatusBarItem Content="就绪" />
    <Separator />
    <StatusBarItem HorizontalAlignment="Right" Content="COM3 已连接" />
</StatusBar>
```

## 6.5 ToolTip（内容可以是任意 UI）

```xml
<Button Content="?">
    <Button.ToolTip>
        <ToolTip>
            <StackPanel>
                <TextBlock Text="参数说明" FontWeight="Bold" />
                <TextBlock Text="值范围 0 ~ 2000" FontSize="11" />
            </StackPanel>
        </ToolTip>
    </Button.ToolTip>
</Button>
```

## 6.6 Popup（浮动弹层，可超出窗口）

```xml
<Popup x:Name="TipPopup" PlacementTarget="{Binding ElementName=PumpToggle}"
       Placement="Bottom" StaysOpen="False" AllowsTransparency="True">
    <Border Background="#333" CornerRadius="4" Padding="8">
        <TextBlock Text="已启动" Foreground="White" />
    </Border>
</Popup>
```

```csharp
TipPopup.IsOpen = true;
```

---

# 七、图形（Shape 家族，位于 `System.Windows.Shapes`）

```xml
<Canvas Height="120" Background="#F7F9FB">
    <Rectangle Canvas.Left="10"  Canvas.Top="10" Width="60" Height="40" Fill="#159B78" />
    <Ellipse   Canvas.Left="90"  Canvas.Top="10" Width="40" Height="40" Fill="Orange" />
    <Line      X1="150" Y1="10" X2="230" Y2="50" Stroke="Gray" StrokeThickness="2" />
    <Polyline  Points="250,50 280,10 310,50 340,10" Stroke="#49B59A" StrokeThickness="2" />
    <Polygon   Points="370,50 400,10 430,50" Fill="SteelBlue" />
    <Path Data="M 450,50 L 480,10 L 510,50 Z" Stroke="Firebrick" Fill="Transparent" />
</Canvas>
```

你的项目 `MainWindow.xaml:490` 那张趋势图就是手搓 `Polyline` 画的。

---

# 八、容器 / 导航 / 弹窗

## 8.1 Window / 自定义对话框

```xml
<!-- SettingsWindow.xaml -->
<Window x:Class="CSharpProject.SettingsWindow" Width="400" Height="300"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="20">
        <TextBlock Text="设置" FontSize="18" />
        <Button Content="确定" Click="OkClick" Margin="0,10,0,0" />
    </StackPanel>
</Window>
```

```csharp
var dlg = new SettingsWindow { Owner = this };
if (dlg.ShowDialog() == true)   // 阻塞直到关闭，返回 DialogResult
{
    /* 刷新 */
}
// dlg.DialogResult = true;  ← 子窗口里这样设置后自动关闭
```

## 8.2 MessageBox

```csharp
MessageBox.Show("保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

var r = MessageBox.Show("确定退出？", "确认",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
if (r == MessageBoxResult.Yes) { Application.Current.Shutdown(); }
```

## 8.3 文件对话框

```csharp
var dlg = new OpenFileDialog
{
    Filter = "CSV 文件|*.csv|所有文件|*.*",
    Title = "选择配方文件"
};
if (dlg.ShowDialog() == true)
{
    string path = dlg.FileName;
}
// 保存用 SaveFileDialog（FileName / DefaultExt / AddExtension）
```

## 8.4 ContentControl / UserControl / Frame

```xml
<!-- UserControl：可复用的自定义控件 -->
<UserControl x:Class="CSharpProject.DeviceCard">
    <Border Style="{StaticResource CardStyle}">
        <ContentPresenter />   <!-- 或直接写内部布局 -->
    </Border>
</UserControl>
```

```xml
<!-- 在页面里用 -->
<local:DeviceCard Width="200" Height="100" />
```

```xml
<!-- Frame + Page：做导航页面 -->
<Frame x:Name="MainFrame" Source="/Pages/OverviewPage.xaml"
       NavigationUIVisibility="Hidden" />
```

---

# 九、多媒体 / 特殊控件

| 控件 | 用途 | 备注 |
|---|---|---|
| `MediaElement` | 播放音视频 | `<MediaElement Source="a.mp4" LoadedBehavior="Manual" x:Name="M"/>` + `M.Play()`；更省资源用 `MediaPlayer` + `VideoDrawing` |
| `InkCanvas` | 手写笔迹 | 触摸屏签字、批注 |
| `WebView2` | 内嵌浏览器 | **需 NuGet** `Microsoft.Web.WebView2`，非内置 |
| `DocumentViewer` / `FlowDocumentScrollViewer` | 显示文档 | 打印预览、帮助文档 |
| `Calendar` / `DatePicker` | 日期 | 见 4.8 |
| `Separator` | 分隔线 | 菜单/工具栏里 |
| `AdornerDecorator` | 装饰层 | 拖拽高亮等高级用法 |

---

# 十、你项目现状对照表

已用到的（`MainWindow.xaml` 统计）：

| 控件 | 次数 | 位置 |
|---|---|---|
| `TextBlock` | 171 | 全部文字 |
| `StackPanel` | 58 | 导航、卡片内部 |
| `Border` | 41 | 卡片、圆角容器 |
| `Grid` | 33 | 布局骨架 |
| `Button` | 12 | 导航按钮、测试按钮 |
| `ScrollViewer` | 5 | 各页面滚动 |
| `UniformGrid` | 2 | 指标卡、实时页 |
| `ComboBox` | 1 | 串口下拉框 |
| `Polyline` | 1 | 趋势图 |
| `DockPanel` | 1 | 侧边栏 |

**对你的“设备监控 HMI”最有价值、目前还没用的控件**（按优先级）：

1. **`DataGrid`** —— 报警记录页（`第 652 行` 现在是手搓 Grid，改成 DataGrid 才能排序/滚动/自动更新）
2. **`ProgressBar`** —— 工艺进度、料位
3. **`ToggleButton`** —— 泵/阀的启停开关
4. **`TabControl`** —— 实时监控页里“温度/压力/流量”分页
5. **`DatePicker`** —— 历史查询时间范围
6. **`TreeView`** —— 设备树（多台设备 + 子单元）
7. **`Slider`** —— 设定值调节（温度设定）
8. **`ToolTip` / `ContextMenu`** —— 工业界面里操作安全性必需
9. **`GridSplitter`** —— 让侧边栏可拖动改宽
10. **`StatusBar`** —— 底部显示串口连接状态/错误

---

# 十一、一分钟速查表

| 控件 | 一句话 | 必记属性 |
|---|---|---|
| `Grid` | 行列布局骨架 | `RowDefinitions`/`ColumnDefinitions`/`Grid.Row` |
| `StackPanel` | 单方向堆叠 | `Orientation` |
| `DockPanel` | 贴边停靠 | `DockPanel.Dock`、`LastChildFill` |
| `WrapPanel` | 自动换行 | `ItemWidth`/`ItemHeight` |
| `UniformGrid` | 等分格子 | `Columns`/`Rows` |
| `Canvas` | 绝对定位 | `Canvas.Left`/`Top` |
| `Border` | 圆角描边背景 | `CornerRadius`/`BorderThickness`/`Padding` |
| `ScrollViewer` | 滚动 | `VerticalScrollBarVisibility` |
| `Viewbox` | 等比缩放 | `Stretch` |
| `GroupBox`/`Expander` | 分组/折叠 | `Header`/`IsExpanded` |
| `TextBlock` | 显示文本 | `Text`/`TextWrapping`/`Foreground` |
| `Label` | 带 Alt 访问键 | `Target` |
| `TextBox` | 单/多行输入 | `Text`/`AcceptsReturn`/`MaxLength` |
| `PasswordBox` | 密码 | `Password`（不能绑定） |
| `Image` | 图片 | `Source`/`Stretch` |
| `Button` | 按钮 | `Click`/`IsDefault`/`IsCancel` |
| `ToggleButton` | 开关 | `IsChecked` |
| `RepeatButton` | 连发 | `Delay`/`Interval` |
| `CheckBox` | 多选 | `IsChecked`(bool?)/`IsThreeState` |
| `RadioButton` | 单选 | `GroupName`/`IsChecked` |
| `ComboBox` | 下拉 | `ItemsSource`/`SelectedItem`/`IsEditable` |
| `ListBox` | 列表 | `ItemsSource`/`SelectionMode`/`ItemTemplate` |
| `ListView` | 带表头列表 | `GridView`/`GridViewColumn` |
| `DataGrid` | 表格 | `AutoGenerateColumns`/`Columns`/`IsReadOnly` |
| `TreeView` | 树 | `HierarchicalDataTemplate` |
| `TabControl` | 标签页 | `TabItem.Header` |
| `Slider` | 拖动取值 | `Minimum`/`Maximum`/`Value`/`IsSnapToTickEnabled` |
| `ProgressBar` | 进度 | `Value`/`IsIndeterminate` |
| `DatePicker` | 日期 | `SelectedDate` |
| `Menu`/`MenuItem` | 菜单 | `Header`/`InputGestureText` |
| `ContextMenu` | 右键菜单 | 任意元素 `.ContextMenu` |
| `ToolBar`/`StatusBar` | 工具栏/状态栏 | `DockPanel.Dock` |
| `ToolTip` | 悬浮提示 | 可为任意 UI |
| `Popup` | 浮动层 | `IsOpen`/`PlacementTarget` |
| `Window` | 窗口 | `ShowDialog()`/`DialogResult` |
| `UserControl` | 复用控件 | `x:Class` + `ContentPresenter` |
| `Frame`+`Page` | 导航 | `Source`/`NavigationUIVisibility` |
| `Rectangle`/`Ellipse`/`Line`/`Polyline`/`Polygon`/`Path` | 图形 | `Fill`/`Stroke`/`Points`/`Data` |
| `MediaElement` | 音视频 | `Source`/`LoadedBehavior` |

需要我把其中某个（比如 **DataGrid 改造报警页** 或 **ToggleButton 控制泵启停**）直接落地到你 `MainWindow.xaml` 里吗？