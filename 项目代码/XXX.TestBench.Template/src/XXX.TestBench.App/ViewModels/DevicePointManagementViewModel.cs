using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位表格行：页面只展示配置副本，不直接修改运行时点位。
/// </summary>
public sealed class DevicePointRow
{
    /// <summary>
    /// 点位配置副本。
    /// </summary>
    public required PointsConfig.PointEntry Entry { get; init; }
    /// <summary>
    /// 点位编码。
    /// </summary>
    public string Code => Entry.Code;
    /// <summary>
    /// 点位名称。
    /// </summary>
    public string Name => Entry.Name;
    /// <summary>
    /// 协议。
    /// </summary>
    public string Protocol => Entry.Protocol;
    /// <summary>
    /// 地址。
    /// </summary>
    public string Address => Entry.Address;
    /// <summary>
    /// 数据类型。
    /// </summary>
    public string DataType => Entry.DataType;
    /// <summary>
    /// 单位。
    /// </summary>
    public string Unit => Entry.Unit;
    /// <summary>
    /// 量程显示文字。
    /// </summary>
    public string ScaleText
    {
        get
        {
            var values = new[] { Entry.EffectiveRawMin, Entry.EffectiveRawMax, Entry.EffectiveEngMin, Entry.EffectiveEngMax };
            return values.Any(value => value.HasValue)
                ? $"{values[0]}~{values[1]} → {values[2]}~{values[3]}"
                : "未配置";
        }
    }
    /// <summary>
    /// 可写状态显示文字。
    /// </summary>
    public string WritableText => Entry.IsWritable ? "可写" : "只读";
    /// <summary>
    /// 风险等级显示文字。
    /// </summary>
    public string RiskText => Entry.RiskLevel == WriteRiskLevel.HighRisk ? "高风险" : "普通";
    /// <summary>
    /// 启用状态显示文字。
    /// </summary>
    public string EnabledText => Entry.IsEnabled ? "启用" : "停用";
}

/// <summary>
/// 设备点位管理：支持模板下载、手工新增/编辑/删除，以及按固定中文格式校验后的 Excel/CSV 整体导入。
/// </summary>
public sealed partial class DevicePointManagementViewModel : PageViewModel
{
    /// <summary>
    /// 组合根注入的服务。
    /// </summary>
    private readonly ShellServices _services;
    /// <summary>
    /// 当前操作者。
    /// </summary>
    private readonly UserContext _actor;

    /// <summary>
    /// 创建设备点位管理视图模型。
    /// </summary>
    public DevicePointManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    /// <summary>
    /// 页面标题。
    /// </summary>
    public override string Title => "设备点位";
    /// <summary>
    /// 页面展示的点位行集合。
    /// </summary>
    public ObservableCollection<DevicePointRow> Points { get; } = new();
    /// <summary>
    /// 当前用户是否有设备管理权限。
    /// </summary>
    public bool CanEdit => _actor.HasPermission(PermissionCode.ManageDevices);
    /// <summary>
    /// 是否已有点位数据。
    /// </summary>
    public bool HasPoints => Points.Count > 0;
    /// <summary>
    /// 点位来源说明文字。
    /// </summary>
    public string SourceText => $"来源：points.json；导入格式：{DevicePointTemplateDefinition.DefaultFileName}";

    [ObservableProperty]
    /// <summary>
    /// 当前选中的点位行。
    /// </summary>
    private DevicePointRow? _selectedPoint;

    /// <summary>
    /// 加载点位目录并刷新列表。
    /// </summary>
    public override Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Points.Clear();
            foreach (var point in _services.DevicePoints.List())
                Points.Add(new DevicePointRow { Entry = point });
            OnPropertyChanged(nameof(HasPoints));
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存新增的点位。
    /// </summary>
    public async Task CreateFromDialogAsync(DevicePointDialogResult result)
    {
        var entries = Points.Select(row => row.Entry).ToList();
        entries.Add(result.ToEntry());
        await SaveEntriesAsync(entries, "设备点位已新增");
    }

    /// <summary>
    /// 保存对选中点位的修改。
    /// </summary>
    public async Task UpdateFromDialogAsync(DevicePointRow row, DevicePointDialogResult result)
    {
        var entries = Points.Select(item => item.Entry).ToList();
        var index = Points.IndexOf(row);
        if (index < 0) { StatusMessage = "选中的点位已不存在"; return; }
        entries[index] = result.ToEntry();
        await SaveEntriesAsync(entries, "设备点位已更新");
    }

    /// <summary>
    /// 导入点位文件并整体保存。
    /// </summary>
    public async Task ImportAsync(string filePath)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _services.DevicePointImporter.ImportAsync(filePath);
            if (!result.IsValid)
            {
                StatusMessage = FormatImportIssues(result);
                return;
            }

            await SaveEntriesAsync(result.Points, $"已导入 {result.Points.Count} 个设备点位");
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 下载设备点位导入模板。
    /// </summary>
    public async Task DownloadTemplateAsync(string filePath)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            await _services.DevicePointTemplateExporter.ExportAsync(filePath);
            StatusMessage = $"设备点位导入模板已保存：{filePath}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 删除当前选中的点位。
    /// </summary>
    [RelayCommand]
    public async Task DeletePointAsync()
    {
        if (SelectedPoint is null) { StatusMessage = "请先选择设备点位"; return; }
        var entries = Points.Where(row => !ReferenceEquals(row, SelectedPoint)).Select(row => row.Entry).ToList();
        await SaveEntriesAsync(entries, "设备点位已删除");
    }

    /// <summary>
    /// 保存点位列表并重载设备运行时。
    /// </summary>
    private async Task SaveEntriesAsync(IReadOnlyList<PointsConfig.PointEntry> entries, string successText)
    {
        StatusMessage = string.Empty;
        try
        {
            if (await _services.RecordRepository.GetActiveRunningRecordAsync() is not null)
                throw new Core.Common.DomainException("存在活动试验，禁止修改设备点位；请先结束试验");

            await _services.DevicePoints.SaveAsync(_actor, entries);
            var reload = await _services.DeviceModes.InitializeAsync(_services.DeviceModes.CurrentMode);
            await LoadAsync();
            StatusMessage = reload.Ok
                ? $"{successText}，设备运行时已重新加载"
                : $"{successText}，但运行时重载失败：{reload.Error}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 把导入问题整理为提示文字。
    /// </summary>
    private static string FormatImportIssues(XXX.TestBench.Core.Ports.DevicePointImportResult result)
    {
        var displayed = result.Issues.Take(8).Select(issue => issue.RowNumber > 0
            ? $"第 {issue.RowNumber} 行：{issue.Message}"
            : issue.Message);
        var suffix = result.Issues.Count > 8 ? $"；另有 {result.Issues.Count - 8} 个错误" : string.Empty;
        return "导入未保存：" + string.Join("；", displayed) + suffix;
    }
}
