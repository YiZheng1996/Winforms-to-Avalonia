using Avalonia.Controls;

namespace XXX.TestBench.Avalonia.Views;

// 这些 View 只负责加载 XAML；业务状态和命令均由对应 ViewModel 持有。
public partial class TestOperationView : UserControl
{
    public TestOperationView() => InitializeComponent();
}

public partial class ProcessOverviewView : UserControl
{
    public ProcessOverviewView() => InitializeComponent();
}

public partial class ManagementView : UserControl
{
    public ManagementView() => InitializeComponent();
}

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();
}

public partial class CalibrationView : UserControl
{
    public CalibrationView() => InitializeComponent();
}

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => InitializeComponent();
}

public partial class InstrumentView : UserControl
{
    public InstrumentView() => InitializeComponent();
}
