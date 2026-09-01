using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Domain;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.TestDefinitions;

namespace XXX.TestBench.App.ViewModels;

/// <summary>配方中心（6 个 Tab）：产品类型/产品型号/试验项定义/参数定义/配方版本/配方编辑器。写操作权限 Manage*。</summary>
public sealed class RecipeCenterViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public RecipeCenterViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
    }

    public override string Title => "配方中心";

    public ObservableCollection<ProductType> Types { get; } = new();
    public ObservableCollection<ProductModel> Models { get; } = new();
    public ObservableCollection<TestItemDefinition> Items { get; } = new();
    public ObservableCollection<ParameterDefinition> Parameters { get; } = new();
    public ObservableCollection<RecipeVersion> Versions { get; } = new();
    public ObservableCollection<RecipeItem> RecipeItems { get; } = new();
    public ObservableCollection<ParameterDefinition> EditorParameters { get; } = new();
    public ObservableCollection<RecipeParameterValue> EditorValues { get; } = new();

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set { if (SetField(ref _selectedTabIndex, value)) _ = OnTabChangedAsync(); } }

    public RelayCommand LoadCommand { get; }

    // ---- 新增字段 ----
    private string _newCode = string.Empty;
    public string NewCode { get => _newCode; set => SetField(ref _newCode, value); }
    private string _newName = string.Empty;
    public string NewName { get => _newName; set => SetField(ref _newName, value); }
    private string _newExecutor = string.Empty;
    public string NewExecutor { get => _newExecutor; set => SetField(ref _newExecutor, value); }
    private string _newParamCode = string.Empty;
    public string NewParamCode { get => _newParamCode; set => SetField(ref _newParamCode, value); }
    private string _newParamName = string.Empty;
    public string NewParamName { get => _newParamName; set => SetField(ref _newParamName, value); }
    private string _newParamMin = string.Empty;
    public string NewParamMin { get => _newParamMin; set => SetField(ref _newParamMin, value); }
    private string _newParamMax = string.Empty;
    public string NewParamMax { get => _newParamMax; set => SetField(ref _newParamMax, value); }

    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductType> ModelTypeOptions { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductType> VersionTypeOptions { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductModel> VersionModelOptions { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductModel> EditorModelOptions { get; } = new();
    public ObservableCollection<TestItemDefinition> EditorItemOptions { get; } = new();
    public ObservableCollection<TestItemDefinition> ParamItemOptions { get; } = new();

    private ProductType? _selectedType;
    public ProductType? SelectedType { get => _selectedType; set => SetField(ref _selectedType, value); }
    private ProductModel? _selectedModel;
    public ProductModel? SelectedModel { get => _selectedModel; set { if (SetField(ref _selectedModel, value)) _ = LoadVersionsAsync(); } }
    private TestItemDefinition? _selectedItem;
    public TestItemDefinition? SelectedItem { get => _selectedItem; set { if (SetField(ref _selectedItem, value)) _ = LoadParametersAsync(); } }
    private TestItemDefinition? _selectedParamItem;
    public TestItemDefinition? SelectedParamItem { get => _selectedParamItem; set { if (SetField(ref _selectedParamItem, value)) _ = LoadParametersAsync(); } }
    private RecipeVersion? _selectedVersion;
    public RecipeVersion? SelectedVersion { get => _selectedVersion; set { if (SetField(ref _selectedVersion, value)) _ = LoadEditorAsync(); } }
    private RecipeItem? _selectedRecipeItem;
    public RecipeItem? SelectedRecipeItem { get => _selectedRecipeItem; set { if (SetField(ref _selectedRecipeItem, value)) _ = LoadEditorValuesAsync(); } }
    private ProductType? _selectedModelType;
    public ProductType? SelectedModelType { get => _selectedModelType; set => SetField(ref _selectedModelType, value); }
    private ProductType? _selectedVersionType;
    public ProductType? SelectedVersionType { get => _selectedVersionType; set { if (SetField(ref _selectedVersionType, value)) _ = LoadVersionModelsAsync(); } }
    private TestItemDefinition? _selectedEditorItemOption;
    public TestItemDefinition? SelectedEditorItemOption { get => _selectedEditorItemOption; set => SetField(ref _selectedEditorItemOption, value); }

    private string _editorSortOrder = "1";
    public string EditorSortOrder { get => _editorSortOrder; set => SetField(ref _editorSortOrder, value); }
    private string _editorParamValue = string.Empty;
    public string EditorParamValue { get => _editorParamValue; set => SetField(ref _editorParamValue, value); }
    private ParameterDefinition? _selectedEditorParameter;
    public ParameterDefinition? SelectedEditorParameter { get => _selectedEditorParameter; set => SetField(ref _selectedEditorParameter, value); }

    private string _status = string.Empty;
    public string Status { get => _status; private set => SetField(ref _status, value); }

    public override async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadTypesAsync();
        await LoadItemsAsync();
        await LoadModelTypeOptionsAsync();
        await LoadVersionTypeOptionsAsync();
        await LoadEditorModelOptionsAsync();
        await LoadParamItemOptionsAsync();
        await LoadEditorItemOptionsAsync();
    }

    private async Task OnTabChangedAsync()
    {
        switch (SelectedTabIndex)
        {
            case 0: await LoadTypesAsync(); break;
            case 1: await LoadModelsAsync(); break;
            case 2: await LoadItemsAsync(); break;
            case 3: await LoadParametersAsync(); break;
            case 4: await LoadVersionsAsync(); break;
            case 5: await LoadEditorAsync(); break;
        }
    }

    private async Task LoadTypesAsync()
    {
        Types.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) Types.Add(t);
    }

    private async Task LoadModelsAsync()
    {
        Models.Clear();
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedModelType?.Id, includeDisabled: true)) Models.Add(m);
    }

    private async Task LoadItemsAsync()
    {
        Items.Clear();
        foreach (var i in await _services.DefinitionRepository.ListItemsAsync(includeDisabled: true)) Items.Add(i);
    }

    private async Task LoadParametersAsync()
    {
        Parameters.Clear();
        EditorParameters.Clear();
        var item = SelectedParamItem ?? SelectedItem;
        if (item is null) return;
        foreach (var p in await _services.DefinitionRepository.ListParametersAsync(item.Id)) Parameters.Add(p);
    }

    private async Task LoadVersionsAsync()
    {
        Versions.Clear();
        if (SelectedVersionType is null || SelectedModel is null) return;
        foreach (var v in await _services.RecipeRepository.ListByModelAsync(SelectedModel.Id, includeRetired: true)) Versions.Add(v);
    }

    private async Task LoadVersionModelsAsync()
    {
        VersionModelOptions.Clear();
        if (SelectedVersionType is null) return;
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedVersionType.Id, includeDisabled: true)) VersionModelOptions.Add(m);
    }

    private async Task LoadEditorAsync()
    {
        RecipeItems.Clear();
        EditorParameters.Clear();
        EditorValues.Clear();
        if (SelectedVersion is null || SelectedVersion.Status != RecipeStatus.Draft) return;
        foreach (var ri in await _services.RecipeRepository.ListItemsAsync(SelectedVersion.Id)) RecipeItems.Add(ri);
        await LoadEditorItemOptionsAsync();
    }

    private async Task LoadEditorValuesAsync()
    {
        EditorParameters.Clear();
        EditorValues.Clear();
        if (SelectedRecipeItem is null) return;
        var defs = await _services.DefinitionRepository.ListParametersAsync(SelectedRecipeItem.TestItemDefinitionId);
        foreach (var p in defs) EditorParameters.Add(p);
        var values = await _services.RecipeRepository.ListParameterValuesAsync(SelectedVersion?.Id ?? 0);
        foreach (var p in defs)
        {
            var v = values.FirstOrDefault(x => x.RecipeItemId == SelectedRecipeItem.Id && x.ParameterDefinitionId == p.Id);
            EditorValues.Add(v ?? new RecipeParameterValue { RecipeItemId = SelectedRecipeItem.Id, ParameterDefinitionId = p.Id, RawValue = string.Empty });
        }
    }

    private async Task LoadModelTypeOptionsAsync()
    {
        ModelTypeOptions.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) ModelTypeOptions.Add(t);
    }

    private async Task LoadVersionTypeOptionsAsync()
    {
        VersionTypeOptions.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) VersionTypeOptions.Add(t);
    }

    private async Task LoadEditorModelOptionsAsync()
    {
        EditorModelOptions.Clear();
        foreach (var m in await _services.ProductRepository.ListModelsAsync(null, includeDisabled: true)) EditorModelOptions.Add(m);
    }

    private async Task LoadParamItemOptionsAsync()
    {
        ParamItemOptions.Clear();
        foreach (var i in await _services.DefinitionRepository.ListItemsAsync(includeDisabled: true)) ParamItemOptions.Add(i);
    }

    private async Task LoadEditorItemOptionsAsync()
    {
        EditorItemOptions.Clear();
        foreach (var i in await _services.DefinitionRepository.ListItemsAsync(includeDisabled: false)) EditorItemOptions.Add(i);
    }

    // ---- Tab0 产品类型 ----
    public RelayCommand AddTypeCommand => new(async () =>
    {
        try { await _services.Products.CreateTypeAsync(_actor, NewCode, NewName); Status = "产品类型已创建"; await LoadTypesAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand DisableSelectedTypeCommand => new(async () =>
    {
        try { if (SelectedType is null) return; await _services.Products.SetTypeEnabledAsync(_actor, SelectedType.Id, !SelectedType.IsEnabled); await LoadTypesAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    // ---- Tab1 产品型号 ----
    public RelayCommand AddModelCommand => new(async () =>
    {
        try { if (SelectedModelType is null) throw new Core.Common.DomainException("请选择产品类型"); await _services.Products.CreateModelAsync(_actor, SelectedModelType.Id, NewCode, NewName); Status = "产品型号已创建"; await LoadModelsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand DisableSelectedModelCommand => new(async () =>
    {
        try { if (SelectedModel is null) return; await _services.Products.SetModelEnabledAsync(_actor, SelectedModel.Id, !SelectedModel.IsEnabled); await LoadModelsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    // ---- Tab2 试验项 ----
    public RelayCommand AddItemCommand => new(async () =>
    {
        try { await _services.Definitions.CreateItemAsync(_actor, NewCode, NewName, NewExecutor, "PassFail", Items.Count + 1); Status = "试验项已创建"; await LoadItemsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand DisableSelectedItemCommand => new(async () =>
    {
        try { if (SelectedItem is null) return; await _services.Definitions.SetItemEnabledAsync(_actor, SelectedItem.Id, !SelectedItem.IsEnabled); await LoadItemsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    // ---- Tab3 参数 ----
    public RelayCommand AddParamCommand => new(async () =>
    {
        try
        {
            var item = SelectedParamItem ?? SelectedItem;
            if (item is null) throw new Core.Common.DomainException("请选择试验项");
            decimal? min = decimal.TryParse(NewParamMin, out var m) ? m : null;
            decimal? max = decimal.TryParse(NewParamMax, out var x) ? x : null;
            await _services.Definitions.CreateParameterAsync(_actor, item.Id, NewParamCode, NewParamName, ParameterDataType.Decimal, false, null, min, max, null, null, Parameters.Count + 1);
            Status = "参数已创建";
            await LoadParametersAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    });

    // ---- Tab4 配方版本 ----
    public RelayCommand CreateDraftCommand => new(async () =>
    {
        try
        {
            var model = SelectedModel ?? EditorModelOptions.FirstOrDefault();
            if (model is null) throw new Core.Common.DomainException("请选择产品型号");
            var draft = await _services.Recipes.CreateDraftAsync(_actor, model.Id, NewName);
            Status = $"草稿已创建（V{draft.Version}）";
            await LoadVersionsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand PublishSelectedVersionCommand => new(async () =>
    {
        try { if (SelectedVersion is null) return; await _services.Recipes.PublishAsync(_actor, SelectedVersion.Id); Status = "配方已发布"; await LoadVersionsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand RetireSelectedVersionCommand => new(async () =>
    {
        try { if (SelectedVersion is null) return; await _services.Recipes.RetireAsync(_actor, SelectedVersion.Id); Status = "配方已停用"; await LoadVersionsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand CopySelectedVersionCommand => new(async () =>
    {
        try { if (SelectedVersion is null) return; var copy = await _services.Recipes.NewVersionFromAsync(_actor, SelectedVersion.Id); Status = $"已复制为新草稿 V{copy.Version}"; await LoadVersionsAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    });

    // ---- Tab5 配方编辑器 ----
    public RelayCommand AddRecipeItemCommand => new(async () =>
    {
        try
        {
            if (SelectedVersion is null || SelectedVersion.Status != RecipeStatus.Draft) throw new Core.Common.DomainException("请选择草稿配方");
            if (SelectedEditorItemOption is null) throw new Core.Common.DomainException("请选择试验项");
            var sort = int.TryParse(EditorSortOrder, out var s) ? s : RecipeItems.Count + 1;
            await _services.Recipes.AddItemAsync(_actor, SelectedVersion.Id, SelectedEditorItemOption.Id, sort);
            Status = "项点已加入配方";
            await LoadEditorAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand SetParamValueCommand => new(async () =>
    {
        try
        {
            if (SelectedVersion is null || SelectedRecipeItem is null || SelectedEditorParameter is null)
                throw new Core.Common.DomainException("请选择项点与参数");
            await _services.Recipes.SetParameterValueAsync(_actor, SelectedVersion.Id, SelectedRecipeItem.Id, SelectedEditorParameter.Id, EditorParamValue);
            Status = "参数值已保存";
            await LoadEditorValuesAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand ValidateEditorCommand => new(async () =>
    {
        try
        {
            if (SelectedVersion is null) throw new Core.Common.DomainException("请选择配方");
            var errors = await _services.Recipes.ValidateDraftAsync(SelectedVersion.Id);
            Status = errors.Count == 0 ? "校验通过" : string.Join("；", errors);
        }
        catch (Exception ex) { Status = ex.Message; }
    });

    public RelayCommand PublishEditorCommand => new(async () =>
    {
        try
        {
            if (SelectedVersion is null) throw new Core.Common.DomainException("请选择配方");
            await _services.Recipes.PublishAsync(_actor, SelectedVersion.Id);
            Status = "配方已发布";
            await LoadVersionsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    });
}
