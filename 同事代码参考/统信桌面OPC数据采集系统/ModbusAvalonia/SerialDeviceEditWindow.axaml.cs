using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PressureTestApp.Communication.Plc;
using System;
using System.Collections.Generic;
using System.IO;

namespace ModbusAvalonia
{
    /// <summary>串口 Modbus 设备编辑窗口，用于编辑串口参数、启停，以及 SlaveMap（CSV 文件 → 485 从站地址）。</summary>
    public partial class SerialDeviceEditWindow : Window
    {
        /// <summary>临时保存的“文件 → 站号”映射列表。</summary>
        private List<KeyValuePair<string, int>> _map = new List<KeyValuePair<string, int>>();

        /// <summary>构造函数。传入 null 表示新增；传入已有配置表示编辑。</summary>
        public SerialDeviceEditWindow(SerialDeviceConfig cfg)
        {
            InitializeComponent();

            if (cfg != null)
            {
                txtPortName.Text = cfg.PortName ?? "";
                txtBaudRate.Text = (cfg.BaudRate > 0 ? cfg.BaudRate : 9600).ToString();
                txtDataBits.Text = (cfg.DataBits > 0 ? cfg.DataBits : 8).ToString();
                chkEnabled.IsChecked = cfg.Enabled == 1;
                cmbParity.SelectedIndex = ParityIndex(cfg.Parity);
                cmbStopBits.SelectedIndex = (cfg.StopBits == "Two" || cfg.StopBits == "2") ? 1 : 0;

                if (cfg.SlaveMap != null)
                {
                    foreach (var kv in cfg.SlaveMap)
                    {
                        _map.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
                    }
                }
            }
            else
            {
                txtBaudRate.Text = "9600";
                txtDataBits.Text = "8";
                chkEnabled.IsChecked = true;
            }

            RefreshMap();
        }

        /// <summary>把校验位字符串转换为下拉框索引。</summary>
        private static int ParityIndex(string parity)
        {
            switch ((parity ?? "").ToLower())
            {
                case "odd": return 1;
                case "even": return 2;
                default: return 0;
            }
        }

        /// <summary>刷新 SlaveMap 列表显示（带序号）。</summary>
        private void RefreshMap()
        {
            lstMap.Items.Clear();
            for (int i = 0; i < _map.Count; i++)
            {
                lstMap.Items.Add((i + 1) + ". " + _map[i].Key + " = " + _map[i].Value);
            }
        }

        /// <summary>从 OPC 目录浏览并选择 CSV 文件。</summary>
        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var options = new FilePickerOpenOptions
            {
                Title = "选择 OPC 目录下的 CSV 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("CSV 文件") { Patterns = new[] { "*.csv" } }
                }
            };

            string opcPath = Path.Combine(AppContext.BaseDirectory, "OPC");
            if (Directory.Exists(opcPath))
            {
                var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(opcPath);
                if (folder != null)
                    options.SuggestedStartLocation = folder;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (files == null || files.Count == 0) return;

            txtFile.Text = files[0].Name;
        }

        /// <summary>添加一个“文件 → 站号”映射。</summary>
        private void btnAddFile_Click(object sender, RoutedEventArgs e)
        {
            string file = (txtFile.Text ?? "").Trim();
            if (string.IsNullOrEmpty(file))
            {
                DialogHelper.ShowMessage(this, "请输入文件名", "提示");
                return;
            }

            int slave;
            if (!int.TryParse(txtSlave.Text, out slave) || slave <= 0) slave = 1;

            _map.Add(new KeyValuePair<string, int>(file, slave));
            RefreshMap();
            txtFile.Text = "";
            txtSlave.Text = "1";
        }

        /// <summary>删除选中的映射项。</summary>
        private void btnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            int index = lstMap.SelectedIndex;
            if (index >= 0 && index < _map.Count)
            {
                _map.RemoveAt(index);
                RefreshMap();
            }
        }

        /// <summary>确定：校验输入并返回编辑好的串口设备配置。</summary>
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            string portName = (txtPortName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(portName))
            {
                DialogHelper.ShowMessage(this, "请输入串口号", "提示");
                return;
            }

            if (!int.TryParse(txtBaudRate.Text, out int baudRate) || baudRate <= 0)
            {
                DialogHelper.ShowMessage(this, "波特率无效", "提示");
                return;
            }

            if (!int.TryParse(txtDataBits.Text, out int dataBits) || dataBits <= 0)
            {
                DialogHelper.ShowMessage(this, "数据位无效", "提示");
                return;
            }

            string[] parityOptions = { "None", "Odd", "Even" };
            string parity = parityOptions[cmbParity.SelectedIndex < 0 ? 0 : cmbParity.SelectedIndex];
            string stopBits = cmbStopBits.SelectedIndex == 1 ? "Two" : "One";

            var cfg = new SerialDeviceConfig
            {
                PortName = portName,
                BaudRate = baudRate,
                Parity = parity,
                DataBits = dataBits,
                StopBits = stopBits,
                Enabled = chkEnabled.IsChecked == true ? 1 : 0,
                SlaveMap = new Dictionary<string, int>()
            };

            foreach (var kv in _map)
            {
                cfg.SlaveMap[kv.Key] = kv.Value;
            }

            Close(cfg);
        }

        /// <summary>取消：返回 null。</summary>
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
