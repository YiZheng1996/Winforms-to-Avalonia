using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 提供九类 Legacy 管理界面的专属字段与会话内行为；未接入 SQLite 前，任何变更都不会跨进程持久化。
/// </summary>
public sealed class ManagementViewModel : ObservableObject
{
    private ManagementCategoryViewModel _selectedCategory;
    private ManagementRecordViewModel? _selectedRecord;
    private ManagementOptionViewModel? _selectedAvailableOption;
    private ManagementOptionViewModel? _selectedConfiguredOption;
    private string _draftName = string.Empty;
    private string _draftCode = string.Empty;
    private string _draftDescription = string.Empty;
    private string _feedbackText = "选择管理模块后，可验证该模块的专属字段与会话内交互。";
    private int _nextSessionId = 1;

    public ManagementViewModel()
    {
        Categories = new ObservableCollection<ManagementCategoryViewModel>(CreateCategories());
        _selectedCategory = Categories[0];
        EditorFields = [];
        NameOptions = [];
        CodeOptions = [];
        DescriptionOptions = [];

        AddCommand = new RelayCommand(AddRecord, () => CanAdd);
        UpdateCommand = new RelayCommand(UpdateRecord, () => CanUpdate);
        DeleteCommand = new RelayCommand(DeleteRecord, () => CanDelete);
        ResetCommand = new RelayCommand(() => ResetDraft(clearSelections: true));
        PublishSelectedCommand = new RelayCommand(PublishSelected, () => CanPublishSelected);
        MoveToConfiguredCommand = new RelayCommand(MoveToConfigured, () => CanMoveToConfigured);
        MoveToAvailableCommand = new RelayCommand(MoveToAvailable, () => CanMoveToAvailable);

        RebuildEditorFields();
        RefreshReferenceOptions();
    }

    public ObservableCollection<ManagementCategoryViewModel> Categories { get; }
    public ObservableCollection<ManagementFieldViewModel> EditorFields { get; }
    public ObservableCollection<string> NameOptions { get; }
    public ObservableCollection<string> CodeOptions { get; }
    public ObservableCollection<string> DescriptionOptions { get; }
    public ICommand AddCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand PublishSelectedCommand { get; }
    public ICommand MoveToConfiguredCommand { get; }
    public ICommand MoveToAvailableCommand { get; }

