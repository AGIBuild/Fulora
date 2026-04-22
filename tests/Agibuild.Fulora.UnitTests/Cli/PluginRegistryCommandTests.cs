using System.Net;
using System.Net.Http;
using Agibuild.Fulora.Cli.Commands;
using Xunit;

namespace Agibuild.Fulora.UnitTests.Cli;

public class PluginRegistryCommandTests
{
    [Fact]
    public async Task SearchCommand_formats_NuGet_results_correctly()
    {
        var json = """
            {"totalHits":2,"data":[
                {"id":"Agibuild.Fulora.Plugin.Database","version":"1.0.0","description":"Database plugin","tags":["fulora-plugin","database"]},
                {"id":"Agibuild.Fulora.Plugin.HttpClient","version":"2.0.0","description":"HTTP client plugin for Fulora","tags":["fulora","http"]}
            ]}
            """;

        var handler = new MockHttpMessageHandler(json);
        using var client = new HttpClient(handler);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = await SearchCommand.ExecuteAsync("database", 20, CancellationToken.None, client);
            Assert.Equal(0, exitCode);

            var output = sw.ToString();
            Assert.Contains("Agibuild.Fulora.Plugin.Database", output);
            Assert.Contains("Agibuild.Fulora.Plugin.HttpClient", output);
            Assert.Contains("1.0.0", output);
            Assert.Contains("2.0.0", output);
            Assert.Contains("Database plugin", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task SearchCommand_shows_no_plugins_found_when_empty()
    {
        var json = """{"totalHits":0,"data":[]}""";
        var handler = new MockHttpMessageHandler(json);
        using var client = new HttpClient(handler);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = await SearchCommand.ExecuteAsync(null, 20, CancellationToken.None, client);
            Assert.Equal(0, exitCode);
            Assert.Contains("No plugins found", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void ListPluginsCommand_parses_csproj_PackageReferences()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Agibuild.Fulora.Plugin.Database" Version="1.0.0" />
                <PackageReference Include="Agibuild.Fulora.Plugin.HttpClient" Version="2.1.0" />
                <PackageReference Include="Other.Package" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "fulora-cli-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, csproj);

        try
        {
            var plugins = ListPluginsCommand.GetFuloraPluginsFromCsproj(csprojPath);
            Assert.Equal(2, plugins.Count);
            Assert.Contains(plugins, p => p.PackageId == "Agibuild.Fulora.Plugin.Database" && p.Version == "1.0.0");
            Assert.Contains(plugins, p => p.PackageId == "Agibuild.Fulora.Plugin.HttpClient" && p.Version == "2.1.0");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ListPluginsCommand_parses_version_from_child_element()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Agibuild.Fulora.Plugin.Database">
                  <Version>3.0.0-preview</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "fulora-cli-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, csproj);

        try
        {
            var plugins = ListPluginsCommand.GetFuloraPluginsFromCsproj(csprojPath);
            Assert.Single(plugins);
            Assert.Equal("3.0.0-preview", plugins[0].Version);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddPluginCommand_constructs_correct_dotnet_command()
    {
        var args = AddPluginCommand.GetDotnetAddPackageArguments("/path/to/proj.csproj", "Agibuild.Fulora.Plugin.Database");
        Assert.Equal("add \"/path/to/proj.csproj\" package \"Agibuild.Fulora.Plugin.Database\"", args);
    }

    [Fact]
    public void AddPluginCommand_DeriveNpmPackageName_returns_correct_format()
    {
        Assert.Equal("@agibuild/fulora-plugin-database", AddPluginCommand.DeriveNpmPackageName("Agibuild.Fulora.Plugin.Database"));
        Assert.Equal("@agibuild/fulora-plugin-http-client", AddPluginCommand.DeriveNpmPackageName("Agibuild.Fulora.Plugin.HttpClient"));
    }

    [Fact]
    public void AddPluginCommand_DeriveNpmPackageName_returns_null_for_non_plugin()
    {
        Assert.Null(AddPluginCommand.DeriveNpmPackageName("Other.Package"));
        Assert.Null(AddPluginCommand.DeriveNpmPackageName(""));
        Assert.Null(AddPluginCommand.DeriveNpmPackageName("   "));
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public MockHttpMessageHandler(string responseJson) => _responseJson = responseJson;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
