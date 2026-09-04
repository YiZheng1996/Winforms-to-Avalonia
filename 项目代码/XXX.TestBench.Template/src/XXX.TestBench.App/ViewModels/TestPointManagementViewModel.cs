using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Domain;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 试验项点管理标签页：在产品类型下维护项点名称、关联逻辑类、判定类型、启用与排序。
/// 新增/编辑弹窗由对应 View 代码后置打开，本 VM 只负责服务调用与列表刷新。
/// </summary>
public sealed partial class TestPointManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public TestPointManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "试验项点";

    /// <summary>
    /// 可筛选的产品类型列表。
    /// </summary>
    public ObservableCollection<ProductType> TypeOptions { get; } = new();

    /// <summary>
    /// 当前产品类型下的试验项点列表。
    /// </summary>
    public ObservableCollection<TestItemPoint> Points { get; } = new();

    /// <summary>
    /// 已注册的关联逻辑类（执行器代码），供新增/编辑弹窗选择。
    /// </summary>
    public IReadOnlyCollection<string> ExecutorCodes => _services.Executors.Codes;

    /// <summary>
    /// 当前筛选的产品类型。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedType;

    partial void OnSelectedTypeChanged(ProductType? value) => _ = LoadPointsAsync();

    /// <summary>
    /// 当前选中的试验项点。
    /// </summary>
    [ObservableProperty]
    private TestItemPoint? _selectedPoint;

    /// <summary>
    /// 当前类型下是否有试验项点。
    /// </summary>
    public bool HasPoints => Points.Count > 0;

    /// <summary>
    /// 加载类型选项与当前类型的项点列表。
    /// </summary>
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await LoadTypesAsync(ct);
            await LoadPointsAsync(ct);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadTypesAsync(CancellationToken ct)
    {
        var types = await _services.ProductRepository.ListTypesAsync(includeDisabled: false, ct);
        var selectedId = SelectedType?.Id;
        TypeOptions.Clear();
        foreach (var type in types) TypeOptions.Add(type);
        if (selectedId is not null)
            SelectedType = TypeOptions.FirstOrDefault(t => t.Id == selectedId);
        else if (SelectedType is null && TypeOptions.Count > 0)
            SelectedType = TypeOptions[0];
    }

    private async Task LoadPointsAsync(CancellationToken ct = default)
    {
        Points.Clear();
        OnPropertyChanged(nameof(HasPoints));
        if (SelectedType is null) return;
        var points = await _services.TestPoints.ListPointsAsync(SelectedType.Id, includeDisabled: true, ct);
        foreach (var point in points) Points.Add(point);
        OnPropertyChanged(nameof(HasPoints));
    }

    /// <summary>
    /// 新增弹窗提交后创建试验项点。
    /// </summary>
    public async Task CreateFromDialogAsync(TestPointDialogResult result)
    {
        StatusMessage = string.Empty;
        try
        {
            if (SelectedType is null) throw new Core.Common.DomainException("请先选择产品类型");
            await _services.TestPoints.CreatePointAsync(_actor, SelectedType.Id, result.Name, result.ExecutorCode, result.ResultKind, result.SortOrder);
            StatusMessage = "试验项点已创建";
            await LoadPointsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 编辑弹窗提交后更新试验项点。
    /// </summary>
    public async Task UpdateFromDialogAsync(int pointId, TestPointDialogResult result)
    {
        StatusMessage = string.Empty;
        try
        {
            await _services.TestPoints.UpdatePointAsync(_actor, pointId, result.Name, result.ExecutorCode, result.ResultKind, result.IsEnabled, result.SortOrder);
            StatusMessage = "试验项点已更新";
            await LoadPointsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 启用/停用选中项点。
    /// </summary>
    [RelayCommand]
    public async Task TogglePointAsync()
    {
        StatusMessage = string.Empty;
        if (SelectedPoint is null) { StatusMessage = "请先选择试验项点"; return; }
        try
        {
            var p = SelectedPoint;
            await _services.TestPoints.UpdatePointAsync(_actor, p.Id, p.Name, p.ExecutorCode, p.ResultKind, !p.IsEnabled, p.SortOrder);
            StatusMessage = p.IsEnabled ? "试验项点已停用" : "试验项点已启用";
            await LoadPointsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 删除选中项点（被型号配置或记录引用的项点由服务拒绝）。
    /// </summary>
    [RelayCommand]
    public async Task DeletePointAsync()
    {
        StatusMessage = string.Empty;
        if (SelectedPoint is null) { StatusMessage = "请先选择试验项点"; return; }
        try
        {
            await _services.TestPoints.DeletePointAsync(_actor, SelectedPoint.Id);
            StatusMessage = "试验项点已删除";
            await LoadPointsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
