using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Domain;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 参数管理页面：产品类型、产品型号、试验项点、项点配置与试验参数。设备点位已独立为左侧导航菜单页。
/// 试验参数为代码固定字段（项目级 + 产品类型/产品型号组合级），不做动态定义表；主数据通过数据库 ID 关联。
/// </summary>
public sealed partial class ParameterManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public ParameterManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        TestPointTab = new TestPointManagementViewModel(services, actor);
        PointConfigTab = new PointConfigurationViewModel(services, actor);
        _selectedTabIndex = CanManageProducts ? 0 : CanManageTestPoints ? 2 : 4;
    }

    public override string Title => "参数管理";

    /// <summary>
    /// 当前用户是否可以维护产品类型和产品型号。
    /// </summary>
    public bool CanManageProducts => _actor.HasPermission(PermissionCode.ManageProducts);

    /// <summary>
    /// 当前用户是否可以维护试验项点及其型号配置。
    /// </summary>
    public bool CanManageTestPoints => _actor.HasPermission(PermissionCode.ManageTestPoints);

    /// <summary>
    /// 当前用户是否可以维护项目级和产品组合级试验参数。
    /// </summary>
    public bool CanManageTestDefinitions => _actor.HasPermission(PermissionCode.ManageTestDefinitions);

    /// <summary>
    /// 试验项点标签页的视图模型。
    /// </summary>
    public TestPointManagementViewModel TestPointTab { get; }

    /// <summary>
    /// 项点配置标签页的视图模型。
    /// </summary>
    public PointConfigurationViewModel PointConfigTab { get; }

    /// <summary>
    /// 产品类型列表。
    /// </summary>
    public ObservableCollection<ProductType> Types { get; } = new();

    /// <summary>
    /// 产品型号列表。
    /// </summary>
    public ObservableCollection<ProductModel> Models { get; } = new();

    /// <summary>
    /// 新增产品型号时可选择的产品类型。
    /// </summary>
    public ObservableCollection<ProductType> ModelTypeOptions { get; } = new();

    /// <summary>
    /// 试验参数页可选择的产品类型。
    /// </summary>
    public ObservableCollection<ProductType> ParamTypeOptions { get; } = new();

    /// <summary>
    /// 试验参数页可选择的产品型号。
    /// </summary>
    public ObservableCollection<ProductModel> ParamModelOptions { get; } = new();

    /// <summary>
    /// 产品类型列表是否有数据。
    /// </summary>
    public bool HasTypes => Types.Count > 0;

    /// <summary>
    /// 产品型号列表是否有数据。
    /// </summary>
    public bool HasModels => Models.Count > 0;

    /// <summary>
    /// 当前标签页序号；变化时自动加载该页数据。
    /// </summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => _ = OnTabChangedAsync();

    /// <summary>
    /// 产品类型页当前选中的类型。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedType;

    /// <summary>
    /// 产品型号页当前选中的型号。
    /// </summary>
    [ObservableProperty]
    private ProductModel? _selectedModel;

    /// <summary>
    /// 产品型号页筛选的产品类型；变化时自动加载该类型型号。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedModelType;

    partial void OnSelectedModelTypeChanged(ProductType? value) => _ = LoadModelsAsync();

    /// <summary>
    /// 试验参数页当前选中的产品类型。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedParamType;

    partial void OnSelectedParamTypeChanged(ProductType? value) => _ = LoadParamModelOptionsAsync();

    /// <summary>
    /// 试验参数页当前选中的产品型号。
    /// </summary>
    [ObservableProperty]
    private ProductModel? _selectedParamModel;

    partial void OnSelectedParamModelChanged(ProductModel? value) => _ = LoadProductParameterAsync();

    /// <summary>
    /// 项目参数：试验时间输入文字。
    /// </summary>
    [ObservableProperty]
    private string _testTimeInput = string.Empty;

    /// <summary>
    /// 产品参数：试验电压输入文字。
    /// </summary>
    [ObservableProperty]
    private string _testVoltageInput = string.Empty;

    /// <summary>
    /// 产品参数：保护电流输入文字。
    /// </summary>
    [ObservableProperty]
    private string _protectCurrentInput = string.Empty;

    private string _status = string.Empty;

    /// <summary>
    /// 页面底部展示的操作结果或错误提示。
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>
    /// 页面加载命令：加载产品与参数页共用的基础选项数据。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadTypesAsync();
        await LoadModelTypeOptionsAsync();
        await LoadParamTypeOptionsAsync();
        await LoadProjectParameterAsync();
        if (SelectedTabIndex == 2)
            await TestPointTab.LoadAsync(ct);
        else if (SelectedTabIndex == 3)
            await PointConfigTab.LoadAsync(ct);
    }

    private async Task OnTabChangedAsync()
    {
        switch (SelectedTabIndex)
        {
            case 0: await LoadTypesAsync(); break;
            case 1: await LoadModelsAsync(); break;
            case 2: await TestPointTab.LoadAsync(); break;
            case 3: await PointConfigTab.LoadAsync(); break;
            case 4: await LoadParamTypeOptionsAsync(); await LoadProjectParameterAsync(); break;
        }
    }

    private async Task LoadTypesAsync()
    {
        Types.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) Types.Add(t);
        OnPropertyChanged(nameof(HasTypes));
    }

    private async Task LoadModelsAsync()
    {
        Models.Clear();
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedModelType?.Id, includeDisabled: true)) Models.Add(m);
        OnPropertyChanged(nameof(HasModels));
    }

    private async Task LoadModelTypeOptionsAsync()
    {
        ModelTypeOptions.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) ModelTypeOptions.Add(t);
    }

    private async Task LoadParamTypeOptionsAsync()
    {
        ParamTypeOptions.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) ParamTypeOptions.Add(t);
    }

    public async Task LoadParamModelOptionsAsync()
    {
        ParamModelOptions.Clear();
        SelectedParamModel = null;
        TestVoltageInput = string.Empty;
        ProtectCurrentInput = string.Empty;
        if (SelectedParamType is null) return;
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedParamType.Id, includeDisabled: true)) ParamModelOptions.Add(m);
    }

    private async Task LoadProjectParameterAsync()
    {
        var value = await _services.TestParameterRepository.GetProjectAsync();
        TestTimeInput = value?.TestTimeSeconds.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task LoadProductParameterAsync()
    {
        if (SelectedParamModel is null)
        {
            TestVoltageInput = string.Empty;
            ProtectCurrentInput = string.Empty;
            return;
        }
        var value = await _services.TestParameterRepository.GetProductAsync(
            SelectedParamModel.ProductTypeId,
            SelectedParamModel.Id);
        TestVoltageInput = value?.TestVoltageV.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ProtectCurrentInput = value?.ProtectCurrentMa.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// 通过页面编辑弹窗保存项目级参数（试验时间）。
    /// </summary>
    [RelayCommand]
    private async Task SaveProjectParameterAsync() => await SaveProjectParameterFromDialogAsync(TestTimeInput);

    public async Task<OperationFeedback> SaveProjectParameterFromDialogAsync(string value)
    {
        try
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                throw new Core.Common.DomainException("试验时间必须为整数。");
            await _services.Parameters.SaveProjectAsync(_actor, seconds);
            await LoadProjectParameterAsync();
            return SetFeedback(true, "项目参数已保存");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 通过页面编辑弹窗保存产品级参数（产品类型 + 产品型号）。
    /// </summary>
    [RelayCommand]
    private async Task SaveProductParameterAsync()
        => await SaveProductParameterFromDialogAsync(TestVoltageInput, ProtectCurrentInput);

    public async Task<OperationFeedback> SaveProductParameterFromDialogAsync(string voltageValue, string currentValue)
    {
        try
        {
            if (SelectedParamModel is null) throw new Core.Common.DomainException("请选择产品型号");
            if (!double.TryParse(voltageValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var voltage))
                throw new Core.Common.DomainException("试验电压必须为数值。");
            if (!double.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
                throw new Core.Common.DomainException("保护电流必须为数值。");
            await _services.Parameters.SaveProductAsync(
                _actor,
                SelectedParamModel.ProductTypeId,
                SelectedParamModel.Id,
                voltage,
                current);
            await LoadProductParameterAsync();
            return SetFeedback(true, "产品参数已保存");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 处理产品类型弹窗的提交结果并创建类型。
    /// </summary>
    public async Task<OperationFeedback> CreateTypeFromDialogAsync(ProductMasterDataDialogResult result)
        => await CreateTypeAsync(result.Name);

    private async Task<OperationFeedback> CreateTypeAsync(string name)
    {
        try
        {
            await _services.Products.CreateTypeAsync(_actor, name);
            await LoadTypesAsync();
            await LoadModelTypeOptionsAsync();
            await LoadParamTypeOptionsAsync();
            return SetFeedback(true, "产品类型已创建");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 停用或启用当前选中的产品类型。
    /// </summary>
    [RelayCommand]
    private async Task DisableSelectedTypeAsync() => await ToggleSelectedTypeAsync();

    public async Task<OperationFeedback> ToggleSelectedTypeAsync()
    {
        try
        {
            if (SelectedType is null)
                return SetFeedback(false, "请先选择产品类型");

            var isEnabled = !SelectedType.IsEnabled;
            await _services.Products.SetTypeEnabledAsync(_actor, SelectedType.Id, isEnabled);
            await LoadTypesAsync();
            return SetFeedback(true, isEnabled ? "产品类型已启用" : "产品类型已停用");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 删除选中的产品类型；存在型号或试验项点时由服务拒绝。
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedTypeAsync() => await DeleteSelectedTypeCoreAsync();

    public async Task<OperationFeedback> DeleteSelectedTypeFromDialogAsync()
        => await DeleteSelectedTypeCoreAsync();

    private async Task<OperationFeedback> DeleteSelectedTypeCoreAsync()
    {
        try
        {
            if (SelectedType is null)
                return SetFeedback(false, "请先选择产品类型");

            var typeId = SelectedType.Id;
            await _services.Products.DeleteTypeAsync(_actor, typeId);
            SelectedType = null;
            SelectedModelType = null;
            SelectedParamType = null;
            SelectedParamModel = null;
            await LoadTypesAsync();
            await LoadModelTypeOptionsAsync();
            await LoadParamTypeOptionsAsync();
            await LoadModelsAsync();
            await LoadParamModelOptionsAsync();
            return SetFeedback(true, "产品类型已删除");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 处理产品型号弹窗的提交结果并创建型号。
    /// </summary>
    public async Task<OperationFeedback> CreateModelFromDialogAsync(ProductMasterDataDialogResult result)
    {
        try
        {
            if (result.ProductTypeId is null) throw new Core.Common.DomainException("请选择产品类型");
            await _services.Products.CreateModelAsync(_actor, result.ProductTypeId.Value, result.Name);
            SelectedModelType = ModelTypeOptions.FirstOrDefault(type => type.Id == result.ProductTypeId.Value);
            await LoadModelsAsync();
            return SetFeedback(true, "产品型号已创建");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 处理产品类型弹窗的编辑提交结果并更新类型名称。
    /// </summary>
    public async Task<OperationFeedback> UpdateTypeFromDialogAsync(int typeId, ProductMasterDataDialogResult result)
    {
        try
        {
            await _services.Products.RenameTypeAsync(_actor, typeId, result.Name);
            await LoadTypesAsync();
            await LoadModelTypeOptionsAsync();
            await LoadParamTypeOptionsAsync();
            if (Types.FirstOrDefault(type => type.Id == typeId) is { } updatedType)
                SelectedType = updatedType;
            return SetFeedback(true, "产品类型已更新");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 处理产品型号弹窗的编辑提交结果并更新型号名称。
    /// </summary>
    public async Task<OperationFeedback> UpdateModelFromDialogAsync(int modelId, ProductMasterDataDialogResult result)
    {
        try
        {
            await _services.Products.RenameModelAsync(_actor, modelId, result.Name);
            await LoadModelsAsync();
            await LoadParamModelOptionsAsync();
            if (Models.FirstOrDefault(model => model.Id == modelId) is { } updatedModel)
                SelectedModel = updatedModel;
            return SetFeedback(true, "产品型号已更新");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 停用或启用当前选中的产品型号。
    /// </summary>
    [RelayCommand]
    private async Task DisableSelectedModelAsync() => await ToggleSelectedModelAsync();

    public async Task<OperationFeedback> ToggleSelectedModelAsync()
    {
        try
        {
            if (SelectedModel is null)
                return SetFeedback(false, "请先选择产品型号");

            var isEnabled = !SelectedModel.IsEnabled;
            await _services.Products.SetModelEnabledAsync(_actor, SelectedModel.Id, isEnabled);
            await LoadModelsAsync();
            return SetFeedback(true, isEnabled ? "产品型号已启用" : "产品型号已停用");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    /// <summary>
    /// 删除选中的产品型号；存在试验记录时由服务拒绝。
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedModelAsync() => await DeleteSelectedModelCoreAsync();

    public async Task<OperationFeedback> DeleteSelectedModelFromDialogAsync()
        => await DeleteSelectedModelCoreAsync();

    private async Task<OperationFeedback> DeleteSelectedModelCoreAsync()
    {
        try
        {
            if (SelectedModel is null)
                return SetFeedback(false, "请先选择产品型号");

            var modelId = SelectedModel.Id;
            await _services.Products.DeleteModelAsync(_actor, modelId);
            SelectedModel = null;
            if (SelectedParamModel?.Id == modelId)
                SelectedParamModel = null;
            await LoadModelsAsync();
            await LoadParamModelOptionsAsync();
            return SetFeedback(true, "产品型号已删除");
        }
        catch (Exception ex) { return SetFeedback(false, ex.Message); }
    }

    private OperationFeedback SetFeedback(bool succeeded, string message)
    {
        Status = message;
        return new OperationFeedback(succeeded, message);
    }
}
