using XXX.TestBench.App.ViewModels;
using Xunit;

namespace XXX.TestBench.App.Tests;

public class ParameterValueDialogViewModelTests
{
    [Fact]
    public void ProjectParameterDialog_RequiresInteger()
    {
        var vm = new ParameterValueDialogViewModel(
            "编辑项目级参数", "修改后生效", "试验时间", "s", "1～3600", "60.5", integerOnly: true);

        Assert.False(vm.TryBuildResult(out _));
        Assert.Equal("参数值必须为整数", vm.ValidationMessage);
    }

    [Fact]
    public void NumericParameterDialog_ReturnsTrimmedValue()
    {
        var vm = new ParameterValueDialogViewModel(
            "编辑产品参数", "修改后生效", "保护电流", "mA", "0～1000", " 100 ", integerOnly: false);

        Assert.True(vm.TryBuildResult(out var value));
        Assert.Equal("100", value);
        Assert.Empty(vm.ValidationMessage);
    }

    [Fact]
    public void ProductParameterDialog_ReturnsBothValues()
    {
        var vm = new ProductParameterDialogViewModel(
            "编辑产品参数", "产品类型 + 产品型号", " 5000 ", " 100 ");

        Assert.True(vm.TryBuildResult(out var result));
        Assert.Equal("5000", result.TestVoltage);
        Assert.Equal("100", result.ProtectCurrent);
    }

    [Fact]
    public void ProductParameterDialog_RequiresBothNumericValues()
    {
        var vm = new ProductParameterDialogViewModel(
            "编辑产品参数", "产品类型 + 产品型号", "5000", "abc");

        Assert.False(vm.TryBuildResult(out _));
        Assert.Equal("保护电流必须为数值", vm.ValidationMessage);
    }
}
