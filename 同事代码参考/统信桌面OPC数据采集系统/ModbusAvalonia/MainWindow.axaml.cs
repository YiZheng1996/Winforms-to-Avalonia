using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PressureTestApp.Communication.Plc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ModbusAvalonia
{
    /// <summary>主窗口：负责连接设备、加载配置、生成点卡片并实时刷新数据。</summary>
    public partial class MainWindow : Window
    {
        /// <summary>串口服务器通信器字典（键为 IP:端口）。</summary>
        private Dictionary<string, IXdpPlcCommunicator> _communicators = new Dictionary<string, IXdpPlcCommunicator>();

        /// <summary>当前加载的配置对象。</summary>
        private XdpConfig _config = null;

        /// <summary>Smart200 PLC 通信器列表。</summary>
        private List<Smart200> _smart200List = new List<Smart200>();

        /// <summary>信捷 PLC 通信器列表。</summary>
        private List<XinjePlcCommunicator> _xinjeList = new List<XinjePlcCommunicator>();

        /// <summary>S7-1200 PLC 通信器列表。</summary>
        private List<S71200> _s71200List = new List<S71200>();

        /// <summary>串口 Modbus 设备通信器列表。</summary>
        private List<SerialModbusCommunicator> _serialList = new List<SerialModbusCommunicator>();

        /// <summary>标签名 → 读取值显示控件 的映射。</summary>
        private Dictionary<string, TextBlock> tagControls = new Dictionary<string, TextBlock>();

        /// <summary>UI 刷新线程。</summary>
        private Thread updateThread = null;

        /// <summary>是否持续刷新 UI。</summary>
        private bool isUpdating = false;

        /// <summary>构造函数：初始化界面并注册关闭时的清理逻辑。</summary>
        public MainWindow()
        {
            InitializeComponent();
            this.Closed += (s, e) => Cleanup();
        }

        /// <summary>生成串口服务器的唯一键（IP:端口）。</summary>
        private string ServerKey(string ip, int port) { return ip + ":" + port; }

        /// <summary>是否存在任意已连接的通信器（串口服务器 / PLC / 串口设备）。</summary>
        private bool IsAnyConnected
        {
            get
            {
                return _communicators.Values.Any(c => c.IsConnected)
                    || _smart200List.Any(c => c.IsConnected)
                    || _xinjeList.Any(c => c.IsConnected)
                    || _s71200List.Any(c => c.IsConnected)
                    || _serialList.Any(c => c.IsConnected);
            }
        }

        /// <summary>汇总所有通信器（串口服务器/PLC/串口设备）的实时点数据。</summary>
        private List<XdpPlcDataModel> GetAllStatusList()
        {
            var all = new List<XdpPlcDataModel>();
            foreach (var comm in _communicators.Values)
            {
                var list = comm.GetPlcStatusList();
                if (list != null) all.AddRange(list);
            }

            foreach (var smart in _smart200List)
            {
                var s7List = smart.GetPlcStatusList();
                if (s7List != null)
                {
                    foreach (var s7 in s7List)
                        all.Add(ConvertS7ToDisplay(s7));
                }
            }

            foreach (var xinje in _xinjeList)
            {
                var xinjeList = xinje.GetPlcStatusList();
                if (xinjeList != null)
                    all.AddRange(xinjeList);
            }

            foreach (var s71200 in _s71200List)
            {
                var s71200List = s71200.GetPlcStatusList();
                if (s71200List != null)
                {
                    foreach (var d in s71200List)
                        all.Add(ConvertS71200ToDisplay(d));
                }
            }

            foreach (var serial in _serialList)
            {
                var serialList = serial.GetPlcStatusList();
                if (serialList != null)
                    all.AddRange(serialList);
            }

            return all;
        }

        /// <summary>根据标签名查找包含该标签的通信器。</summary>
        private IXdpPlcCommunicator GetCommunicatorByTag(string tagName)
        {
            foreach (var comm in _communicators.Values)
            {
                var list = comm.GetPlcStatusList();
                if (list != null && list.Any(d => d.TagName == tagName))
                    return comm;
            }
            foreach (var comm in _serialList)
            {
                var list = comm.GetPlcStatusList();
                if (list != null && list.Any(d => d.TagName == tagName))
                    return comm;
            }
            return null;
        }

        /// <summary>断开所有设备并清空通信器列表。</summary>
        private void DisconnectAllServers()
        {
            foreach (var comm in _communicators.Values)
            {
                try { comm.Disconnect(); }
                catch { }
                try { comm.Dispose(); }
                catch { }
            }
            _communicators.Clear();

            foreach (var smart in _smart200List)
            {
                try { smart.Disconnect(); } catch { }
                try { smart.Dispose(); } catch { }
            }
            _smart200List.Clear();

            foreach (var xinje in _xinjeList)
            {
                try { xinje.Disconnect(); } catch { }
                try { xinje.Dispose(); } catch { }
            }
            _xinjeList.Clear();

            foreach (var s71200 in _s71200List)
            {
                try { s71200.Disconnect(); } catch { }
                try { s71200.Dispose(); } catch { }
            }
            _s71200List.Clear();

            foreach (var serial in _serialList)
            {
                try { serial.Disconnect(); } catch { }
                try { serial.Dispose(); } catch { }
            }
            _serialList.Clear();
        }

        /// <summary>弹出消息提示框。</summary>
        private void ShowMessage(string message, string title = "提示")
        {
            DialogHelper.ShowMessage(this, message, title);
        }

        /// <summary>连接/断开串口服务器。</summary>
        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            string opcPath = Path.Combine(AppContext.BaseDirectory, "OPC");
            string configPath = Path.Combine(opcPath, "OPCConfig.json");
            _config = XdpConfigLoader.Load(configPath);

            try
            {
                // 已连接时再次点击则断开
                if (IsAnyConnected)
                {
                    DisconnectAllServers();
                    btnConnect.Content = "连接";
                    isUpdating = false;
                    txtStatus.Text = "已断开";
                    return;
                }

                if (_config == null || _config.Servers == null || _config.Servers.Count == 0)
                {
                    ShowMessage("配置中没有串口服务器", "错误");
                    return;
                }

                int successCount = 0;
                foreach (var server in _config.Servers)
                {
                    if (server.Enabled != 1) continue;
                    if (string.IsNullOrEmpty(server.IP)) continue;

                    int port = server.Port > 0 ? server.Port : 10001;
                    string key = ServerKey(server.IP, port);
                    if (_communicators.ContainsKey(key)) continue;

                    var comm = new XdpPlcCommunicator(server.IP, port);
                    if (comm.IsConnected)
                    {
                        _communicators[key] = comm;
                        successCount++;
                    }
                    else
                    {
                        try { comm.Dispose(); } catch { }
                    }
                }

                if (successCount > 0)
                {
                    ShowMessage("已连接 " + successCount + " 个串口服务器");
                    btnConnect.Content = "断开";
                    txtStatus.Text = "已连接 " + successCount + " 个串口服务器";
                }
                else
                {
                    ShowMessage("所有串口服务器均连接失败", "错误");
                    txtStatus.Text = "连接失败";
                }
            }
            catch (Exception ex)
            {
                ShowMessage("未知错误：" + ex.Message, "错误");
            }
        }

        /// <summary>加载配置：连接服务器并加载 PLC、串口设备，生成点卡片并启动刷新。</summary>
        private void btnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string opcPath = Path.Combine(AppContext.BaseDirectory, "OPC");

                if (!Directory.Exists(opcPath))
                {
                    ShowMessage("配置目录不存在：" + opcPath, "错误");
                    return;
                }

                string[] csvFiles = Directory.GetFiles(opcPath, "*.csv");
                if (csvFiles.Length == 0)
                {
                    ShowMessage("OPC 目录中没有 CSV 配置文件：" + opcPath, "错误");
                    return;
                }

                if (_config == null)
                {
                    string configPath = Path.Combine(opcPath, "OPCConfig.json");
                    _config = XdpConfigLoader.Load(configPath);
                }

                var enabledFiles = new List<string>();

                // 处理串口服务器：按 SlaveMap 加载每个 CSV 到对应从站 
                foreach (var server in _config.Servers)
                {
                    if (server.Enabled != 1) continue;
                    if (server.SlaveMap == null || server.SlaveMap.Count == 0) continue;

                    string key = ServerKey(server.IP, server.Port > 0 ? server.Port : 10001);
                    if (!_communicators.ContainsKey(key)) continue;

                    var comm = _communicators[key];
                    var serverFiles = csvFiles
                        .Where(f => server.SlaveMap.ContainsKey(Path.GetFileName(f)))
                        .ToArray();

                    if (serverFiles.Length == 0) continue;

                    string configPath = Path.Combine(opcPath, "OPCConfig.json");
                    comm.LoadSlaveIdMap(configPath);
                    comm.MappingPlcFromCSV(serverFiles);

                    enabledFiles.AddRange(serverFiles);
                }

                // 加载 PLC 和串口设备
                LoadPlcs(opcPath);
                LoadSerialDevices(opcPath);

                // 生成点卡片并启动 UI 刷新
                GeneratePointCards(enabledFiles.ToArray());
                StartUpdateThread();

                txtStatus.Text = "配置加载成功！共加载 " + enabledFiles.Count + " 个文件";
                ShowMessage("配置加载成功！共加载 " + enabledFiles.Count + " 个文件");
            }
            catch (Exception ex)
            {
                ShowMessage("加载配置失败：" + ex.Message, "错误");
            }
        }

        /// <summary>打开配置管理窗口。</summary>
        private void btnManageConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new ConfigManagerWindow();
            window.ShowDialog(this);
        }

        /// <summary>退出程序。</summary>
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>读取温湿度（当前已隐藏并屏蔽相关代码）。</summary>
        private void btnReadAmbient_Click(object sender, RoutedEventArgs e)
        {
            /*
            try
            {
                if (!IsAnyConnected)
                {
                    ShowMessage("请先连接 PLC");
                    return;
                }

                var statusList = GetAllStatusList();
                if (statusList == null || statusList.Count == 0)
                {
                    ShowMessage("请先点击“加载配置”");
                    return;
                }

                var ambientList = statusList
                    .Where(d => d.TagName != null && d.TagName.StartsWith("COMServer2_Ambient."))
                    .ToList();

                if (ambientList.Count == 0)
                {
                    ShowMessage("未找到 COMServer2_Ambient 数据");
                    return;
                }

                foreach (var d in ambientList)
                {
                    if (d.Value == null) continue;

                    if (d.Description != null && d.Description.Contains("温度"))
                    {
                        double temp = Convert.ToDouble(d.Value) / 100.0;
                        txtAmbientTemp.Text = "环境温度：" + temp.ToString("F2") + " ℃";
                    }
                    else if (d.Description != null && d.Description.Contains("湿度"))
                    {
                        double hum = Convert.ToDouble(d.Value) / 10.0;
                        txtAmbientHumidity.Text = "环境湿度：" + hum.ToString("F1") + " %";
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage("读取失败：" + ex.Message, "错误");
            }
            */
        }

        /// <summary>加载并连接所有启用的 PLC（Smart200 / 信捷 / S7-1200）。</summary>
        private void LoadPlcs(string opcPath)
        {
            // 先清理旧的 PLC 连接
            foreach (var smart in _smart200List)
            {
                try { smart.Disconnect(); } catch { }
                try { smart.Dispose(); } catch { }
            }
            _smart200List.Clear();

            foreach (var xinje in _xinjeList)
            {
                try { xinje.Disconnect(); } catch { }
                try { xinje.Dispose(); } catch { }
            }
            _xinjeList.Clear();

            foreach (var s71200 in _s71200List)
            {
                try { s71200.Disconnect(); } catch { }
                try { s71200.Dispose(); } catch { }
            }
            _s71200List.Clear();

            if (_config == null || _config.Plcs == null)
                return;

            foreach (var plc in _config.Plcs)
            {
                if (plc.Enabled != 1) continue;
                if (string.IsNullOrEmpty(plc.IP)) continue;

                // 解析该 PLC 需要加载的 CSV 点表（支持多文件）
                string[] csvPaths = GetPlcCsvPaths(plc, opcPath);
                if (csvPaths.Length == 0) continue;

                try
                {
                    if (plc.Type == PlcType.Smart200)
                    {
                        var smart = new Smart200(plc.IP);
                        if (!smart.IsConnected)
                        {
                            ShowMessage("Smart200 PLC 连接失败：" + plc.IP, "错误");
                            smart.Dispose();
                            continue;
                        }

                        smart.MappingPlcFromCSV(csvPaths[0]);

                        // 为 Smart200 点加 "PLC.S7." 前缀
                        if (smart.PlcStatusList != null)
                        {
                            foreach (var item in smart.PlcStatusList)
                            {
                                item.TagName = "PLC.S7." + item.TagName;
                            }
                        }

                        _smart200List.Add(smart);
                    }
                    else if (plc.Type == PlcType.Xinje)
                    {
                        var xinje = new XinjePlcCommunicator(plc.IP);
                        if (!xinje.IsConnected)
                        {
                            ShowMessage("信捷PLC 连接失败：" + plc.IP, "错误");
                            xinje.Dispose();
                            continue;
                        }

                        xinje.MappingPlcFromCSV(csvPaths[0]);
                        _xinjeList.Add(xinje);
                    }
                    else if (plc.Type == PlcType.S71200)
                    {
                        var s71200 = new S71200(plc.IP);
                        if (!s71200.IsConnected)
                        {
                            ShowMessage("S7-1200 PLC 连接失败：" + plc.IP, "错误");
                            s71200.Dispose();
                            continue;
                        }

                        s71200.MappingPlcFromCSV(csvPaths);

                        // 为 S7-1200 点加 "PLC.S1200." 前缀
                        if (s71200.PlcStatusList != null)
                        {
                            foreach (var item in s71200.PlcStatusList)
                            {
                                item.TagName = "PLC.S1200." + item.TagName;
                            }
                        }

                        _s71200List.Add(s71200);
                    }
                }
                catch (Exception ex)
                {
                    ShowMessage("PLC 加载失败：" + ex.Message, "错误");
                }
            }
        }

        /// <summary>解析 PLC 需要加载的 CSV 点表文件路径（支持多文件，缺失文件自动跳过）。</summary>
        private string[] GetPlcCsvPaths(PlcConfig plc, string opcPath)
        {
            var names = new List<string>();

            if (plc.CsvFiles != null)
            {
                foreach (var f in plc.CsvFiles)
                {
                    if (!string.IsNullOrWhiteSpace(f))
                        names.Add(f.Trim());
                }
            }

            if (names.Count == 0 && !string.IsNullOrWhiteSpace(plc.CsvFile))
                names.Add(plc.CsvFile.Trim());

            if (names.Count == 0)
            {
                names.Add(plc.Type switch
                {
                    PlcType.Xinje => "PLC_XDP.csv",
                    PlcType.S71200 => "PLC.S1200.AI.csv",
                    _ => "PLC.S7.csv"
                });
            }

            return names
                .Select(name => Path.Combine(opcPath, name))
                .Where(File.Exists)
                .ToArray();
        }

        /// <summary>加载并连接所有启用的串口 Modbus 设备。</summary>
        private void LoadSerialDevices(string opcPath)
        {
            // 先清理旧的串口设备连接
            foreach (var serial in _serialList)
            {
                try { serial.Disconnect(); } catch { }
                try { serial.Dispose(); } catch { }
            }
            _serialList.Clear();

            if (_config == null || _config.SerialDevices == null)
                return;

            // 一个串口设备条目 = 一个串口 + SlaveMap（文件 → 从站地址），与串口服务器一致
            var openedPorts = new HashSet<string>();

            foreach (var dev in _config.SerialDevices)
            {
                if (dev.Enabled != 1) continue;
                if (string.IsNullOrEmpty(dev.PortName)) continue;
                if (dev.SlaveMap == null || dev.SlaveMap.Count == 0) continue;

                // 同一串口只打开一次
                string portKey = dev.PortName.ToUpperInvariant();
                if (openedPorts.Contains(portKey)) continue;

                try
                {
                    var comm = new SerialModbusCommunicator(dev);
                    if (!comm.IsConnected)
                    {
                        ShowMessage("串口设备连接失败：" + dev.PortName, "错误");
                        comm.Dispose();
                        continue;
                    }

                    bool hasCsv = false;
                    foreach (var kv in dev.SlaveMap)
                    {
                        string csvPath = Path.Combine(opcPath, kv.Key);
                        if (!File.Exists(csvPath)) continue;

                        byte slaveId = kv.Value > 0 ? (byte)kv.Value : (byte)1;
                        comm.AddCsv(slaveId, csvPath);
                        hasCsv = true;
                    }

                    if (!hasCsv)
                    {
                        comm.Dispose();
                        continue;
                    }

                    comm.Start();
                    _serialList.Add(comm);
                    openedPorts.Add(portKey);
                }
                catch (Exception ex)
                {
                    ShowMessage("串口设备加载失败：" + ex.Message, "错误");
                }
            }
        }

        /// <summary>把 Smart200 的 PlcDataModel 转换为界面使用的 XdpPlcDataModel。</summary>
        private XdpPlcDataModel ConvertS7ToDisplay(PlcDataModel s7)
        {
            XdpDataValueType vt;
            switch (s7.ValueType)
            {
                case PlcDataValueType.Boolean:
                    vt = XdpDataValueType.Boolean;
                    break;
                case PlcDataValueType.Byte:
                case PlcDataValueType.Int:
                    vt = XdpDataValueType.Word;
                    break;
                case PlcDataValueType.DWord:
                    vt = XdpDataValueType.DWord;
                    break;
                case PlcDataValueType.Float:
                    vt = XdpDataValueType.Float;
                    break;
                default:
                    vt = XdpDataValueType.Float;
                    break;
            }

            return new XdpPlcDataModel
            {
                TagName = s7.TagName,
                PlcAddress = s7.PlcAddress,
                ValueType = vt,
                Description = s7.Description,
                Value = s7.Value,
                UpdateTime = s7.UpdateTime
            };
        }

        /// <summary>把 S7-1200 的 S71200DataModel 转换为界面使用的 XdpPlcDataModel。</summary>
        private XdpPlcDataModel ConvertS71200ToDisplay(S71200DataModel d)
        {
            XdpDataValueType vt;
            switch (d.ValueType)
            {
                case PlcDataValueType.Boolean:
                    vt = XdpDataValueType.Boolean;
                    break;
                case PlcDataValueType.Byte:
                case PlcDataValueType.Int:
                    vt = XdpDataValueType.Word;
                    break;
                case PlcDataValueType.DWord:
                    vt = XdpDataValueType.DWord;
                    break;
                case PlcDataValueType.Float:
                    vt = XdpDataValueType.Float;
                    break;
                default:
                    vt = XdpDataValueType.Float;
                    break;
            }

            return new XdpPlcDataModel
            {
                TagName = d.TagName,
                PlcAddress = d.PlcAddress,
                ValueType = vt,
                Description = d.Description,
                Value = d.Value,
                UpdateTime = d.UpdateTime
            };
        }

        /// <summary>清空并重新生成所有点卡片。</summary>
        private void GeneratePointCards(string[] csvFiles)
        {
            PointsPanel.Children.Clear();
            tagControls.Clear();

            // 串口服务器（XDP）点
            List<XdpPlcDataModel> dataList = ParseCsvForControls(csvFiles);

            foreach (var data in dataList)
            {
                PointsPanel.Children.Add(BuildPointCard(data));
            }

            // 追加 Smart200 (S7) 点，显示在 XDP 点后面
            foreach (var smart in _smart200List)
            {
                var s7List = smart.GetPlcStatusList();
                if (s7List != null)
                {
                    foreach (var s7 in s7List)
                    {
                        PointsPanel.Children.Add(BuildPointCard(ConvertS7ToDisplay(s7)));
                    }
                }
            }

            // 追加信捷 (XDP) 点，显示在 Smart200 点后面
            foreach (var xinje in _xinjeList)
            {
                var xinjeList = xinje.GetPlcStatusList();
                if (xinjeList != null)
                {
                    foreach (var x in xinjeList)
                    {
                        PointsPanel.Children.Add(BuildPointCard(x));
                    }
                }
            }

            // 追加 S7-1200 点，显示在信捷点后面
            foreach (var s71200 in _s71200List)
            {
                var s71200List = s71200.GetPlcStatusList();
                if (s71200List != null)
                {
                    foreach (var d in s71200List)
                    {
                        PointsPanel.Children.Add(BuildPointCard(ConvertS71200ToDisplay(d)));
                    }
                }
            }

            // 追加串口设备（如温湿度）点
            foreach (var serial in _serialList)
            {
                var serialList = serial.GetPlcStatusList();
                if (serialList != null)
                {
                    foreach (var d in serialList)
                    {
                        PointsPanel.Children.Add(BuildPointCard(d));
                    }
                }
            }
        }

        /// <summary>构建单个点的卡片控件（显示变量名、描述、地址、读取值和写入区）。</summary>
        private Control BuildPointCard(XdpPlcDataModel data)
        {
            var border = new Border
            {
                Width = 340,
                MinHeight = 130,
                Margin = new Thickness(5),
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#CCCCCC")),
                CornerRadius = new CornerRadius(6)
            };

            var stack = new StackPanel { Spacing = 4 };

            // 变量名
            stack.Children.Add(new TextBlock
            {
                Text = data.TagName,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            // 描述
            stack.Children.Add(new TextBlock
            {
                Text = data.Description ?? "",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });

            // 地址
            stack.Children.Add(new TextBlock
            {
                Text = "地址: " + data.PlcAddress,
                Foreground = Brushes.Gray
            });

            // 读取值行
            var readRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            readRow.Children.Add(new TextBlock { Text = "读取值:" });
            var readValue = new TextBlock { Text = "---" };
            readRow.Children.Add(readValue);
            stack.Children.Add(readRow);
            tagControls[data.TagName + "_read"] = readValue;

            // 写入行
            var writeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            writeRow.Children.Add(new TextBlock { Text = "写入:" });

            Control writeControl;
            if (data.ValueType == XdpDataValueType.Boolean)
            {
                var chk = new CheckBox { Content = "状态" };
                writeControl = chk;
                writeRow.Children.Add(chk);
            }
            else
            {
                var txt = new TextBox { Width = 150, Watermark = "输入值" };
                writeControl = txt;
                writeRow.Children.Add(txt);
            }

            var btnWrite = new Button { Content = "写入", Width = 60 };
            btnWrite.Click += (s, e) => WriteValue(data, writeControl);
            writeRow.Children.Add(btnWrite);

            stack.Children.Add(writeRow);

            border.Child = stack;
            return border;
        }

        /// <summary>解析 CSV 文件生成用于界面显示的点列表（不依赖通信器）。</summary>
        private List<XdpPlcDataModel> ParseCsvForControls(string[] csvFiles)
        {
            List<XdpPlcDataModel> result = new List<XdpPlcDataModel>();

            foreach (var filePath in csvFiles)
            {
                if (!File.Exists(filePath)) continue;
                string prefix = Path.GetFileNameWithoutExtension(filePath) + ".";

                using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8))
                {
                    reader.ReadLine(); // 跳过表头
                    string line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] parts = line.Split(',');
                        if (parts.Length < 3) continue;

                        string tagName = prefix + parts[0].Trim('"');
                        string address = parts[1].Trim('"');
                        string dataType = parts[2].Trim('"');
                        string description = parts.Length > 15 ? parts[15].Trim('"') : "";

                        XdpDataValueType valueType;
                        switch (dataType.ToLower())
                        {
                            case "boolean":
                            case "布尔":
                                valueType = XdpDataValueType.Boolean;
                                break;
                            case "word":
                            case "字":
                                valueType = XdpDataValueType.Word;
                                break;
                            case "dword":
                            case "双字":
                            case "long":
                            case "长整型":
                                valueType = XdpDataValueType.DWord;
                                break;
                            case "float":
                            case "浮点":
                                valueType = XdpDataValueType.Float;
                                break;
                            default:
                                valueType = XdpDataValueType.Float;
                                break;
                        }

                        result.Add(new XdpPlcDataModel
                        {
                            TagName = tagName,
                            PlcAddress = address,
                            ValueType = valueType,
                            Description = description
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>把界面输入的值写入对应设备。</summary>
        private void WriteValue(XdpPlcDataModel data, Control writeControl)
        {
            try
            {
                object value = null;

                if (data.ValueType == XdpDataValueType.Boolean)
                {
                    var chk = writeControl as CheckBox;
                    if (chk == null) return;
                    value = chk.IsChecked == true;
                }
                else
                {
                    var txt = writeControl as TextBox;
                    if (txt == null) return;

                    switch (data.ValueType)
                    {
                        case XdpDataValueType.Word:
                            ushort wordValue;
                            if (!ushort.TryParse(txt.Text, out wordValue))
                            {
                                ShowMessage("请输入有效的整数（0-65535）", "错误");
                                return;
                            }
                            value = wordValue;
                            break;
                        case XdpDataValueType.DWord:
                            uint dwordValue;
                            if (!uint.TryParse(txt.Text, out dwordValue))
                            {
                                ShowMessage("请输入有效的整数（0-4294967295）", "错误");
                                return;
                            }
                            value = dwordValue;
                            break;
                        case XdpDataValueType.Float:
                            float floatValue;
                            if (!float.TryParse(txt.Text, out floatValue))
                            {
                                ShowMessage("请输入有效的浮点数", "错误");
                                return;
                            }
                            value = floatValue;
                            break;
                        default:
                            ShowMessage("不支持的数据类型", "错误");
                            return;
                    }
                }

                // 优先通过串口服务器通信器写入
                var comm = GetCommunicatorByTag(data.TagName);
                if (comm != null && comm.IsConnected)
                {
                    comm.WriteDataToPlc(data.TagName, value);
                    ShowMessage("写入成功！");
                    return;
                }

                // 信捷 PLC 写入
                if (data.TagName != null && data.TagName.StartsWith("PLC_XDP.", StringComparison.OrdinalIgnoreCase))
                {
                    var xinje = _xinjeList.FirstOrDefault(x => x.IsConnected &&
                        x.GetPlcStatusList() != null && x.GetPlcStatusList().Any(d => d.TagName == data.TagName));
                    if (xinje != null)
                    {
                        xinje.WriteTag(data.TagName, value);
                        ShowMessage("写入成功！");
                        return;
                    }
                }

                // Smart200 PLC 写入
                if (data.TagName != null && data.TagName.StartsWith("PLC.S7.", StringComparison.OrdinalIgnoreCase))
                {
                    var smart = _smart200List.FirstOrDefault(s => s.IsConnected &&
                        s.GetPlcStatusList() != null && s.GetPlcStatusList().Any(d => d.TagName == data.TagName));
                    if (smart != null)
                    {
                        smart.WriteTag(data.TagName, value);
                        ShowMessage("写入成功！");
                        return;
                    }
                }

                // S7-1200 PLC 写入
                if (data.TagName != null && data.TagName.StartsWith("PLC.S1200.", StringComparison.OrdinalIgnoreCase))
                {
                    var s71200 = _s71200List.FirstOrDefault(s => s.IsConnected &&
                        s.GetPlcStatusList() != null && s.GetPlcStatusList().Any(d => d.TagName == data.TagName));
                    if (s71200 != null)
                    {
                        s71200.WriteTag(data.TagName, value);
                        ShowMessage("写入成功！");
                        return;
                    }
                }

                ShowMessage("未找到对应的连接", "错误");
            }
            catch (Exception ex)
            {
                ShowMessage("写入失败：" + ex.Message, "错误");
            }
        }

        /// <summary>启动 UI 刷新线程。</summary>
        private void StartUpdateThread()
        {
            if (updateThread != null && updateThread.IsAlive)
            {
                isUpdating = false;
                updateThread.Join(1000);
            }

            isUpdating = true;
            updateThread = new Thread(UpdateControls);
            updateThread.IsBackground = true;
            updateThread.Start();
        }

        /// <summary>UI 刷新线程主体，定时把最新数据投递到 UI 线程。</summary>
        private void UpdateControls()
        {
            while (isUpdating)
            {
                try
                {
                    if (IsAnyConnected)
                    {
                        List<XdpPlcDataModel> dataList = GetAllStatusList();
                        Dispatcher.UIThread.Post(() => UpdateControlValues(dataList));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("更新控件失败：" + ex.Message);
                }

                Thread.Sleep(100);
            }
        }

        /// <summary>把最新数据更新到界面读取值控件。</summary>
        private void UpdateControlValues(List<XdpPlcDataModel> dataList)
        {
            foreach (var data in dataList)
            {
                string readKey = data.TagName + "_read";
                if (tagControls.TryGetValue(readKey, out var ctrl))
                {
                    if (data.Value == null)
                    {
                        ctrl.Text = "---";
                    }
                    else if (data.ValueType == XdpDataValueType.Boolean)
                    {
                        ctrl.Text = (bool)data.Value ? "True" : "False";
                    }
                    else
                    {
                        ctrl.Text = data.Value.ToString();
                    }
                }
            }
        }

        /// <summary>关闭窗口时清理资源。</summary>
        private void Cleanup()
        {
            isUpdating = false;
            if (updateThread != null && updateThread.IsAlive)
            {
                updateThread.Join(1000);
            }
            DisconnectAllServers();
        }
    }
}