    public ManagementCategoryViewModel SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null || !SetProperty(ref _selectedCategory, value))
                return;

            _selectedRecord = null;
            OnPropertyChanged(nameof(SelectedRecord));
            RebuildEditorFields();
            ResetDraft(clearSelections: false);
            RefreshReferenceOptions();
            NotifyCategoryState();
        }
    }

    public ManagementRecordViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value))
                return;
            if (value is not null)
                LoadRecordIntoEditor(value);
            RaiseCommandState();
        }
    }

    public ManagementOptionViewModel? SelectedAvailableOption
    {
        get => _selectedAvailableOption;
        set
        {
            if (SetProperty(ref _selectedAvailableOption, value))
                RaiseCommandState();
        }
    }

    public ManagementOptionViewModel? SelectedConfiguredOption
    {
        get => _selectedConfiguredOption;
        set
        {
            if (SetProperty(ref _selectedConfiguredOption, value))
                RaiseCommandState();
        }
    }

    public ObservableCollection<ManagementRecordViewModel> Records => SelectedCategory.Records;
    public ObservableCollection<ManagementOptionViewModel> PermissionOptions => SelectedCategory.PermissionOptions;
    public ObservableCollection<ManagementOptionViewModel> AvailableOptions => SelectedCategory.AvailableOptions;
    public ObservableCollection<ManagementOptionViewModel> ConfiguredOptions => SelectedCategory.ConfiguredOptions;
    public string CategoryTitle => SelectedCategory.Title;
    public string CategoryFields => SelectedCategory.Fields;
    public string CategoryEvidence => SelectedCategory.LegacyEvidence;
    public string NameLabel => SelectedCategory.NameLabel;
    public string CodeLabel => SelectedCategory.CodeLabel;
    public string DescriptionLabel => SelectedCategory.DescriptionLabel;
    public bool ShowCode => SelectedCategory.ShowCode;
    public bool ShowDescription => SelectedCategory.ShowDescription;
    public bool NameUsesSelection => SelectedCategory.NameUsesSelection;
    public bool CodeUsesSelection => SelectedCategory.CodeUsesSelection;
    public bool DescriptionUsesSelection => SelectedCategory.DescriptionUsesSelection;
    public bool ShowNameTextInput => !NameUsesSelection;
    public bool ShowCodeTextInput => ShowCode && !CodeUsesSelection;
    public bool ShowDescriptionTextInput => ShowDescription && !DescriptionUsesSelection;
    public bool HasEditorFields => EditorFields.Count > 0;
    public bool ShowPermissionChecklist => SelectedCategory.UsesPermissionChecklist;
    public bool ShowDualList => SelectedCategory.UsesDualList;
    public bool ShowPublishAction => SelectedCategory.Key == "models";
    public string CategoryBoundaryText => SelectedCategory.Key switch
    {
        "users" => "真实账号认证与密码策略尚未接入；本页不保存密码，用户和角色只在当前会话中演示。",
        "permissions" or "allocation" => "真实登录权限、菜单可见性和授权写库尚未接入；本页不能授予、撤销或校验任何实际权限。",
        "models" => "Legacy 型号发布会写入数据库；当前“发布”只标记会话状态，不产生正式发布数据。",
        "test-items" => "Legacy 项点维护会创建、移动或删除动态 .cs 源码文件；该文件副作用保持锁定，本页只记录会话字段。",
        "item-config" => "Legacy 项点配置会按顺序写入外部数据库；当前双列表保留选择顺序，但不会保存或下发。",
        "test-params" => "Legacy 仅编辑车型/型号、报表模板文件名和保存目录；空白“参数界面”不迁移，文件选择与 INI 写入保持锁定。",
        _ => "当前模块仅提供会话内字段校验；外部数据库和配置文件写入保持锁定。"
    };
    public string RecordSummary => $"当前会话 {Records.Count} 条；尚未写入 Legacy SQLite。";
    public string FeedbackText => _feedbackText;
    public string PersistenceBoundaryText =>
        "九类专属字段和选择行为已迁移为 MVVM；真实认证、SQLite 事务、备份恢复和旧库兼容尚未闭合。本页操作只作用于当前进程。";

    public string DraftName
    {
        get => _draftName;
        set
        {
            if (SetProperty(ref _draftName, value))
                RaiseCommandState();
        }
    }

    public string DraftCode
    {
        get => _draftCode;
        set
        {
            if (SetProperty(ref _draftCode, value))
            {
                RefreshReferenceOptions();
                RaiseCommandState();
            }
        }
    }

    public string DraftDescription
    {
        get => _draftDescription;
        set
        {
            if (SetProperty(ref _draftDescription, value))
                RaiseCommandState();
        }
    }

    public bool CanAdd => IsDraftValid && IsDraftUnique(excludedRecordId: null);
    public bool CanUpdate =>
        SelectedRecord is not null &&
        Records.Contains(SelectedRecord) &&
        IsDraftValid &&
        IsDraftUnique(SelectedRecord.Id);
    public bool CanDelete => SelectedRecord is not null && Records.Contains(SelectedRecord);
    public bool CanPublishSelected =>
        ShowPublishAction &&
        SelectedRecord is not null &&
        Records.Contains(SelectedRecord) &&
        !SelectedRecord.IsPublished;
    public bool CanMoveToConfigured => ShowDualList && SelectedAvailableOption is not null;
    public bool CanMoveToAvailable => ShowDualList && SelectedConfiguredOption is not null;

    private bool IsDraftValid =>
        !string.IsNullOrWhiteSpace(DraftName) &&
        DraftName.Trim().Length <= 100 &&
        (!SelectedCategory.ShowCode || !SelectedCategory.CodeRequired || !string.IsNullOrWhiteSpace(DraftCode)) &&
        DraftCode.Trim().Length <= 100 &&
        (!SelectedCategory.ShowDescription || !SelectedCategory.DescriptionRequired || !string.IsNullOrWhiteSpace(DraftDescription)) &&
        (!NameUsesSelection || NameOptions.Contains(DraftName, StringComparer.Ordinal)) &&
        (!CodeUsesSelection || CodeOptions.Contains(DraftCode, StringComparer.Ordinal)) &&
        (!DescriptionUsesSelection || DescriptionOptions.Contains(DraftDescription, StringComparer.Ordinal)) &&
        EditorFields.All(field => !field.IsRequired || field.IsBoolean || !string.IsNullOrWhiteSpace(field.Value)) &&
        (!ShowPermissionChecklist || PermissionOptions.Any(option => option.IsSelected)) &&
        (!ShowDualList || ConfiguredOptions.Count > 0);

    private bool IsDraftUnique(int? excludedRecordId)
    {
        var draftControlName = SelectedCategory.Key == "permissions"
            ? EditorFields.Single(field => field.Key == "control-name").Value.Trim()
            : string.Empty;

        // 修改时按会话记录的稳定 Id 排除自身，并先确认 SelectedRecord 确实属于当前集合；
        // 这样即使外部错误传入一个“长得相同”的临时对象，也不能绕开重复校验。
        foreach (var record in Records.Where(record => record.Id != excludedRecordId))
        {
            var hasConflict = SelectedCategory.Key switch
            {
                // 用户可共享同一角色，因此只校验用户名；不能把“角色”误当作唯一代码。
                "users" or "roles" or "allocation" or "vehicle-types" => Same(record.Name, DraftName),
                // Legacy 权限编辑只约束权限代码和非空控件名称唯一，不凭空增加隐藏的“权限类型”。
                "permissions" =>
                    Same(record.Code, DraftCode) ||
                    (!string.IsNullOrWhiteSpace(draftControlName) &&
                     Same(record.Values.GetValueOrDefault("control-name", string.Empty), draftControlName)),
                // 型号/项点配置/试验参数均以车型和型号组合识别，允许不同车型复用型号名称。
                "models" or "item-config" or "test-params" =>
                    Same(record.Name, DraftName) && Same(record.Code, DraftCode),
                // 项点的代码字段是关联逻辑类，车型保存在说明字段；唯一键应是“车型 + 项点名称”。
                "test-items" =>
                    Same(record.Name, DraftName) &&
                    Same(record.Values.GetValueOrDefault("description", string.Empty), DraftDescription),
                _ => Same(record.Name, DraftName)
            };

            if (hasConflict)
                return false;
        }

        return true;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private void AddRecord()
    {
        var record = CreateRecord(_nextSessionId++, "会话内新增");
        Records.Add(record);
        SelectedRecord = record;
        _feedbackText = $"已在当前会话新增“{record.Name}”；未修改任何数据库或配置文件。";
        NotifyRecordMutation();
    }

    private void UpdateRecord()
    {
        if (SelectedRecord is null)
            return;

        var draft = CreateRecord(SelectedRecord.Id, SelectedRecord.Status);
        SelectedRecord.UpdateFrom(draft);
        _feedbackText = $"已在当前会话修改“{SelectedRecord.Name}”；未修改任何数据库或配置文件。";
        NotifyRecordMutation();
    }

    private void DeleteRecord()
    {
        if (SelectedRecord is null)
            return;
        var name = SelectedRecord.Name;
        var selectionKey = SelectedRecord.Id.ToString(CultureInfo.InvariantCulture);
        Records.Remove(SelectedRecord);

        if (SelectedCategory.Key == "permissions")
        {
            // Legacy 关系表以权限 Id 关联。这里同样按稳定 Id 级联，不能按显示名称删除，
            // 否则两个同名、不同权限代码的合法记录会互相误伤。
            var allocations = Categories.Single(category => category.Key == "allocation").Records;
            foreach (var allocation in allocations)
                allocation.RemoveSelection(selectionKey);
        }

        _selectedRecord = null;
        OnPropertyChanged(nameof(SelectedRecord));
        ResetDraft(clearSelections: true);
        _feedbackText = $"已从当前会话移除“{name}”；退出应用后不会保留。";
        NotifyRecordMutation();
    }

    private void PublishSelected()
    {
        if (!CanPublishSelected || SelectedRecord is null)
            return;

        // Legacy 发布会写数据库；此处只改变会话状态，明确避免把 UI 交互误当作正式发布。
        SelectedRecord.MarkPublished(DateTimeOffset.Now);
        _feedbackText = $"“{SelectedRecord.Name}”仅在当前会话标记为已发布；未写入型号数据库。";
        NotifyRecordMutation();
    }

    private void MoveToConfigured()
    {
        if (SelectedAvailableOption is null)
            return;
        var option = SelectedAvailableOption;
        AvailableOptions.Remove(option);
        ConfiguredOptions.Add(option);
        SelectedAvailableOption = null;
        SelectedConfiguredOption = option;
        RaiseOptionState();
    }

    private void MoveToAvailable()
    {
        if (SelectedConfiguredOption is null)
            return;
        var option = SelectedConfiguredOption;
        ConfiguredOptions.Remove(option);
        AvailableOptions.Add(option);
        SelectedConfiguredOption = null;
        SelectedAvailableOption = option;
        RaiseOptionState();
    }

    private ManagementRecordViewModel CreateRecord(int id, string status)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = DraftName.Trim(),
            ["code"] = DraftCode.Trim(),
            ["description"] = DraftDescription.Trim()
        };
        foreach (var field in EditorFields)
            values[field.Key] = field.SerializedValue;

        var selections = CurrentSelections();
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(DraftDescription))
            details.Add($"{DescriptionLabel}：{DraftDescription.Trim()}");
        details.AddRange(EditorFields.Select(field => $"{field.Label}：{field.DisplayValue}"));
        var selectionLabels = CurrentSelectionLabels();
        if (selectionLabels.Count > 0)
            details.Add($"{(ShowPermissionChecklist ? "已选权限" : "已选项点")}：{string.Join("、", selectionLabels)}");

        return new ManagementRecordViewModel(
            id,
            DraftName.Trim(),
            SelectedCategory.ShowCode ? DraftCode.Trim() : string.Empty,
            string.Join("；", details),
            status,
            values,
            selections);
    }

    private IReadOnlyList<string> CurrentSelections()
    {
        if (ShowPermissionChecklist)
            // 记录稳定权限 Id，显示名称只用于界面；Legacy 同名权限不能在分配时被合并。
            return PermissionOptions.Where(option => option.IsSelected).Select(option => option.Key).ToArray();
        if (ShowDualList)
            return ConfiguredOptions.Select(option => option.Key).ToArray();
        return [];
    }

    private IReadOnlyList<string> CurrentSelectionLabels()
    {
        if (ShowPermissionChecklist)
            return PermissionOptions.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
        if (ShowDualList)
            return ConfiguredOptions.Select(option => option.Name).ToArray();
        return [];
    }

    private void LoadRecordIntoEditor(ManagementRecordViewModel record)
    {
        _draftName = record.Values.GetValueOrDefault("name", record.Name);
        _draftCode = record.Values.GetValueOrDefault("code", record.Code);
        _draftDescription = record.Values.GetValueOrDefault("description", string.Empty);
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(DraftCode));
        OnPropertyChanged(nameof(DraftDescription));
        RefreshReferenceOptions();
        foreach (var field in EditorFields)
            field.Load(record.Values.GetValueOrDefault(field.Key, string.Empty));
        RestoreSelections(record.Selections);
    }

    private void RestoreSelections(IReadOnlyList<string> selections)
    {
        if (ShowPermissionChecklist)
        {
            // 候选权限只能来自仍存在的权限管理记录；旧分配中的失效 Id 不得反向造回候选。
            foreach (var option in PermissionOptions)
                option.IsSelected = selections.Contains(option.Key, StringComparer.Ordinal);
        }
        else if (ShowDualList)
        {
            var allOptions = AvailableOptions.Concat(ConfiguredOptions).Distinct().ToArray();
            AvailableOptions.Clear();
            ConfiguredOptions.Clear();
            foreach (var option in allOptions)
            {
                if (selections.Contains(option.Key, StringComparer.Ordinal))
                    ConfiguredOptions.Add(option);
                else
                    AvailableOptions.Add(option);
            }
        }
        RaiseOptionState();
    }

    private void RebuildEditorFields()
    {
        foreach (var field in EditorFields)
            field.PropertyChanged -= OnEditorFieldChanged;
        EditorFields.Clear();
        foreach (var definition in SelectedCategory.DetailFields)
        {
            var field = new ManagementFieldViewModel(definition);
            field.PropertyChanged += OnEditorFieldChanged;
            EditorFields.Add(field);
        }
        OnPropertyChanged(nameof(EditorFields));
        OnPropertyChanged(nameof(HasEditorFields));
    }

    private void RefreshReferenceOptions()
    {
        // Legacy 的角色、车型、型号都是下拉选择。当前未接外部数据库，因此候选项只来自本进程中
        // 已建立的角色/车型/型号记录；这既保留选择语义，也不会伪造数据库数据。
        var roleNames = Categories.Single(category => category.Key == "roles").Records.Select(record => record.Name);
        var vehicleTypeNames = Categories.Single(category => category.Key == "vehicle-types").Records.Select(record => record.Name);
        var modelRecords = Categories.Single(category => category.Key == "models").Records;

        IEnumerable<string> nameOptions = SelectedCategory.Key switch
        {
            "allocation" => roleNames,
            "item-config" or "test-params" => modelRecords
                // Legacy 的业务选择只返回已发布型号；管理页本身仍可查看未发布记录。
                .Where(record => record.IsPublished && Same(record.Code, DraftCode))
                .Select(record => record.Name),
            _ => []
        };
        IEnumerable<string> codeOptions = SelectedCategory.Key switch
        {
            "users" => roleNames,
            "models" or "item-config" or "test-params" => vehicleTypeNames,
            _ => []
        };
        IEnumerable<string> descriptionOptions = SelectedCategory.Key == "test-items"
            ? vehicleTypeNames
            : [];

        ReplaceOptions(NameOptions, nameOptions);
        ReplaceOptions(CodeOptions, codeOptions);
        ReplaceOptions(DescriptionOptions, descriptionOptions);
        SynchronizePermissionOptions();
        SynchronizeItemConfigurationOptions();
    }

    private void SynchronizePermissionOptions()
    {
        // 权限分配的候选项来自“权限管理”会话记录，界面不提供 Legacy 中不存在的临时造权限能力。
        // 选项 Key 使用会话记录 Id，Name 仅用于显示，从而完整保留 Legacy 允许的同名权限。
        var allocation = Categories.Single(category => category.Key == "allocation");
        var permissions = Categories.Single(category => category.Key == "permissions").Records
            .Select(record => new
            {
                Key = record.Id.ToString(CultureInfo.InvariantCulture),
                record.Name
            })
            .ToArray();

        foreach (var stale in allocation.PermissionOptions
                     .Where(option => permissions.All(permission => permission.Key != option.Key))
                     .ToArray())
        {
            stale.PropertyChanged -= OnOptionPropertyChanged;
            allocation.PermissionOptions.Remove(stale);
        }

        foreach (var permission in permissions)
        {
            var option = allocation.PermissionOptions.FirstOrDefault(candidate => candidate.Key == permission.Key);
            if (option is null)
            {
                option = new ManagementOptionViewModel(permission.Key, permission.Name);
                option.PropertyChanged += OnOptionPropertyChanged;
                allocation.PermissionOptions.Add(option);
            }
            else
            {
                option.UpdateName(permission.Name);
            }
        }

        RefreshPermissionAllocationDescriptions();
    }

    private void RefreshPermissionAllocationDescriptions()
    {
        var permissionNames = Categories.Single(category => category.Key == "permissions").Records
            .ToDictionary(
                record => record.Id.ToString(CultureInfo.InvariantCulture),
                record => record.Name,
                StringComparer.Ordinal);
        foreach (var allocation in Categories.Single(category => category.Key == "allocation").Records)
        {
            var labels = allocation.Selections
                .Select(key => permissionNames.GetValueOrDefault(key))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray();
            allocation.UpdateSelectionDescription("已选权限", labels);
        }
    }

    private void SynchronizeItemConfigurationOptions()
    {
        if (SelectedCategory.Key != "item-config")
            return;

        // 项点配置只能选择“项点管理”中同车型的会话记录；配置页自身不创造新项点。
        var names = Categories.Single(category => category.Key == "test-items").Records
            .Where(record => Same(record.Values.GetValueOrDefault("description", string.Empty), DraftCode))
            .Select(record => record.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var stale in ConfiguredOptions.Where(option => !names.Contains(option.Name, StringComparer.Ordinal)).ToArray())
        {
            stale.PropertyChanged -= OnOptionPropertyChanged;
            ConfiguredOptions.Remove(stale);
        }
        foreach (var stale in AvailableOptions.Where(option => !names.Contains(option.Name, StringComparer.Ordinal)).ToArray())
        {
            stale.PropertyChanged -= OnOptionPropertyChanged;
            AvailableOptions.Remove(stale);
        }
        foreach (var name in names.Where(name => ConfiguredOptions.Concat(AvailableOptions).All(option => option.Name != name)))
        {
            var option = new ManagementOptionViewModel(name);
            option.PropertyChanged += OnOptionPropertyChanged;
            AvailableOptions.Add(option);
        }
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string> source)
    {
        var normalized = source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (target.SequenceEqual(normalized, StringComparer.Ordinal))
            return;

        target.Clear();
        foreach (var value in normalized)
            target.Add(value);
    }

    private void ResetDraft(bool clearSelections)
    {
        _draftName = string.Empty;
        _draftCode = string.Empty;
        _draftDescription = string.Empty;
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(DraftCode));
        OnPropertyChanged(nameof(DraftDescription));
        foreach (var field in EditorFields)
            field.Reset();

        if (clearSelections)
        {
            foreach (var option in PermissionOptions)
                option.IsSelected = false;
            foreach (var option in ConfiguredOptions.ToArray())
            {
                ConfiguredOptions.Remove(option);
                AvailableOptions.Add(option);
            }
        }
        _selectedAvailableOption = null;
        _selectedConfiguredOption = null;
        OnPropertyChanged(nameof(SelectedAvailableOption));
        OnPropertyChanged(nameof(SelectedConfiguredOption));
        RefreshReferenceOptions();
        RaiseOptionState();
    }

    private void NotifyCategoryState()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(PermissionOptions));
        OnPropertyChanged(nameof(AvailableOptions));
        OnPropertyChanged(nameof(ConfiguredOptions));
        OnPropertyChanged(nameof(CategoryTitle));
        OnPropertyChanged(nameof(CategoryFields));
        OnPropertyChanged(nameof(CategoryEvidence));
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(CodeLabel));
        OnPropertyChanged(nameof(DescriptionLabel));
        OnPropertyChanged(nameof(ShowCode));
        OnPropertyChanged(nameof(ShowDescription));
        OnPropertyChanged(nameof(NameUsesSelection));
        OnPropertyChanged(nameof(CodeUsesSelection));
        OnPropertyChanged(nameof(DescriptionUsesSelection));
        OnPropertyChanged(nameof(ShowNameTextInput));
        OnPropertyChanged(nameof(ShowCodeTextInput));
        OnPropertyChanged(nameof(ShowDescriptionTextInput));
        OnPropertyChanged(nameof(ShowPermissionChecklist));
        OnPropertyChanged(nameof(ShowDualList));
        OnPropertyChanged(nameof(ShowPublishAction));
        OnPropertyChanged(nameof(CategoryBoundaryText));
        OnPropertyChanged(nameof(RecordSummary));
        RaiseCommandState();
    }

    private void NotifyRecordMutation()
    {
        SynchronizePermissionOptions();
        RefreshReferenceOptions();
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(RecordSummary));
        RaiseCommandState();
    }

    private void RaiseOptionState()
    {
        OnPropertyChanged(nameof(PermissionOptions));
        OnPropertyChanged(nameof(AvailableOptions));
        OnPropertyChanged(nameof(ConfiguredOptions));
        RaiseCommandState();
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanPublishSelected));
        OnPropertyChanged(nameof(CanMoveToConfigured));
        OnPropertyChanged(nameof(CanMoveToAvailable));
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        ((RelayCommand)UpdateCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PublishSelectedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveToConfiguredCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveToAvailableCommand).RaiseCanExecuteChanged();
    }

    private void OnEditorFieldChanged(object? sender, PropertyChangedEventArgs e) => RaiseCommandState();

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseCommandState();

    private static IEnumerable<ManagementCategoryViewModel> CreateCategories()
    {
        yield return new(
            "users", "用户管理", "用户名、角色（密码由认证迁移边界处理）", "Procedure/User/ucUserManager + frmUserEdit",
            "用户名", "角色", "", showCode: true, showDescription: false, codeRequired: true, descriptionRequired: false,
            codeUsesSelection: true);
        yield return new(
            "roles", "角色管理", "角色名称、角色描述", "Procedure/User/ucRole + frmRoleEdit",
            "角色名称", "", "角色描述", showCode: false, showDescription: true, codeRequired: false, descriptionRequired: true);
        yield return new(
            "permissions", "权限管理", "权限名称、权限代码、控件名称、权限备注", "Procedure/User/ucPermission + frmPermissionEdit",
            "权限名称", "权限代码", "权限备注", showCode: true, showDescription: true, codeRequired: true, descriptionRequired: false,
            // Legacy 只要求名称和代码；控件名称允许为空，但非空时必须保持唯一。
            [new("control-name", "控件名称", ManagementFieldKind.Text, false)]);
        yield return new(
            "allocation", "权限分配", "角色、权限勾选", "Procedure/User/ucPermissionAllocation",
            "角色", "", "", showCode: false, showDescription: false, codeRequired: false, descriptionRequired: false,
            usesPermissionChecklist: true, nameUsesSelection: true);
        yield return new(
            "vehicle-types", "车型管理", "车型名称、车型备注", "Procedure/ucKindManage + frmModelTypeEdit",
            "车型名称", "", "车型备注", showCode: false, showDescription: true, codeRequired: false, descriptionRequired: false);
        yield return new(
            "models", "型号管理", "车型、型号名称、型号描述、会话发布", "Procedure/ucModelManage + frmModelEdit",
            "型号名称", "车型", "型号描述", showCode: true, showDescription: true, codeRequired: true, descriptionRequired: false,
            codeUsesSelection: true);
        yield return new(
            "test-items", "项点管理", "车型、项点名称、关联逻辑类、启用状态", "Procedure/ucItemManagerial + frmTestProcess",
            "项点名称", "关联逻辑类名称", "车型", showCode: true, showDescription: true, codeRequired: true, descriptionRequired: true,
            [new("enabled", "启用状态", ManagementFieldKind.Boolean, false)], descriptionUsesSelection: true);
        yield return new(
            "item-config", "项点配置", "车型、型号、候选项点、已选项点", "Procedure/ucItemConfiguration",
            "型号", "车型", "", showCode: true, showDescription: false, codeRequired: true, descriptionRequired: false,
            usesDualList: true, nameUsesSelection: true, codeUsesSelection: true);
        yield return new(
            "test-params", "试验参数", "车型、型号、报表模板文件名、报表保存目录", "Procedure/ucTestParams",
            "型号", "车型", "", showCode: true, showDescription: false, codeRequired: true, descriptionRequired: false,
            [
                new("report-template", "报表模板文件名", ManagementFieldKind.Text, true),
                new("report-save-path", "报表保存目录", ManagementFieldKind.Text, true)
            ], nameUsesSelection: true, codeUsesSelection: true);
    }
}

