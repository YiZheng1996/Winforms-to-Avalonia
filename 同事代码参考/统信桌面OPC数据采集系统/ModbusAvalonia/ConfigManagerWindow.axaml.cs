using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PressureTestApp.Communication.Plc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModbusAvalonia
{
    /// <summary>配置管理窗口，统一管理串口服务器、PLC、串口设备三类配置。</summary>
    public partial class ConfigManagerWindow : Window
    {
        /// <summary>OPCConfig.json 配置文件的完整路径。</summary>
        private string configPath;

        /// <summary>当前加载的配置对象。</summary>
        private XdpConfig config;

        /// <summary>串口服务器列表（工作副本，保存时写回 config）。</summary>
        private List<XdpServerConfig> servers;

        /// <summary>PLC 列表（Smart200 / 信捷，工作副本）。</summary>
        private List<PlcConfig> plcs;

        /// <summary>串口设备列表（串口 Modbus RTU，工作副本）。</summary>
        private List<SerialDeviceConfig> serialDevices;

        /// <summary>构造函数：加载 OPCConfig.json 并刷新三个页签的列表。</summary>
        public ConfigManagerWindow()
        {
            InitializeComponent();
            configPath = Path.Combine(AppContext.BaseDirectory, "OPC", "OPCConfig.json");
            config = XdpConfigLoader.Load(configPath);
            servers = config.Servers ?? new List<XdpServerConfig>();
            plcs = config.Plcs ?? new List<PlcConfig>();
            serialDevices = config.SerialDevices ?? new List<SerialDeviceConfig>();
            RefreshServers();
            RefreshPlcs();
            RefreshSerialDevices();
        }

        /// <summary>刷新串口服务器列表显示。</summary>
        private void RefreshServers()
        {
            lstServers.Items.Clear();
            foreach (var s in servers)
            {
                int count = s.SlaveMap != null ? s.SlaveMap.Count : 0;
                lstServers.Items.Add(s.IP + ":" + s.Port + "   启用=" + s.Enabled + "   文件数=" + count);
            }
        }

        /// <summary>刷新 PLC 列表显示。</summary>
        private void RefreshPlcs()
        {
            lstPlcs.Items.Clear();
            foreach (var p in plcs)
            {
                int count = p.CsvFiles != null ? p.CsvFiles.Count : 0;
                if (count == 0 && !string.IsNullOrWhiteSpace(p.CsvFile))
                    count = 1;
                lstPlcs.Items.Add(p.Type + "   " + p.IP + "   启用=" + p.Enabled + "   文件数=" + count);
            }
        }

        /// <summary>刷新串口设备列表显示。</summary>
        private void RefreshSerialDevices()
        {
            lstSerialDevices.Items.Clear();
            foreach (var s in serialDevices)
            {
                int count = s.SlaveMap != null ? s.SlaveMap.Count : 0;
                lstSerialDevices.Items.Add(s.PortName + "   " + s.BaudRate + "," + s.Parity + "," + s.DataBits + "," + s.StopBits + "   启用=" + s.Enabled + "   文件数=" + count);
            }
        }

        /// <summary>新增串口服务器。</summary>
        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ServerEditWindow(null);
            _ = dlg.ShowDialog<XdpServerConfig>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    servers.Add(t.Result);
                    Dispatcher.UIThread.Post(RefreshServers);
                }
            });
        }

        /// <summary>编辑选中的串口服务器。</summary>
        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            int index = lstServers.SelectedIndex;
            if (index < 0 || index >= servers.Count) return;

            var dlg = new ServerEditWindow(servers[index]);
            _ = dlg.ShowDialog<XdpServerConfig>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    servers[index] = t.Result;
                    Dispatcher.UIThread.Post(RefreshServers);
                }
            });
        }

        /// <summary>删除选中的串口服务器。</summary>
        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            int index = lstServers.SelectedIndex;
            if (index < 0 || index >= servers.Count) return;

            DialogHelper.ShowConfirm(this, "确定删除该服务器配置吗？", result =>
            {
                if (result)
                {
                    servers.RemoveAt(index);
                    Dispatcher.UIThread.Post(RefreshServers);
                }
            });
        }

        /// <summary>新增 PLC。</summary>
        private void btnAddPlc_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PlcEditWindow("添加 PLC", PlcType.Smart200, "", 1, new List<string>());
            _ = dlg.ShowDialog<PlcEditResult>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    plcs.Add(new PlcConfig
                    {
                        Type = t.Result.Type,
                        IP = t.Result.IP,
                        Enabled = t.Result.Enabled,
                        CsvFiles = t.Result.CsvFiles
                    });
                    Dispatcher.UIThread.Post(RefreshPlcs);
                }
            });
        }

        /// <summary>编辑选中的 PLC。</summary>
        private void btnEditPlc_Click(object sender, RoutedEventArgs e)
        {
            int index = lstPlcs.SelectedIndex;
            if (index < 0 || index >= plcs.Count) return;

            var p = plcs[index];
            var dlg = new PlcEditWindow("编辑 PLC", p.Type, p.IP, p.Enabled, p.CsvFiles);
            _ = dlg.ShowDialog<PlcEditResult>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    plcs[index] = new PlcConfig
                    {
                        Type = t.Result.Type,
                        IP = t.Result.IP,
                        Enabled = t.Result.Enabled,
                        CsvFiles = t.Result.CsvFiles
                    };
                    Dispatcher.UIThread.Post(RefreshPlcs);
                }
            });
        }

        /// <summary>删除选中的 PLC。</summary>
        private void btnDeletePlc_Click(object sender, RoutedEventArgs e)
        {
            int index = lstPlcs.SelectedIndex;
            if (index < 0 || index >= plcs.Count) return;

            DialogHelper.ShowConfirm(this, "确定删除该 PLC 配置吗？", result =>
            {
                if (result)
                {
                    plcs.RemoveAt(index);
                    Dispatcher.UIThread.Post(RefreshPlcs);
                }
            });
        }

        /// <summary>新增串口设备。</summary>
        private void btnAddSerial_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SerialDeviceEditWindow(null);
            _ = dlg.ShowDialog<SerialDeviceConfig>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    serialDevices.Add(t.Result);
                    Dispatcher.UIThread.Post(RefreshSerialDevices);
                }
            });
        }

        /// <summary>编辑选中的串口设备。</summary>
        private void btnEditSerial_Click(object sender, RoutedEventArgs e)
        {
            int index = lstSerialDevices.SelectedIndex;
            if (index < 0 || index >= serialDevices.Count) return;

            var dlg = new SerialDeviceEditWindow(serialDevices[index]);
            _ = dlg.ShowDialog<SerialDeviceConfig>(this).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    serialDevices[index] = t.Result;
                    Dispatcher.UIThread.Post(RefreshSerialDevices);
                }
            });
        }

        /// <summary>删除选中的串口设备。</summary>
        private void btnDeleteSerial_Click(object sender, RoutedEventArgs e)
        {
            int index = lstSerialDevices.SelectedIndex;
            if (index < 0 || index >= serialDevices.Count) return;

            DialogHelper.ShowConfirm(this, "确定删除该串口设备配置吗？", result =>
            {
                if (result)
                {
                    serialDevices.RemoveAt(index);
                    Dispatcher.UIThread.Post(RefreshSerialDevices);
                }
            });
        }

        /// <summary>保存：合并服务器、写回三类列表并序列化到 OPCConfig.json。</summary>
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                config.Servers = MergeServers(servers);
                servers = config.Servers;

                config.Plcs = plcs;
                config.SerialDevices = serialDevices;
                config.Smart200 = null;
                config.Xinje = null;

                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter());
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, json, Encoding.UTF8);

                RefreshServers();
                RefreshPlcs();
                RefreshSerialDevices();
                DialogHelper.ShowMessage(this, "保存成功！\n" + configPath);
            }
            catch (Exception ex)
            {
                DialogHelper.ShowMessage(this, "保存失败：" + ex.Message, "错误");
            }
        }

        /// <summary>关闭窗口。</summary>
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>合并相同 IP/端口的服务器配置，避免重复连接同一个串口服务器。</summary>
        private List<XdpServerConfig> MergeServers(List<XdpServerConfig> list)
        {
            var merged = new List<XdpServerConfig>();
            foreach (var s in list)
            {
                int port = s.Port > 0 ? s.Port : 10001;
                var existing = merged.FirstOrDefault(x => x.IP == s.IP && x.Port == port);
                if (existing == null)
                {
                    existing = new XdpServerConfig
                    {
                        IP = s.IP,
                        Port = port,
                        Enabled = s.Enabled,
                        SlaveMap = new Dictionary<string, int>()
                    };
                    merged.Add(existing);
                }
                else if (s.Enabled == 1)
                {
                    existing.Enabled = 1;
                }

                if (s.SlaveMap != null)
                {
                    foreach (var kv in s.SlaveMap)
                    {
                        existing.SlaveMap[kv.Key] = kv.Value;
                    }
                }
            }
            return merged;
        }
    }
}
