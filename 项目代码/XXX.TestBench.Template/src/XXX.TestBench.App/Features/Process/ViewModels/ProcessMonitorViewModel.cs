using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 工艺监控页面的兼容外壳。具体刷新、绑定和写入职责分拆在同名 partial 文件中。
/// </summary>
public sealed partial class ProcessMonitorViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;
    private readonly IProcessSnapshotReader _snapshotReader;

    public ProcessMonitorViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        _snapshotReader = new ProcessSnapshotReader();
        BuildProcessPointModels();
        ApplyRuntimePresentationMode(_services.DeviceModes.Runtime);
    }

    public override string Title => "工艺监控";

}