public enum ManagementFieldKind
{
    Text,
    Boolean
}

public sealed record ManagementFieldDefinition(
    string Key,
    string Label,
    ManagementFieldKind Kind,
    bool IsRequired);

public sealed class ManagementFieldViewModel : ObservableObject
{
    private string _value = string.Empty;
    private bool _booleanValue;

    public ManagementFieldViewModel(ManagementFieldDefinition definition)
    {
        Definition = definition;
    }

    public ManagementFieldDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Label => Definition.Label;
    public bool IsRequired => Definition.IsRequired;
    public bool IsText => Definition.Kind == ManagementFieldKind.Text;
    public bool IsBoolean => Definition.Kind == ManagementFieldKind.Boolean;
    public string SerializedValue => IsBoolean ? BooleanValue.ToString() : Value.Trim();
    public string DisplayValue => IsBoolean ? (BooleanValue ? "启用" : "停用") : Value.Trim();

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set => SetProperty(ref _booleanValue, value);
    }

    public void Load(string value)
    {
        if (IsBoolean)
            BooleanValue = bool.TryParse(value, out var parsed) && parsed;
        else
            Value = value;
    }

    public void Reset()
    {
        Value = string.Empty;
        BooleanValue = false;
    }
}

public sealed class ManagementOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private string _name;

    public ManagementOptionViewModel(string name)
        : this(name, name)
    {
    }

    public ManagementOptionViewModel(string key, string name)
    {
        Key = key;
        _name = name;
    }

    public string Key { get; }
    public string Name => _name;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void UpdateName(string name)
    {
        SetProperty(ref _name, name, nameof(Name));
    }
}

