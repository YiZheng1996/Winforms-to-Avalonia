using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PressureTestApp.Communication.Plc;
using System;
using System.Collections.Generic;
using System.IO;

namespace ModbusAvalonia
{
    /// <summary>PLC 编辑结果，用于从编辑窗口回传配置。</summary>
    public class PlcEditResult
    {
        public PlcType Type { get; set; }
        public string IP { get; set; }
        public int Enabled { get; set; }
        public List<string> CsvFiles { get; set; }
    }

    /// <summary>PLC（Smart200 / 信捷 / S7-1200）编辑窗口。</summary>
    public partial class PlcEditWindow : Window
    {
        /// <summary>临时保存的 CSV 点表文件列表。</summary>
        private List<string> _csvFiles = new List<string>();

        /// <summary>构造函数，初始化各输入控件的当前值。</summary>
        public PlcEditWindow(string title, PlcType type, string ip, int enabled, List<string> csvFiles)
        {
            InitializeComponent();
            Title = title;
            cmbType.SelectedIndex = type == PlcType.Xinje ? 1 : type == PlcType.S71200 ? 2 : 0;
            txtIP.Text = ip ?? "";
            chkEnabled.IsChecked = enabled == 1;

            if (csvFiles != null)
                _csvFiles.AddRange(csvFiles);

            RefreshCsvFiles();
        }

        /// <summary>刷新 CSV 文件列表显示（带序号）。</summary>
        private void RefreshCsvFiles()
        {
            lstCsvFiles.Items.Clear();
            for (int i = 0; i < _csvFiles.Count; i++)
            {
                lstCsvFiles.Items.Add((i + 1) + ". " + _csvFiles[i]);
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
                AllowMultiple = true,
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

            foreach (var file in files)
            {
                string name = file.Name;
                if (!string.IsNullOrWhiteSpace(name) && !_csvFiles.Contains(name))
                    _csvFiles.Add(name);
            }

            RefreshCsvFiles();
        }

        /// <summary>添加一个 CSV 文件到列表。</summary>
        private void btnAddFile_Click(object sender, RoutedEventArgs e)
        {
            string file = (txtCsvFile.Text ?? "").Trim();
            if (string.IsNullOrEmpty(file))
            {
                DialogHelper.ShowMessage(this, "请输入文件名", "提示");
                return;
            }

            if (!_csvFiles.Contains(file))
                _csvFiles.Add(file);

            RefreshCsvFiles();
            txtCsvFile.Text = "";
        }

        /// <summary>删除选中的 CSV 文件。</summary>
        private void btnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            int index = lstCsvFiles.SelectedIndex;
            if (index >= 0 && index < _csvFiles.Count)
            {
                _csvFiles.RemoveAt(index);
                RefreshCsvFiles();
            }
        }

        /// <summary>确定：校验输入并返回编辑结果。</summary>
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            string ip = (txtIP.Text ?? "").Trim();
            if (string.IsNullOrEmpty(ip))
            {
                DialogHelper.ShowMessage(this, "请输入 IP", "提示");
                return;
            }

            Close(new PlcEditResult
            {
                Type = cmbType.SelectedIndex switch
                {
                    1 => PlcType.Xinje,
                    2 => PlcType.S71200,
                    _ => PlcType.Smart200
                },
                IP = ip,
                Enabled = chkEnabled.IsChecked == true ? 1 : 0,
                CsvFiles = new List<string>(_csvFiles)
            });
        }

        /// <summary>取消：返回 null。</summary>
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
