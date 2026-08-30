using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 把 Legacy 的车型、产品信息和具体试验类重组为作业状态；设备动作仍由 Core/Gateway 门禁阻断。
/// </summary>
public sealed class TestOperationViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _owner;
    private string _selectedVehicleType = "压力调整阀 B11";
    private string _selectedModel = "B11";
    private string _productNumber = string.Empty;
    private string _vehicleNumber = string.Empty;
    private string _remarks = string.Empty;
    private string _feedbackText = "请先进入仿真会话，再提交本次试验产品信息。";

    public TestOperationViewModel(MainWindowViewModel owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _owner.PropertyChanged += OnOwnerPropertyChanged;

        AvailableVehicleTypes = new ReadOnlyCollection<string>(
            ["压力调整阀 B11", "电空变换阀 EP", "测试类型"]);
        AvailableModels = new ReadOnlyCollection<string>(["B11", "EP", "CS/测试"]);
        TestItems = new ObservableCollection<TestItemViewModel>(CreateLegacyTestItems());
        SubmitProductCommand = new RelayCommand(SubmitProduct, () => CanSubmitProduct);
        SelectVisibleCommand = new RelayCommand(() => SetVisibleSelection(true));
        ClearSelectionCommand = new RelayCommand(() => SetVisibleSelection(false));
        // 作业页额外要求至少选中一个可见项点；包装 Core 命令，防止未来 Test00
        // 接入后通过代码直接 Execute 绕过页面自己的项点门禁。
        StartAutomaticTestCommand = new RelayCommand(
            () => _owner.StartAutomaticTestCommand.Execute(null),
            () => CanStartAutomaticTest);

        // 命令必须先于项点事件订阅完成初始化，因为首轮可见性计算本身会触发
        // PropertyChanged，并同步刷新命令的 CanExecute 状态。
        foreach (var item in TestItems)
            item.PropertyChanged += OnTestItemPropertyChanged;
        UpdateItemVisibility();
    }

    public IReadOnlyList<string> AvailableVehicleTypes { get; }
    public IReadOnlyList<string> AvailableModels { get; }
    public ObservableCollection<TestItemViewModel> TestItems { get; }
    public ICommand SubmitProductCommand { get; }
    public ICommand SelectVisibleCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand StartAutomaticTestCommand { get; }
    public ICommand StopCommand => _owner.StopCommand;

    public string SelectedVehicleType
    {
        get => _selectedVehicleType;
        set
        {
            if (!SetProperty(ref _selectedVehicleType, value))
                return;
            UpdateItemVisibility();
            RaiseProductState();
        }
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
                RaiseProductState();
        }
    }

    public string ProductNumber
    {
        get => _productNumber;
        set
        {
            if (SetProperty(ref _productNumber, value))
                RaiseProductState();
        }
    }

    public string VehicleNumber
    {
        get => _vehicleNumber;
        set
        {
            if (SetProperty(ref _vehicleNumber, value))
                RaiseProductState();
        }
    }

    public string Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public string FeedbackText => _feedbackText;
    public string BenchStatusText => _owner.StatusText;
    public string TestModeText => _owner.TestModeText;
    public string SafetyText => _owner.SafetyText;
    public string ControlBoundaryText =>
        "当前只迁移作业界面与状态合同；Test00、手动输出和自动试验设备动作继续禁用，不创建离线写队列。";
    public int SelectedItemCount => TestItems.Count(item => item.IsVisible && item.IsSelected);
    public string SelectionSummary => $"已选择 {SelectedItemCount} 个可见项点";
    public bool CanSubmitProduct =>
        _owner.CanSelectProduct &&
        !string.IsNullOrWhiteSpace(SelectedVehicleType) &&
        !string.IsNullOrWhiteSpace(SelectedModel) &&
        !string.IsNullOrWhiteSpace(ProductNumber) &&
        ProductNumber.Trim().Length <= 128 &&
        !string.IsNullOrWhiteSpace(VehicleNumber);
    public bool CanStartAutomaticTest => _owner.CanStartAutomaticTest && SelectedItemCount > 0;
    public bool CanStop => _owner.CanStop;

    public void Dispose()
    {
        _owner.PropertyChanged -= OnOwnerPropertyChanged;
        foreach (var item in TestItems)
            item.PropertyChanged -= OnTestItemPropertyChanged;
    }

    private void SubmitProduct()
    {
        // Core 当前只有单一 ProductId 合同；界面保留 Legacy 的产品编号、车号和备注，
        // 但只把产品编号提交给 Core，避免在数据库边界完成前伪造型号持久化。
        _owner.ProductId = ProductNumber.Trim();
        _owner.SelectProductCommand.Execute(null);
        _feedbackText = $"已提交本次产品：{SelectedVehicleType} / {SelectedModel} / {ProductNumber.Trim()} / 车号 {VehicleNumber.Trim()}。";
        OnPropertyChanged(nameof(FeedbackText));
        RaiseProductState();
    }

    private void SetVisibleSelection(bool selected)
    {
        foreach (var item in TestItems.Where(item => item.IsVisible))
            item.IsSelected = selected;
        RaiseSelectionState();
    }

    private void UpdateItemVisibility()
    {
        var group = SelectedVehicleType switch
        {
            "压力调整阀 B11" => "B11",
            "电空变换阀 EP" => "EP",
            _ => "TEST"
        };
        foreach (var item in TestItems)
            item.IsVisible = item.Group == group;
        RaiseSelectionState();
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.StatusText)
            or nameof(MainWindowViewModel.TestModeText)
            or nameof(MainWindowViewModel.SafetyText)
            or nameof(MainWindowViewModel.CanSelectProduct)
            or nameof(MainWindowViewModel.CanStartAutomaticTest)
            or nameof(MainWindowViewModel.CanStop))
        {
            OnPropertyChanged(nameof(BenchStatusText));
            OnPropertyChanged(nameof(TestModeText));
            OnPropertyChanged(nameof(SafetyText));
            OnPropertyChanged(nameof(CanStartAutomaticTest));
            OnPropertyChanged(nameof(CanStop));
            ((RelayCommand)StartAutomaticTestCommand).RaiseCanExecuteChanged();
            RaiseProductState();
        }
    }

    private void OnTestItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestItemViewModel.IsSelected) or nameof(TestItemViewModel.IsVisible))
            RaiseSelectionState();
    }

    private void RaiseProductState()
    {
        OnPropertyChanged(nameof(CanSubmitProduct));
        ((RelayCommand)SubmitProductCommand).RaiseCanExecuteChanged();
    }

    private void RaiseSelectionState()
    {
        OnPropertyChanged(nameof(SelectedItemCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanStartAutomaticTest));
        ((RelayCommand)StartAutomaticTestCommand).RaiseCanExecuteChanged();
    }

    private static IEnumerable<TestItemViewModel> CreateLegacyTestItems()
    {
        yield return new("B11", "容量试验", "B11_CapacityTest");
        yield return new("B11", "高压泄漏试验", "B11_HighLeakTest");
        yield return new("B11", "高压灵敏度试验", "B11_HighVoltageSensitivityTest");
        yield return new("B11", "低压泄漏试验", "B11_LowLeakTest");
        yield return new("B11", "低压灵敏度试验", "B11_LowVoltageSensitivityTest");
        yield return new("B11", "型面压力调整", "B11_ProfilePressureTest");
        yield return new("B11", "调压试验", "B11_VoltageSideTest");
        yield return new("EP", "供给阀试验", "EP_SupplyValveTest");
        yield return new("EP", "试验准备", "EP_PrepareTest");
        yield return new("EP", "重叠位置试验", "EP_OverlapPositionTest");
        yield return new("EP", "滞后试验", "EP_LagTest");
        yield return new("EP", "绝缘试验", "EP_InsulationTest");
        yield return new("EP", "输入电流试验", "EP_InputCurrentTest");
        yield return new("EP", "排气阀试验", "EP_ExhaustValveTest");
        yield return new("EP", "作用试验", "EP_EffectTest");
        yield return new("EP", "阀升程调整", "EP_DetermineTest");
        yield return new("EP", "容量试验", "EP_CapacityTest");
        yield return new("TEST", "测试项 001", "Test001");
        yield return new("TEST", "测试项 002", "Test002");
        yield return new("TEST", "CS 测试 01", "CS_Test01");
        yield return new("TEST", "CS 测试 02", "CS_Test02");
    }
}

public sealed class TestItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible;

    public TestItemViewModel(string group, string name, string legacyClassName)
    {
        Group = group;
        Name = name;
        LegacyClassName = legacyClassName;
    }

    public string Group { get; }
    public string Name { get; }
    public string LegacyClassName { get; }
    public string StatusText => "未执行";
    public string ResultText => "—";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