public sealed class ManagementCategoryViewModel
{
    public ManagementCategoryViewModel(
        string key,
        string title,
        string fields,
        string legacyEvidence,
        string nameLabel,
        string codeLabel,
        string descriptionLabel,
        bool showCode,
        bool showDescription,
        bool codeRequired,
        bool descriptionRequired,
        IReadOnlyList<ManagementFieldDefinition>? detailFields = null,
        bool usesPermissionChecklist = false,
        bool usesDualList = false,
        bool nameUsesSelection = false,
        bool codeUsesSelection = false,
        bool descriptionUsesSelection = false)
    {
        Key = key;
        Title = title;
        Fields = fields;
        LegacyEvidence = legacyEvidence;
        NameLabel = nameLabel;
        CodeLabel = codeLabel;
        DescriptionLabel = descriptionLabel;
        ShowCode = showCode;
        ShowDescription = showDescription;
        CodeRequired = codeRequired;
        DescriptionRequired = descriptionRequired;
        DetailFields = detailFields ?? [];
        UsesPermissionChecklist = usesPermissionChecklist;
        UsesDualList = usesDualList;
        NameUsesSelection = nameUsesSelection;
        CodeUsesSelection = codeUsesSelection;
        DescriptionUsesSelection = descriptionUsesSelection;
    }

