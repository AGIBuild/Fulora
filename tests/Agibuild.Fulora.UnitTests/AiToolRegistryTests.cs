using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiToolRegistryTests
{
    [Fact]
    public void Register_discovers_AiTool_methods_on_interface()
    {
        var registry = new AiToolRegistry();
        var service = new TestToolService();
        registry.Register(service);

        Assert.True(registry.Tools.Count >= 2);
    }

    [Fact]
    public void FindTool_returns_registered_tool()
    {
        var registry = new AiToolRegistry();
        registry.Register(new TestToolService());

        var tool = registry.FindTool("GetWeather");

        Assert.NotNull(tool);
        Assert.Equal("GetWeather", tool.Name);
    }

    [Fact]
    public void FindTool_returns_null_for_unknown()
    {
        var registry = new AiToolRegistry();
        registry.Register(new TestToolService());

        var tool = registry.FindTool("NonExistent");

        Assert.Null(tool);
    }

    [Fact]
    public async Task Registered_tool_can_be_invoked()
    {
        var registry = new AiToolRegistry();
        registry.Register(new TestToolService());

        var tool = registry.FindTool("GetWeather");
        Assert.NotNull(tool);

        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["city"] = "Tokyo"
        }));

        Assert.NotNull(result);
        Assert.Contains("Tokyo", result.ToString());
    }

    [Fact]
    public void Register_discovers_methods_with_AiTool_on_class()
    {
        var registry = new AiToolRegistry();
        registry.Register(new DirectToolClass());

        Assert.Single(registry.Tools);
        Assert.NotNull(registry.FindTool("Calculate"));
    }

    [AiTool]
    public interface ITestToolService
    {
        /// <summary>Gets weather for a city.</summary>
        string GetWeather(string city);

        /// <summary>Gets time for a timezone.</summary>
        string GetTime(string timezone);
    }

    private sealed class TestToolService : ITestToolService
    {
        public string GetWeather(string city) => $"Sunny in {city}";
        public string GetTime(string timezone) => $"12:00 in {timezone}";
    }

    private sealed class DirectToolClass
    {
        [AiTool]
        public int Calculate(int a, int b) => a + b;
    }
}
