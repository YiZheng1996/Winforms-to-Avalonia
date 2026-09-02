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
/// 参数管理页面（三个标签页）：产品类型、产品型号、试验参数。
/// 试验参数为代码固定字段（项目/类型/型号三级），不做动态定义表，也不做配方编辑器。
/// </summary>
public sealed partial class RecipeCenterViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public RecipeCenterViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "参数管理";

    /// <summary>产品类型列表。</summary>
    public ObservableCollection<ProductType> Types { get; } = new();

    /// <summary>产品型号列表。</summary>
    public ObservableCollection<ProductModel> Models { get; } = new();

    /// <summary>新增产品型号时可选择的产品类型。</summary>
    public ObservableCollection<ProductType> ModelTypeOptions { get; } = new();

    /// <summary>试验参数页可选择的产品类型。</summary>
    public ObservableCollection<ProductType> ParamTypeOptions { get; } = new();

    /// <summary>试验参数页可选择的产品型号。</summary>
    public ObservableCollection<ProductModel> ParamModelOptions { get; } = new();

    /// <summary>产品类型列表是否有数据。</summary>
    public bool HasTypes => Types.Count > 0;

    /// <summary>产品型号列表是否有数据。</summary>
    public bool HasModels => Models.Count > 0;

    /// <summary>当前标签页序号；变化时自动加载该页数据。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => _ = OnTabChangedAsync();

    /// <summary>新增产品类型时录入的代码。</summary>
    [ObservableProperty]
    private string _newCode = string.Empty;

    /// <summary>新增产品类型时录入的名称。</summary>
    [ObservableProperty]
    private string _newName = string.Empty;

    /// <summary>产品类型页当前选中的类型。</summary>
    [ObservableProperty]
    private ProductType? _selectedType;

    /// <summary>产品型号页当前选中的型号。</summary>
    [ObservableProperty]
    private ProductModel? _selectedModel;

    /// <summary>产品型号页筛选的产品类型；变化时自动加载该类型型号。</summary>
    [ObservableProperty]
    private ProductType? _selectedModelType;

    partial void OnSelectedModelTypeChanged(ProductType? value) => _ = LoadModelsAsync();

    /// <summary>试验参数页当前选中的产品类型。</summary>
    [ObservableProperty]
    private ProductType? _selectedParamType;

    partial void OnSelectedParamTypeChanged(ProductType? value) => _ = LoadParamModelOptionsAsync();

    /// <summary>试验参数页当前选中的产品型号。</summary>
    [ObservableProperty]
    private ProductModel? _selectedParamModel;

    partial void OnSelectedParamModelChanged(ProductModel? value) => _ = LoadModelParameterAsync();

    /// <summary>项目参数：试验时间输入文字。</summary>
    [ObservableProperty]
    private string _testTimeInput = string.Empty;

    /// <summary>类型参数：试验电压输入文字。</summary>
    [ObservableProperty]
    private string _testVoltageInput = string.Empty;

    /// <summary>型号参数：保护电流输入文字。</summary>
    [ObservableProperty]
    private string _protectCurrentInput = string.Empty;

    private string _status = string.Empty;

    /// <summary>页面底部展示的操作结果或错误提示。</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>页面加载命令：加载产品与参数页共用的基础选项数据。</summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadTypesAsync();
        await LoadModelTypeOptionsAsync();
        await LoadParamTypeOptionsAsync();
        await LoadProjectParameterAsync();
    }

    private async Task OnTabChangedAsync()
    {
        switch (SelectedTabIndex)
        {
            case 0: await LoadTypesAsync(); break;
            case 1: await LoadModelsAsync(); break;
            case 2: await LoadParamTypeOptionsAsync(); await LoadProjectParameterAsync(); break;
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
        if (SelectedParamType is null) return;
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedParamType.Id, includeDisabled: true)) ParamModelOptions.Add(m);
        await LoadTypeParameterAsync();
    }

    private async Task LoadProjectParameterAsync()
    {
        var value = await _services.TestParameterRepository.GetProjectAsync();
        TestTimeInput = value?.TestTimeSeconds.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task LoadTypeParameterAsync()
    {
        if (SelectedParamType is null)
        {
            TestVoltageInput = string.Empty;
            return;
        }
        var value = await _services.TestParameterRepository.GetTypeAsync(SelectedParamType.Id);
        TestVoltageInput = value?.TestVoltageV.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task LoadModelParameterAsync()
    {
        if (SelectedParamModel is null)
        {
            ProtectCurrentInput = string.Empty;
            return;
        }
        var value = await _services.TestParameterRepository.GetModelAsync(SelectedParamModel.Id);
        ProtectCurrentInput = value?.ProtectCurrentMa.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>保存项目级参数（试验时间）。</summary>
    [RelayCommand]
    private async Task SaveProjectParameterAsync()
    {
        try
        {
            if (!int.TryParse(TestTimeInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                throw new Core.Common.DomainException("试验时间必须为整数。");
            await _services.Parameters.SaveProjectAsync(_actor, seconds);
            Status = "项目参数已保存";
            await LoadProjectParameterAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>保存产品类型级参数（试验电压）。</summary>
    [RelayCommand]
    private async Task SaveTypeParameterAsync()
    {
        try
        {
            if (SelectedParamType is null) throw new Core.Common.DomainException("请选择产品类型");
            if (!double.TryParse(TestVoltageInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var voltage))
                throw new Core.Common.DomainException("试验电压必须为数值。");
            await _services.Parameters.SaveTypeAsync(_actor, SelectedParamType.Id, voltage);
            Status = "类型参数已保存";
            await LoadTypeParameterAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>保存产品型号级参数（保护电流）。</summary>
    [RelayCommand]
    private async Task SaveModelParameterAsync()
    {
        try
        {
            if (SelectedParamModel is null) throw new Core.Common.DomainException("请选择产品型号");
            if (!double.TryParse(ProtectCurrentInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
                throw new Core.Common.DomainException("保护电流必须为数值。");
            await _services.Parameters.SaveModelAsync(_actor, SelectedParamModel.Id, current);
            Status = "型号参数已保存";
            await LoadModelParameterAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>处理产品类型弹窗的提交结果并创建类型。</summary>
    public async Task CreateTypeFromDialogAsync(ProductMasterDataDialogResult result)
        => await CreateTypeAsync(result.Code, result.Name);

    private async Task CreateTypeAsync(string code, string name)
    {
        try
        {
            await _services.Products.CreateTypeAsync(_actor, code, name);
            Status = "产品类型已创建";
            NewCode = string.Empty;
            NewName = string.Empty;
            await LoadTypesAsync();
            await LoadModelTypeOptionsAsync();
            await LoadParamTypeOptionsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>新增产品类型命令：按录入的代码与名称创建类型。</summary>
    [RelayCommand]
    private async Task AddTypeAsync() => await CreateTypeAsync(NewCode, NewName);

    /// <summary>停用或启用当前选中的产品类型。</summary>
    [RelayCommand]
    private async Task DisableSelectedTypeAsync()
    {
        try
        {
            if (SelectedType is null) return;
            await _services.Products.SetTypeEnabledAsync(_actor, SelectedType.Id, !SelectedType.IsEnabled);
            await LoadTypesAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>处理产品型号弹窗的提交结果并创建型号。</summary>
    public async Task CreateModelFromDialogAsync(ProductMasterDataDialogResult result)
    {
        try
        {
            if (result.ProductTypeId is null) throw new Core.Common.DomainException("请选择产品类型");
            await _services.Products.CreateModelAsync(_actor, result.ProductTypeId.Value, result.Code, result.Name);
            Status = "产品型号已创建";
            NewCode = string.Empty;
            NewName = string.Empty;
            SelectedModelType = ModelTypeOptions.FirstOrDefault(type => type.Id == result.ProductTypeId.Value);
            await LoadModelsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>新增产品型号命令：按所选类型与录入内容创建型号。</summary>
    [RelayCommand]
    private async Task AddModelAsync() => await CreateModelAsync(SelectedModelType?.Id, NewCode, NewName);

    private async Task CreateModelAsync(int? productTypeId, string code, string name)
    {
        try
        {
            if (productTypeId is null) throw new Core.Common.DomainException("请选择产品类型");
            await _services.Products.CreateModelAsync(_actor, productTypeId.Value, code, name);
            Status = "产品型号已创建";
            NewCode = string.Empty;
            NewName = string.Empty;
            await LoadModelsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>停用或启用当前选中的产品型号。</summary>
    [RelayCommand]
    private async Task DisableSelectedModelAsync()
    {
        try
        {
            if (SelectedModel is null) return;
            await _services.Products.SetModelEnabledAsync(_actor, SelectedModel.Id, !SelectedModel.IsEnabled);
            await LoadModelsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}
