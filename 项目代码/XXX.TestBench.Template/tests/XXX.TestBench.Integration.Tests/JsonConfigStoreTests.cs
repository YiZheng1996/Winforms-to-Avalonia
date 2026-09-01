using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public async Task LoadAndValidate_Works()
    {
        using var env = TestEnv.Create();
        var store = new JsonConfigStore(env.ConfigRoot);
        var app = await store.LoadAsync<AppConfig>("app.json");
        app.Validate();
        Assert.Equal("测试系统", app.SystemName);

        var device = await store.LoadAsync<DeviceConfig>("device.json");
        device.Validate();
        Assert.Equal(Core.Domain.Devices.DeviceMode.Simulation, device.DeviceMode);
    }

    [Fact]
    public async Task InvalidSchemaVersion_ThrowsConfigValidation()
    {
        using var env = TestEnv.Create();
                File.WriteAllText(Path.Combine(env.ConfigRoot, "app.json"), """
        {
          "schemaVersion": 99,
          "systemName": "x",
          "brand": "b",
          "language": "zh-CN",
          "defaultPaths": { "database": "t.db", "reportOutput": "r", "reportTemplates": "t" }
        }
        """);
        var store = new JsonConfigStore(env.ConfigRoot);
        var app = await store.LoadAsync<AppConfig>("app.json");
        Assert.Throws<ConfigValidationException>(() => app.Validate());
    }

    [Fact]
    public async Task SaveAtomic_CreatesBackup_AndRoundTrips()
    {
        using var env = TestEnv.Create();
        var store = new JsonConfigStore(env.ConfigRoot);
        var original = await store.LoadAsync<AppConfig>("app.json");

        original.SystemName = "改名后的系统";
        await store.SaveAsync("app.json", original);

        var reloaded = await store.LoadAsync<AppConfig>("app.json");
        Assert.Equal("改名后的系统", reloaded.SystemName);
        Assert.True(File.Exists(Path.Combine(env.ConfigRoot, "app.json.bak")));
    }
}
