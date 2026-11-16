using CommunityToolkit.Mvvm.ComponentModel;
namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 记录列表中的一行，其中结论可被界面修改。
/// </summary>
public sealed partial class RecordRow : ObservableObject
{
    /// <summary>
    /// 记录编号。
    /// </summary>
    public required int Id { get; init; }
    /// <summary>
    /// 记录流水号。
    /// </summary>
    public required string RecordNumber { get; init; }
    /// <summary>
    /// 产品编号。
    /// </summary>
    public required string ProductNumber { get; init; }
    /// <summary>
    /// 产品型号名称。
    /// </summary>
    public required string ProductModelName { get; init; }
    /// <summary>
    /// 记录状态文字。
    /// </summary>
    public required string StateText { get; init; }
    /// <summary>
    /// 设备模式文字。
    /// </summary>
    public required string DeviceModeText { get; init; }
    /// <summary>
    /// 开始时间文字。
    /// </summary>
    public required string StartedAt { get; init; }

    /// <summary>
    /// 记录结论文字。
    /// </summary>
    [ObservableProperty]
    private string _conclusion = string.Empty;
}
