using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointDialogViewModelTests
{
    [Fact]
    public void NewDialog_UsesCustomerChoicesAndSafeReadOnlyDefault()
    {
        var vm = new DevicePointDialogViewModel(false, null);

        Assert.Contains(vm.DataTypeOptions, option => option.Value == DevicePointDataType.Boolean && option.DisplayName.Contains("开关量"));
        Assert.Equal(DevicePointProtocol.Simulation, vm.SelectedProtocolOption?.Value);
        Assert.Equal(DevicePointDataType.Decimal, vm.SelectedDataTypeOption?.Value);
        Assert.Equal("ReadOnly", vm.SelectedWritePolicy?.Key);
        Assert.False(vm.IsWritable);
    }

    [Fact]
    public void HighRiskPolicy_ProducesExplicitWriteRiskResult()
    {
        var vm = new DevicePointDialogViewModel(false, null)
        {
            Code = "DO_Start",
            Name = "启动命令",
            Address = "sim.start"
        };
        vm.SelectedWritePolicy = vm.WritePolicyOptions.Single(option => option.Key == "HighRiskWritable");

        var success = vm.TryBuildResult(out var result);

        Assert.True(success);
        Assert.True(result.IsWritable);
        Assert.Equal(WriteRiskLevel.HighRisk, result.RiskLevel);
        Assert.Contains("二次确认", vm.WriteSafetyMessage, StringComparison.Ordinal);
    }
}
