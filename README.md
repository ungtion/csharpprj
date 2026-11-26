# 设备监控中心

这是一个基于 WPF 的 Windows 桌面示例项目，提供设备监控界面、串口收发、ADC 采集记录，以及 Modbus TCP 寄存器只读测试。

> 注意：设备总览和实时监控中的温度、压力、转速、稼动率是程序生成的模拟数据；配方和报警页面也是界面示例。当前实际设备通信功能是串口收发、ADC 串口采集和 Modbus TCP 只读寄存器读取。

## 目录

- [运行环境与启动](#运行环境与启动)
- [界面功能与使用方法](#界面功能与使用方法)
- [主要文件职责](#主要文件职责)
- [关键字段与变量](#关键字段与变量)
- [通信数据流程](#通信数据流程)

## 运行环境与启动

- Windows
- .NET 10 SDK
- WPF
- NuGet 包：`System.IO.Ports`（项目文件中已声明）

在项目目录运行：

```powershell
dotnet run --project .\CSharpProject.csproj
```

也可以用 Visual Studio 或 VS Code 打开 `CSharpProject.csproj` 后运行。程序启动时由 `App.xaml` 打开 `MainWindow.xaml`。

## 界面功能与使用方法

### 设备总览

左侧导航的“设备总览”页面包含模拟测点和通用串口面板。

**模拟测点**

- 主窗口创建后立即显示一组模拟值，之后由定时器每秒更新。
- 温度、压力、转速和稼动率不是从 PLC 或传感器读取的。

**通用串口收发**

1. 点击“扫描”，从系统枚举可用 COM 端口。
2. 选择端口和波特率，再点击“打开串口”。
3. 在发送框输入内容并选择发送方式：
   - 普通文本：以 UTF-8 发送；勾选“追加 `\r\n`”时在末尾添加换行。
   - 十六进制：勾选“十六进制发送”，输入如 `01 03 00 00 00 0A`；支持空格、逗号、短横线、分号、冒号分隔，也支持 `0x` 前缀。每个字节必须由两个十六进制数字组成。
   - Modbus RTU CRC：仅在十六进制模式下可勾选；在输入帧后追加 CRC-16/MODBUS，低字节先发。
4. 接收内容显示在接收区。可以复制或保存当前接收区文本，也可以清空接收区。
5. 不再使用时点击“关闭串口”。串口接收区以文本方式显示数据；如果要处理任意二进制数据，应使用字节缓冲区而不是依赖文本解码。

### ADC 采集

ADC 页面使用独立的串口会话，与设备总览中的通用串口互不影响。

1. 刷新并选择 ADC 串口、波特率（默认 `115200`）。
2. 选择 TXT 保存位置；初始路径为程序当前工作目录下的 `adc_record.txt`。
3. 输入产品 SN，按回车或点击“确认并自动采集”。串口未打开时，程序会绑定 SN 并尝试自动打开串口；也可以手动打开。
4. 页面按行读取串口输出，识别包含 `Sensor Voltage:` 的行，并解析标记后紧邻的数字作为 ADC 值。
5. 当前值会显示在页面上。代码将 `1700 <= ADC < 3200` 视为自动保存目标范围；命中后对当前 SN 自动保存一次。也可以点击“保存当前电压值”手动保存。
6. 更改 SN 后会重置当前采集的区间状态。采集记录和区间变化日志会追加写入所选 TXT 文件。

保存失败或串口异常会显示在 ADC 日志中。ADC 串口文本解析、SN/区间状态、判定和记录写入由 `AdcCollectionService.cs` 负责；`MainWindow.xaml.cs` 负责串口连接和界面更新。如需匹配实际产品标准，应以设备/工艺规格确认服务中的 ADC 阈值。

### MC405 Modbus TCP 通讯测试

该页面提供 Modbus TCP 连接和寄存器读取，不提供寄存器写入或运动控制。

1. 输入控制器主机地址和 TCP 端口（默认 `192.168.0.250:502`）。
2. 点击“连接”建立 TCP 连接。
3. 选择功能码 `03`（保持寄存器）或 `04`（输入寄存器），填写 Unit ID、起始地址和数量。
4. 点击“读取寄存器”，结果以十进制和十六进制显示，日志中同时记录请求帧与响应帧。
5. 点击“断开”释放连接。读取数量限制为 `1–125`，地址范围不能超过 `65535`。

### 其他页面

- **实时监控**：展示与设备总览相同的模拟测点。
- **bake**：配方信息为静态示例；“计次示例”按钮只演示事件响应。
- **报警记录**：当前为静态示例，尚未接入实时报警、确认或历史存储。
- **系统信息**：展示界面中的系统信息内容。

## 主要文件职责

| 文件 | 职责 |
|---|---|
| `App.xaml` | WPF 应用入口配置、启动窗口和应用级颜色/控件样式。 |
| `App.xaml.cs` | 应用启动时注册 UI 线程未处理异常提示。 |
| `MainWindow.xaml` | 主窗口布局、导航、页面控件、控件名称和事件绑定。 |
| `MainWindow.xaml.cs` | 页面切换、模拟数据、界面事件、通用串口和 ADC 串口连接、Modbus TCP 页面交互。 |
| `AdcCollectionService.cs` | 封装 ADC 接收文本解析、采集状态与区间判定、记录文件写入。 |
| `SerialPortSession.cs` | 封装单个串口的打开、关闭、文本/字节发送和后台接收缓冲。 |
| `HexText.cs` | 十六进制文本与字节数组之间的解析和格式化。 |
| `ModbusCrc16.cs` | 计算 CRC-16/MODBUS、为 RTU 帧追加 CRC，并格式化 CRC 字节。 |
| `ModbusTcpSession.cs` | 封装 Modbus TCP 连接及功能码 03/04 的寄存器读取、响应校验和资源释放。 |
| `CSharpProject.csproj` | 项目目标框架、WPF 配置和 NuGet 依赖。 |

## 关键字段与变量

### `MainWindow.xaml.cs`

| 字段 | 用途 |
|---|---|
| `MaxReceiveLength` | 接收区及日志文本的最大保留长度，超出时截掉较早内容。 |
| `_simulationTimer` | 每秒触发一次模拟测点刷新。 |
| `_receiveFlushTimer` | 每 60 毫秒从串口会话取出累积数据并更新界面。 |
| `_serialSession` | 设备总览通用串口的会话对象。 |
| `_adcSerialSession` | ADC 页面专用串口会话对象，与 `_serialSession` 分离。 |
| `_adcCollectionService` | ADC 业务对象，负责解析接收文本、维护本次采集状态、区间判定和记录写入。 |
| `_modbusTcpSession` | MC405 Modbus TCP 连接和读取会话。 |
| `_totalReceivedBytes` | 通用串口接收区当前统计的字节数；清空接收区时归零。 |
| `_demoClickCount` | “计次示例”按钮累计点击次数。 |
| `NavigationButtons` / `NavigationPages` | 导航按钮与页面的对应集合，供 `ShowPage` 同步切换状态。 |

### `AdcCollectionService.cs`

| 字段/属性 | 用途 |
|---|---|
| `SavePath` | ADC 记录文件的写入路径。 |
| `SerialNumber` / `CurrentValue` | 当前绑定的产品 SN 和最近解析到的 ADC 值。 |
| `_lineBuffer` | 暂存未完整的串口行，待后续数据到达后再解析。 |
| `_fixedRangeText` / `_count` | 当前区间文本和区间编号。 |
| `_autoSaved` | 当前 SN 是否已经触发自动保存。 |
| `_lastAdc` | 上次写入区间日志的 ADC 值，用于过滤相同值的重复日志。 |

### `SerialPortSession.cs`

| 字段/属性 | 用途 |
|---|---|
| `_sync` | 保护接收缓冲区、接收字节计数和故障信息的线程锁。 |
| `_pendingText` | 暂存后台串口回调收到、尚未显示的文本。 |
| `_port` | 当前 `SerialPort` 实例；未连接时为 `null`。 |
| `_pendingByteCount` | 暂存文本对应的接收字节数，取出缓冲时一起清零。 |
| `_faultMessage` | 暂存最近一次后台接收故障，供界面线程显示。 |
| `_disposed` | 标记会话是否已释放。 |
| `IsOpen` | 反映串口当前是否打开。 |
| `PortName` / `BaudRate` | 当前已打开端口的名称和波特率。 |

串口参数目前固定为 8 数据位、无校验、1 停止位、无流控（8-N-1）。

### `ModbusTcpSession.cs`

| 字段/属性 | 用途 |
|---|---|
| `TimeoutMilliseconds` | TCP 连接和单次读操作的超时时间，当前为 3000 毫秒。 |
| `_client` | 当前 TCP 客户端。 |
| `_transactionId` | 为 Modbus TCP 请求生成事务标识，并用于匹配响应。 |
| `IsConnected` | 当前连接状态。 |
| `ModbusTcpReadResult` | 保存解析出的寄存器，以及对应的请求帧和响应帧。 |
| `ModbusTcpException.ExceptionCode` | 设备返回 Modbus 异常响应时携带的异常码。 |

## 通信数据流程

### 通用串口

1. `MainWindow` 调用 `SerialPortSession.Open` 打开端口。
2. `SerialPort.DataReceived` 在后台线程触发；`OnDataReceived` 只读取数据并写入受锁保护的缓冲区，不直接操作 WPF 控件。
3. `_receiveFlushTimer` 在 UI 线程定时调用 `TryTakePendingText` 和 `TryTakeFault`。
4. `MainWindow` 更新接收文本框、字节数和日志。
5. 关闭窗口时停止定时器并释放串口与 TCP 会话。

### 十六进制发送

`SendClick` → `HexText.TryParse` →（可选）`ModbusCrc16.AppendCrc` → `SerialPortSession.WriteBytes` → `HexText.Format` 记录发送日志。

### ADC 采集

串口后台缓冲 → UI 定时取出 → `_adcLineBuffer` 拼接完整行 → `ProcessAdcLine` 解析 `Sensor Voltage:` → `OnAdcValue` 更新界面、判定区间并按规则写入 TXT。

### Modbus TCP 读取

`MainWindow` 验证界面输入 → `ModbusTcpSession.ConnectAsync` 建立连接 → `ReadRegistersAsync` 发送功能码 03/04 请求并校验响应 → 返回寄存器和帧数据供界面显示。