    public string Key { get; }
    public string Title { get; }
    public string Fields { get; }
    public string LegacyEvidence { get; }
    public string NameLabel { get; }
    public string CodeLabel { get; }
    public string DescriptionLabel { get; }
    public bool ShowCode { get; }
    public bool ShowDescription { get; }
    public bool CodeRequired { get; }
    public bool DescriptionRequired { get; }
    public IReadOnlyList<ManagementFieldDefinition> DetailFields { get; }
    public bool UsesPermissionChecklist { get; }
    public bool UsesDualList { get; }
    public bool NameUsesSelection { get; }
    public bool CodeUsesSelection { get; }
    public bool DescriptionUsesSelection { get; }
    public ObservableCollection<ManagementRecordViewModel> Records { get; } = [];
    public ObservableCollection<ManagementOptionViewModel> PermissionOptions { get; } = [];
    public ObservableCollection<ManagementOptionViewModel> AvailableOptions { get; } = [];
    public ObservableCollection<ManagementOptionViewModel> ConfiguredOptions { get; } = [];
}

public sealed class ManagementRecordViewModel : ObservableObject
{
    private string _name;
    private string _code;
    private string _description;
    private string _status;
    private Dictionary<string, string> _values;
    private IReadOnlyList<string> _selections;

    public ManagementRecordViewModel(
        int id,
        string name,
        string code,
        string description,
        string status,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> selections)
    {
        Id = id;
        _name = name;
        _code = code;
        _description = description;
        _status = status;
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);
        _selections = selections.ToArray();
    }

    public int Id { get; }
    public string Name => _name;
    public string Code => _code;
    public string Description => _description;
    public string Status => _status;
    public bool IsPublished => _status.StartsWith("会话内已发布", StringComparison.Ordinal);
    public IReadOnlyDictionary<string, string> Values => _values;
    public IReadOnlyList<string> Selections => _selections;

    public void UpdateFrom(ManagementRecordViewModel draft)
    {
        _name = draft.Name;
        _code = draft.Code;
        _description = draft.Description;
        _values = new Dictionary<string, string>(draft.Values, StringComparer.Ordinal);
        _selections = draft.Selections.ToArray();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(Selections));
    }

    public void MarkPublished(DateTimeOffset publishedAt)
    {
        _status = $"会话内已发布 {publishedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsPublished));
    }

    public void RemoveSelection(string selection)
    {
        var remaining = _selections
            .Where(value => !string.Equals(value, selection, StringComparison.Ordinal))
            .ToArray();
        if (remaining.Length == _selections.Count)
            return;

        _selections = remaining;
        OnPropertyChanged(nameof(Selections));
    }

    public void UpdateSelectionDescription(string label, IReadOnlyList<string> displayValues)
    {
        var description = displayValues.Count == 0
            ? $"{label}：—"
            : $"{label}：{string.Join("、", displayValues)}";
        if (_description == description)
            return;

        _description = description;
        OnPropertyChanged(nameof(Description));
    }
}
