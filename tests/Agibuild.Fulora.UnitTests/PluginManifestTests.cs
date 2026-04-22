using Agibuild.Fulora;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class PluginManifestTests
{
    private const string ValidJson = """
        {
          "id": "Agibuild.Fulora.Plugin.Database",
          "displayName": "Database (SQLite)",
          "services": ["DatabaseService"],
          "npmPackage": "@agibuild/fulora-plugin-database",
          "minFuloraVersion": "1.0.0",
          "platforms": ["windows", "macos", "linux"]
        }
        """;

    private const string MinimalJson = """
        {
          "id": "Test.Plugin",
          "displayName": "Test",
          "services": ["SomeService"],
          "minFuloraVersion": "2.0.0"
        }
        """;

    [Fact]
    public void Parse_ValidJson_AllFieldsPopulated()
    {
        var m = PluginManifest.Parse(ValidJson);

        Assert.NotNull(m);
        Assert.Equal("Agibuild.Fulora.Plugin.Database", m.Id);
        Assert.Equal("Database (SQLite)", m.DisplayName);
        Assert.Single(m.Services, "DatabaseService");
        Assert.Equal("@agibuild/fulora-plugin-database", m.NpmPackage);
        Assert.Equal("1.0.0", m.MinFuloraVersion);
        Assert.NotNull(m.Platforms);
        Assert.Equal(3, m.Platforms.Length);
    }

    [Fact]
    public void Parse_MinimalJson_OptionalFieldsNull()
    {
        var m = PluginManifest.Parse(MinimalJson);

        Assert.NotNull(m);
        Assert.Equal("Test.Plugin", m.Id);
        Assert.Null(m.NpmPackage);
        Assert.Null(m.Platforms);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(PluginManifest.Parse(null!));
        Assert.Null(PluginManifest.Parse(""));
        Assert.Null(PluginManifest.Parse("   "));
    }

    [Fact]
    public void IsCompatibleWith_VersionMeetsMinimum_ReturnsTrue()
    {
        var m = PluginManifest.Parse(ValidJson)!;
        Assert.True(m.IsCompatibleWith(new Version(1, 0, 0)));
        Assert.True(m.IsCompatibleWith(new Version(1, 1, 0)));
        Assert.True(m.IsCompatibleWith(new Version(2, 0, 0)));
    }

    [Fact]
    public void IsCompatibleWith_VersionBelowMinimum_ReturnsFalse()
    {
        var m = PluginManifest.Parse(ValidJson)!;
        Assert.False(m.IsCompatibleWith(new Version(0, 9, 0)));
        Assert.False(m.IsCompatibleWith(new Version(0, 0, 1)));
    }

    [Fact]
    public void IsCompatibleWith_NullVersion_ThrowsArgumentNull()
    {
        var m = PluginManifest.Parse(ValidJson)!;
        Assert.Throws<ArgumentNullException>(() => m.IsCompatibleWith(null!));
    }

    [Fact]
    public void IsCompatibleWith_InvalidMinVersion_ReturnsFalse()
    {
        var json = """{"id":"x","displayName":"x","services":[],"minFuloraVersion":"not-a-version"}""";
        var m = PluginManifest.Parse(json)!;
        Assert.False(m.IsCompatibleWith(new Version(1, 0, 0)));
    }

    [Fact]
    public void LoadFromFile_NonExistentPath_ReturnsNull()
    {
        Assert.Null(PluginManifest.LoadFromFile("/tmp/nonexistent-fulora-manifest.json"));
    }

    [Fact]
    public void LoadFromFile_ValidFile_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, ValidJson);
            var m = PluginManifest.LoadFromFile(tmpFile);
            Assert.NotNull(m);
            Assert.Equal("Agibuild.Fulora.Plugin.Database", m.Id);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
