using Agibuild.Fulora.Plugin.LocalStorage;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class LocalStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public LocalStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fulora-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "local-storage.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private LocalStorageService CreateService() => new(_filePath);

    [Fact]
    public async Task Get_NonExistentKey_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(await svc.GetValue("unknown"));
    }

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        var svc = CreateService();
        await svc.SetValue("theme", "dark");
        Assert.Equal("dark", await svc.GetValue("theme"));
    }

    [Fact]
    public async Task Set_OverwritesExistingKey()
    {
        var svc = CreateService();
        await svc.SetValue("lang", "en");
        await svc.SetValue("lang", "zh");
        Assert.Equal("zh", await svc.GetValue("lang"));
    }

    [Fact]
    public async Task Remove_ExistingKey()
    {
        var svc = CreateService();
        await svc.SetValue("k", "v");
        await svc.Remove("k");
        Assert.Null(await svc.GetValue("k"));
    }

    [Fact]
    public async Task Remove_NonExistentKey_NoOp()
    {
        var svc = CreateService();
        await svc.Remove("nope");
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        var svc = CreateService();
        await svc.SetValue("a", "1");
        await svc.SetValue("b", "2");
        await svc.Clear();
        Assert.Null(await svc.GetValue("a"));
        Assert.Null(await svc.GetValue("b"));
        Assert.Empty(await svc.GetKeys());
    }

    [Fact]
    public async Task GetKeys_ReturnsAllKeys()
    {
        var svc = CreateService();
        await svc.SetValue("x", "1");
        await svc.SetValue("y", "2");
        await svc.SetValue("z", "3");
        var keys = await svc.GetKeys();
        Assert.Equal(3, keys.Length);
        Assert.Contains("x", keys);
        Assert.Contains("y", keys);
        Assert.Contains("z", keys);
    }

    [Fact]
    public async Task GetKeys_Empty_ReturnsEmpty()
    {
        var svc = CreateService();
        Assert.Empty(await svc.GetKeys());
    }

    [Fact]
    public async Task Persistence_AcrossServiceRestart()
    {
        var svc1 = CreateService();
        await svc1.SetValue("theme", "dark");
        await svc1.SetValue("lang", "zh");

        var svc2 = CreateService();
        Assert.Equal("dark", await svc2.GetValue("theme"));
        Assert.Equal("zh", await svc2.GetValue("lang"));
    }

    [Fact]
    public async Task Persistence_RemoveThenRestart()
    {
        var svc1 = CreateService();
        await svc1.SetValue("a", "1");
        await svc1.SetValue("b", "2");
        await svc1.Remove("a");

        var svc2 = CreateService();
        Assert.Null(await svc2.GetValue("a"));
        Assert.Equal("2", await svc2.GetValue("b"));
    }

    [Fact]
    public async Task Persistence_ClearThenRestart()
    {
        var svc1 = CreateService();
        await svc1.SetValue("x", "1");
        await svc1.Clear();

        var svc2 = CreateService();
        Assert.Empty(await svc2.GetKeys());
    }

    [Fact]
    public void Constructor_CorruptJsonFile_RecoveryGracefully()
    {
        File.WriteAllText(_filePath, "not-json!!!");
        var svc = CreateService();
        var keys = svc.GetKeys().Result;
        Assert.Empty(keys);
    }

    [Fact]
    public async Task Set_EmptyValue_Works()
    {
        var svc = CreateService();
        await svc.SetValue("empty", "");
        Assert.Equal("", await svc.GetValue("empty"));
    }

    [Fact]
    public async Task Set_UnicodeValue_Persists()
    {
        var svc = CreateService();
        await svc.SetValue("greeting", "你好世界 🌍");

        var svc2 = CreateService();
        Assert.Equal("你好世界 🌍", await svc2.GetValue("greeting"));
    }
}
