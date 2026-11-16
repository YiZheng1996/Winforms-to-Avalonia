using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PressureTestApp.Communication.Plc;
using System;
using System.Collections.Generic;
using System.IO;

namespace ModbusAvalonia
{
    /// <summary>串口服务器编辑窗口，用于编辑 IP/端口/启停，以及 SlaveMap（CSV 文件 → 485 从站地址）。</summary>
    public partial class ServerEditWindow : Window
    {
        /// <summary>临时保存的“文件 → 站号”映射列表。</summary>
        private List<KeyValuePair<string, int>> _map = new List<KeyValuePair<string, int>>();

        /// <summary>构造函数。传入 null 表示新增；传入已有配置表示编辑。</summary>
        public ServerEditWindow(XdpServerConfig server)
        {
            InitializeComponent();

            txtIP.Text = server?.IP ?? "";
            txtPort.Text = (server != null && server.Port > 0 ? server.Port : 10001).ToString();
            chkEnabled.IsChecked = server != null && server.Enabled == 1;

            if (server != null && server.SlaveMap != null)
            {
                foreach (var kv in server.SlaveMap)
                {
                    _map.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
                }
            }

            RefreshMap();
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

        /// <summary>确定：校验输入并返回编辑好的服务器配置。</summary>
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            string ip = (txtIP.Text ?? "").Trim();
            if (string.IsNullOrEmpty(ip))
            {
                DialogHelper.ShowMessage(this, "请输入 IP", "提示");
                return;
            }

            int port;
            if (!int.TryParse(txtPort.Text, out port) || port <= 0) port = 10001;

            var server = new XdpServerConfig
            {
                IP = ip,
                Port = port,
                Enabled = chkEnabled.IsChecked == true ? 1 : 0,
                SlaveMap = new Dictionary<string, int>()
            };

            foreach (var kv in _map)
            {
                server.SlaveMap[kv.Key] = kv.Value;
            }

            Close(server);
        }

        /// <summary>取消：返回 null。</summary>
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
