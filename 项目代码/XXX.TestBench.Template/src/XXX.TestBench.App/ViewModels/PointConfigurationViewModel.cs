using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 项点配置标签页：为产品型号从所属产品类型的启用项点中选择并按顺序编排自动试验序列。
/// 左侧为可选项点，右侧为已配置序列；支持添加、移除、上移、下移与整体保存。
/// </summary>
public sealed partial class PointConfigurationViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public PointConfigurationViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "项点配置";

    /// <summary>
    /// 产品类型选项。
    /// </summary>
    public ObservableCollection<ProductType> TypeOptions { get; } = new();

    /// <summary>
    /// 产品型号选项。
    /// </summary>
    public ObservableCollection<ProductModel> ModelOptions { get; } = new();

    /// <summary>
    /// 左侧：类型下可选的启用项点。
    /// </summary>
    public ObservableCollection<TestItemPoint> AvailablePoints { get; } = new();

    /// <summary>
    /// 右侧：型号已配置的项点序列。
    /// </summary>
    public ObservableCollection<TestItemPoint> ConfiguredPoints { get; } = new();

    /// <summary>
    /// 当前选中的产品类型。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedType;

    partial void OnSelectedTypeChanged(ProductType? value) => _ = LoadModelsAsync();

    /// <summary>
    /// 当前选中的产品型号。
    /// </summary>
    [ObservableProperty]
    private ProductModel? _selectedModel;

    partial void OnSelectedModelChanged(ProductModel? value) => _ = LoadConfigurationAsync();

    /// <summary>
    /// 左侧选中的可选项点。
    /// </summary>
    [ObservableProperty]
    private TestItemPoint? _selectedAvailable;

    /// <summary>
    /// 右侧选中的已配置项点。
    /// </summary>
    [ObservableProperty]
    private TestItemPoint? _selectedConfigured;

    /// <summary>
    /// 是否存在已配置项点。
    /// </summary>
    public bool HasConfigured => ConfiguredPoints.Count > 0;

    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await LoadTypesAsync(ct);
            await LoadModelsAsync(ct);
            await LoadConfigurationAsync(ct);
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

    private async Task LoadModelsAsync(CancellationToken ct = default)
    {
        var selectedId = SelectedModel?.Id;
        ModelOptions.Clear();
        if (SelectedType is not null)
        {
            var models = await _services.ProductRepository.ListModelsAsync(SelectedType.Id, includeDisabled: false, ct);
            foreach (var model in models) ModelOptions.Add(model);
        }
        if (selectedId is not null)
            SelectedModel = ModelOptions.FirstOrDefault(m => m.Id == selectedId);
        else if (SelectedModel is null && ModelOptions.Count > 0)
            SelectedModel = ModelOptions[0];
    }

    private async Task LoadConfigurationAsync(CancellationToken ct = default)
    {
        AvailablePoints.Clear();
        ConfiguredPoints.Clear();
        OnPropertyChanged(nameof(HasConfigured));
        if (SelectedModel is null || SelectedType is null) return;

        var configured = await _services.TestPoints.ListConfiguredPointsAsync(SelectedModel.Id, ct);
        var all = await _services.TestPoints.ListPointsAsync(SelectedType.Id, includeDisabled: false, ct);
        var configuredIds = configured.Select(p => p.Id).ToHashSet();
        foreach (var point in configured) ConfiguredPoints.Add(point);
        foreach (var point in all.Where(p => !configuredIds.Contains(p.Id)).OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
            AvailablePoints.Add(point);
        OnPropertyChanged(nameof(HasConfigured));
    }

    /// <summary>
    /// 把左侧选中项点添加到右侧序列末尾。
    /// </summary>
    [RelayCommand]
    public Task AddSelectedAsync() => MovePointAsync(SelectedAvailable, fromAvailable: true, down: false);

    /// <summary>
    /// 把右侧选中项点移回左侧。
    /// </summary>
    [RelayCommand]
    public Task RemoveSelectedAsync() => MovePointAsync(SelectedConfigured, fromAvailable: false, down: false);

    /// <summary>
    /// 上移右侧选中项点。
    /// </summary>
    [RelayCommand]
    public Task MoveUpAsync() => MovePointAsync(SelectedConfigured, fromAvailable: false, down: false, up: true);

    /// <summary>
    /// 下移右侧选中项点。
    /// </summary>
    [RelayCommand]
    public Task MoveDownAsync() => MovePointAsync(SelectedConfigured, fromAvailable: false, down: true);

    private Task MovePointAsync(TestItemPoint? point, bool fromAvailable, bool down, bool up = false)
    {
        if (point is null) return Task.CompletedTask;
        if (fromAvailable)
        {
            AvailablePoints.Remove(point);
            ConfiguredPoints.Add(point);
            SelectedConfigured = point;
        }
        else
        {
            var index = ConfiguredPoints.IndexOf(point);
            if (up && index > 0)
            {
                ConfiguredPoints.Move(index, index - 1);
            }
            else if (down && index >= 0 && index < ConfiguredPoints.Count - 1)
            {
                ConfiguredPoints.Move(index, index + 1);
            }
            else if (!up && !down)
            {
                ConfiguredPoints.Remove(point);
                AvailablePoints.Add(point);
                var sorted = AvailablePoints.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
                AvailablePoints.Clear();
                foreach (var p in sorted) AvailablePoints.Add(p);
                SelectedAvailable = point;
            }
        }
        OnPropertyChanged(nameof(HasConfigured));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存当前右侧顺序为型号的自动试验序列。
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (SelectedModel is null) throw new Core.Common.DomainException("请选择产品型号");
            var ids = ConfiguredPoints.Select(p => p.Id).ToList();
            await _services.TestPoints.SaveConfigurationAsync(_actor, SelectedModel.Id, ids);
            StatusMessage = "项点配置已保存";
            await LoadConfigurationAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
