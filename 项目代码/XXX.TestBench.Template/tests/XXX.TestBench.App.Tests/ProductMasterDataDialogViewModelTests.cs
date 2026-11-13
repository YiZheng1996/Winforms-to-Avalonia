using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Products;
using Xunit;

namespace XXX.TestBench.App.Tests;

/// <summary>
/// 产品类型/型号新增与编辑共用的弹窗表单状态测试。
/// </summary>
public class ProductMasterDataDialogViewModelTests
{
    private static IEnumerable<ProductType> Types()
    {
        yield return new ProductType { Id = 1, Name = "压力试验" };
        yield return new ProductType { Id = 2, Name = "绝缘试验", IsEnabled = false };
    }

    [Fact]
    public void AddTypeDialog_DefaultsToAddTitle()
    {
        var vm = new ProductMasterDataDialogViewModel(false, Types());

        Assert.Equal("新增产品类型", vm.DialogTitle);
        Assert.False(vm.IsEdit);
        Assert.True(vm.IsTypeDialog);
        Assert.Equal(string.Empty, vm.Name);
    }

    [Fact]
    public void EditTypeDialog_PrefillsName()
    {
        var vm = new ProductMasterDataDialogViewModel(false, Types(), isEdit: true, name: "压力试验");

        Assert.Equal("编辑产品类型", vm.DialogTitle);
        Assert.True(vm.IsEdit);
        Assert.Equal("压力试验", vm.Name);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void EditModelDialog_PreselectsType_AndLocksTypeSelection()
    {
        var vm = new ProductMasterDataDialogViewModel(true, Types(), isEdit: true, name: "型号1", typeId: 1);

        Assert.Equal("编辑产品型号", vm.DialogTitle);
        Assert.Equal("型号1", vm.Name);
        Assert.NotNull(vm.SelectedType);
        Assert.Equal(1, vm.SelectedType!.Id);
        Assert.False(vm.CanSelectType);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void AddModelDialog_CanSelectType()
    {
        var vm = new ProductMasterDataDialogViewModel(true, Types());

        Assert.True(vm.CanSelectType);
        Assert.False(vm.CanSave);
        Assert.False(vm.TryBuildResult(out _));
    }
}