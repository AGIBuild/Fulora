using System.Text.Json;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class DocumentationGovernanceTests
{
    private static readonly IReadOnlyDictionary<string, string> RequiredPlatformDocuments = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Product Platform Roadmap"] = "product-platform-roadmap.md",
        ["Architecture Layering"] = "architecture-layering.md",
        ["Platform Status"] = "platform-status.md",
        ["Release Governance"] = "release-governance.md",
        ["Framework Capabilities"] = "framework-capabilities.json"
    };

    private static readonly string[] PublicDocsRepointTargets =
    [
        "README.md",
        "docs/index.md",
        "docs/articles/architecture.md",
        "docs/articles/bridge-guide.md",
        "docs/articles/spa-hosting.md",
        "docs/shipping-your-app.md",
        "docs/release-checklist.md",
        "docs/agibuild_webview_design_doc.md",
        "docs/docs-site-deploy.md"
    ];

    private static readonly string[] ForbiddenOpenSpecReferences =
    [
        "openspec/ROADMAP.md",
        "openspec/PROJECT.md",
        "openspec/specs/"
    ];

    [Fact]
    public void Required_platform_documents_exist()
    {
        var repoRoot = FindRepoRoot();
        var requiredFiles = RequiredPlatformDocuments.Values.Select(x => $"docs/{x}").ToArray();

        foreach (var relativePath in requiredFiles)
        {
            var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absolutePath), $"Missing required platform document: {relativePath}");
        }
    }

    [Fact]
    public void Docs_index_exposes_platform_document_entry_set()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        Assert.True(File.Exists(indexPath), "Missing docs/index.md");

        var indexContent = File.ReadAllText(indexPath);

        foreach (var expected in RequiredPlatformDocuments)
        {
            AssertContainsAllTokens(indexContent, expected.Key, expected.Value);
        }
    }

    [Fact]
    public void Docs_toc_includes_required_platform_navigation_at_top_level()
    {
        var repoRoot = FindRepoRoot();
        var tocPath = Path.Combine(repoRoot, "docs", "toc.yml");
        Assert.True(File.Exists(tocPath), "Missing docs/toc.yml");

        var tocContent = File.ReadAllText(tocPath);
        foreach (var expected in RequiredPlatformDocuments)
        {
            AssertContainsAllTokens(tocContent, expected.Key, expected.Value);
        }
    }

    [Fact]
    public void Platform_document_entries_are_consistent_across_index_and_toc()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        var tocPath = Path.Combine(repoRoot, "docs", "toc.yml");

        var indexContent = File.ReadAllText(indexPath);
        var tocContent = File.ReadAllText(tocPath);

        foreach (var expected in RequiredPlatformDocuments)
        {
            AssertContainsAllTokens(indexContent, expected.Key, expected.Value);
            AssertContainsAllTokens(tocContent, expected.Key, expected.Value);
        }
    }

    [Fact]
    public void Framework_capabilities_registry_includes_governed_metadata_for_each_capability()
    {
        var repoRoot = FindRepoRoot();
        var capabilitiesPath = Path.Combine(repoRoot, "docs", "framework-capabilities.json");
        Assert.True(File.Exists(capabilitiesPath), "Missing docs/framework-capabilities.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(capabilitiesPath));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("capabilities", out var capabilitiesElement), "Capabilities array is required.");
        Assert.True(root.TryGetProperty("registry_status", out var registryStatus) && registryStatus.ValueKind == JsonValueKind.String);
        Assert.Contains("placeholder", registryStatus.GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var capabilities = capabilitiesElement.EnumerateArray().ToList();
        Assert.NotEmpty(capabilities);

        var validPolicies = new HashSet<string>(StringComparer.Ordinal)
        {
            "architecture-approval-required",
            "release-gate-required",
            "compatibility-note-required"
        };
        var validLayers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Kernel",
            "Bridge",
            "Framework Services",
            "Plugins / Vertical Features"
        };
        var validTiers = new HashSet<string>(StringComparer.Ordinal) { "A", "B", "C" };
        var requiredPlatformKeys = new[] { "windows", "macos", "linux", "ios", "android" };
        var hasDualIdFields = false;

        foreach (var capability in capabilities)
        {
            Assert.True(capability.TryGetProperty("capability_id", out var starterId) && starterId.ValueKind == JsonValueKind.String);
            Assert.True(capability.TryGetProperty("id", out var legacyId) && legacyId.ValueKind == JsonValueKind.String);

            var starterCapabilityId = starterId.GetString();
            var legacyCapabilityId = legacyId.GetString();
            Assert.False(string.IsNullOrWhiteSpace(starterCapabilityId), "capability_id must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(legacyCapabilityId), "id must not be empty.");
            Assert.Equal(starterCapabilityId, legacyCapabilityId);
            hasDualIdFields = true;

            Assert.True(
                capability.TryGetProperty("breakingChangePolicy", out var policy)
                && policy.ValueKind == JsonValueKind.String
                && validPolicies.Contains(policy.GetString() ?? string.Empty),
                $"Capability '{starterCapabilityId}' must define a governed breakingChangePolicy.");

            Assert.True(
                capability.TryGetProperty("layer", out var layer)
                && layer.ValueKind == JsonValueKind.String
                && validLayers.Contains(layer.GetString() ?? string.Empty),
                $"Capability '{starterCapabilityId}' must declare a valid layer.");

            Assert.True(
                capability.TryGetProperty("tier", out var tier)
                && tier.ValueKind == JsonValueKind.String
                && validTiers.Contains(tier.GetString() ?? string.Empty),
                $"Capability '{starterCapabilityId}' must declare a valid tier.");

            Assert.True(
                capability.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(status.GetString()),
                $"Capability '{starterCapabilityId}' must declare status.");

            Assert.True(
                capability.TryGetProperty("compatibilityScope", out var compatibilityScope)
                && compatibilityScope.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(compatibilityScope.GetString()),
                $"Capability '{starterCapabilityId}' must declare compatibilityScope.");

            Assert.True(
                capability.TryGetProperty("rollbackStrategy", out var rollbackStrategy)
                && rollbackStrategy.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(rollbackStrategy.GetString()),
                $"Capability '{starterCapabilityId}' must declare rollbackStrategy.");

            Assert.True(
                capability.TryGetProperty("test_requirements", out var testRequirements)
                && testRequirements.ValueKind == JsonValueKind.Array
                && testRequirements.GetArrayLength() > 0
                && testRequirements.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())),
                $"Capability '{starterCapabilityId}' must declare non-empty test requirements.");

            Assert.True(
                capability.TryGetProperty("platform_support", out var platformSupport)
                && platformSupport.ValueKind == JsonValueKind.Object,
                $"Capability '{starterCapabilityId}' must declare platform_support.");

            foreach (var platform in requiredPlatformKeys)
            {
                Assert.True(
                    platformSupport.TryGetProperty(platform, out var supportStatus)
                    && supportStatus.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(supportStatus.GetString()),
                    $"Capability '{starterCapabilityId}' must include platform_support.{platform}.");
            }

            Assert.True(
                capability.TryGetProperty("contract_ref", out var contractRef)
                && contractRef.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(contractRef.GetString()),
                $"Capability '{starterCapabilityId}' must declare contract_ref.");

            Assert.True(
                capability.TryGetProperty("limitations_ref", out var limitationsRef)
                && limitationsRef.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(limitationsRef.GetString()),
                $"Capability '{starterCapabilityId}' must declare limitations_ref.");
        }

        if (hasDualIdFields)
        {
            Assert.True(
                root.TryGetProperty("migration_notes", out var migrationNotes)
                && migrationNotes.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(migrationNotes.GetString()),
                "When both id and capability_id are present, root.migration_notes must explain compatibility intent.");
        }
    }

    [Fact]
    public void Roadmap_mentions_capability_governance_and_release_controls()
    {
        var repoRoot = FindRepoRoot();
        var roadmapPath = Path.Combine(repoRoot, "docs", "product-platform-roadmap.md");
        Assert.True(File.Exists(roadmapPath), "Missing docs/product-platform-roadmap.md");

        var content = File.ReadAllText(roadmapPath);
        Assert.Contains("capabilit", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("architect", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Product_platform_roadmap_covers_required_governance_sections()
    {
        var repoRoot = FindRepoRoot();
        var roadmapPath = Path.Combine(repoRoot, "docs", "product-platform-roadmap.md");
        Assert.True(File.Exists(roadmapPath), "Missing docs/product-platform-roadmap.md");

        var content = File.ReadAllText(roadmapPath);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["positioning"],
            ["product-grade", "platform"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["strategic", "direction"],
            ["strategy", "stable"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["stable core", "extensions"],
            ["stable", "core", "extensions"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["layering", "model"],
            ["four", "layers"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["capability", "support", "contract"],
            ["capability", "tier"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["security"],
            ["boundary", "default deny"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["observability"],
            ["traces", "metrics"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["release", "governance"],
            ["release gates"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["developer", "defaults"],
            ["templates", "default"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["p0", "p5"],
            ["priority", "p0"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["documentation", "governance"],
            ["docfx", "discoverable"]);
    }

    [Fact]
    public void Architecture_layering_defines_four_layers_decision_tree_and_kernel_approval_rules()
    {
        var repoRoot = FindRepoRoot();
        var layeringPath = Path.Combine(repoRoot, "docs", "architecture-layering.md");
        Assert.True(File.Exists(layeringPath), "Missing docs/architecture-layering.md");

        var content = File.ReadAllText(layeringPath);
        AssertContainsAllTokensIgnoreCase(
            content,
            "kernel",
            "bridge",
            "framework services",
            "plugins / vertical features");
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["allowed dependencies"],
            ["depends only", "must not depend"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["public api", "types"],
            ["kernel api", "bridge api", "framework api", "plugin api"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["decision tree"],
            ["classify", "capability"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["kernel", "approval", "before merge"],
            ["kernel api", "approval rules"]);
    }

    [Fact]
    public void Platform_status_placeholder_reserves_tiered_support_and_known_limitations_sections()
    {
        var repoRoot = FindRepoRoot();
        var statusPath = Path.Combine(repoRoot, "docs", "platform-status.md");
        Assert.True(File.Exists(statusPath), "Missing docs/platform-status.md");

        var content = File.ReadAllText(statusPath);
        AssertContainsAllTokensIgnoreCase(content, "placeholder", "tbd");
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["must not", "stable release fact snapshot"],
            ["not be treated", "stable", "fact snapshot"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["tier a", "tier b", "tier c"],
            ["known limitations"]);
    }

    [Fact]
    public void Index_declares_tier_registry_status_as_governed_truth_source()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        Assert.True(File.Exists(indexPath), "Missing docs/index.md");

        var content = File.ReadAllText(indexPath);
        AssertContainsAllTokensIgnoreCase(
            content,
            "runtime model",
            "tiered",
            "registry",
            "status",
            "governed source of truth");
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["directional", "five platforms"],
            ["directional", "five-platform"],
            ["does not mean", "fully covered"]);
    }

    [Fact]
    public void Framework_capabilities_registry_uses_starter_schema_and_representative_entries()
    {
        var repoRoot = FindRepoRoot();
        var capabilitiesPath = Path.Combine(repoRoot, "docs", "framework-capabilities.json");
        Assert.True(File.Exists(capabilitiesPath), "Missing docs/framework-capabilities.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(capabilitiesPath));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("schemaVersion", out var schemaVersion) && schemaVersion.ValueKind == JsonValueKind.String);
        Assert.True(root.TryGetProperty("migration_notes", out var migrationNotes) && migrationNotes.ValueKind == JsonValueKind.String);
        var migrationNotesText = migrationNotes.GetString() ?? string.Empty;
        AssertContainsAllTokensIgnoreCase(
            migrationNotesText,
            "capability_id",
            "canonical",
            "id",
            "compatibility",
            "migration");
        Assert.True(root.TryGetProperty("capabilities", out var capabilitiesElement), "Capabilities array is required.");

        var capabilities = capabilitiesElement.EnumerateArray().ToList();
        Assert.True(capabilities.Count >= 4, "Starter capability registry should include representative entries across four layers.");

        var observedLayers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var capability in capabilities)
        {
            Assert.True(capability.TryGetProperty("layer", out var layer) && layer.ValueKind == JsonValueKind.String);
            var layerName = layer.GetString();
            Assert.False(string.IsNullOrWhiteSpace(layerName), "Each capability must declare a non-empty layer.");
            observedLayers.Add(layerName!);
        }

        Assert.Contains("Kernel", observedLayers);
        Assert.Contains("Bridge", observedLayers);
        Assert.Contains("Framework Services", observedLayers);
        Assert.Contains("Plugins / Vertical Features", observedLayers);
    }

    [Fact]
    public void Release_governance_defines_stable_rules_and_release_gates()
    {
        var repoRoot = FindRepoRoot();
        var governancePath = Path.Combine(repoRoot, "docs", "release-governance.md");
        Assert.True(File.Exists(governancePath), "Missing docs/release-governance.md");

        var content = File.ReadAllText(governancePath);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["stable release", "rules"], ["release gates"], ["promotion flow"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["compatibility"], ["security"], ["observability"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["evidence contract"], ["artifact convention"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["schema"], ["path"], ["naming"]);
        AssertContainsAllTokens(content, "`gate`", "`releaseLine`", "`snapshotAtUtc`", "`status`", "`producer`", "`artifacts[]`");
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["must include"],
            ["required"],
            ["every gate artifact"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["gate", "identity"], ["gate", "belongs"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["releaseline", "release line"], ["governed release line"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["snapshotatutc", "utc"], ["iso-8601"], ["yyyy-mm-ddthh:mm:ssz"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["status", "pass", "fail"], ["status", "blocked"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["producer", "generator"], ["ci", "workflow", "job"], ["release tool"]);
        AssertContainsAnyTokenGroupIgnoreCase(content, ["artifacts[]", "type", "path"], ["hash"], ["build/run id"]);
    }

    [Fact]
    public void Public_docs_do_not_reference_legacy_openspec_paths()
    {
        var repoRoot = FindRepoRoot();

        foreach (var relativePath in PublicDocsRepointTargets)
        {
            var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absolutePath), $"Missing public document under test: {relativePath}");

            var content = File.ReadAllText(absolutePath);
            foreach (var forbidden in ForbiddenOpenSpecReferences)
            {
                Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Readme_links_to_product_platform_roadmap_doc_entry()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var content = File.ReadAllText(readmePath);
        Assert.Contains("docs/product-platform-roadmap.md", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipping_guide_links_to_release_governance_doc()
    {
        var repoRoot = FindRepoRoot();
        var shippingPath = Path.Combine(repoRoot, "docs", "shipping-your-app.md");
        Assert.True(File.Exists(shippingPath), "Missing docs/shipping-your-app.md");

        var content = File.ReadAllText(shippingPath);
        Assert.Contains("release-governance.md", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_and_docs_index_use_consistent_public_roadmap_status_language()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");
        Assert.True(File.Exists(indexPath), "Missing docs/index.md");

        var readmeContent = File.ReadAllText(readmePath);
        var indexContent = File.ReadAllText(indexPath);

        var readmeHasCompleted = readmeContent.Contains("completed", StringComparison.OrdinalIgnoreCase);
        var readmeHasPlanned = readmeContent.Contains("planned", StringComparison.OrdinalIgnoreCase);
        var indexHasCompleted = indexContent.Contains("completed", StringComparison.OrdinalIgnoreCase);
        var indexHasPlanned = indexContent.Contains("planned", StringComparison.OrdinalIgnoreCase);

        Assert.False(
            readmeHasCompleted && indexHasPlanned,
            "README says roadmap/status is completed while docs/index says planned. Public roadmap/status language must stay consistent.");

        Assert.False(
            readmeHasPlanned && indexHasCompleted,
            "README says roadmap/status is planned while docs/index says completed. Public roadmap/status language must stay consistent.");
    }

    [Fact]
    public void Historical_design_doc_uses_non_normative_language_and_points_to_current_governance_sources()
    {
        var repoRoot = FindRepoRoot();
        var designDocPath = Path.Combine(repoRoot, "docs", "agibuild_webview_design_doc.md");
        Assert.True(File.Exists(designDocPath), "Missing docs/agibuild_webview_design_doc.md");

        var content = File.ReadAllText(designDocPath);

        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["历史", "参考"],
            ["早期", "设计稿"],
            ["不作为", "规范"]);
        AssertContainsAllTokensIgnoreCase(
            content,
            "product-platform-roadmap.md",
            "platform-status.md",
            "release-governance.md");

        var forbiddenPhrases = new[]
        {
            "已达成基础目标",
            "覆盖率",
            "✅ done",
            "planned",
            "🔜 next"
        };

        foreach (var forbidden in forbiddenPhrases)
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_declares_governed_tiered_support_language_for_five_platform_direction()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var content = File.ReadAllText(readmePath);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["tiered", "registry", "status"],
            ["tiered", "governance"],
            ["registry", "status", "governed"]);
        AssertContainsAnyTokenGroupIgnoreCase(
            content,
            ["directional"],
            ["not fully covered"],
            ["does not mean", "fully covered"]);
    }

    [Fact]
    public void Historical_design_doc_is_not_exposed_as_primary_navigation_entry()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        var tocPath = Path.Combine(repoRoot, "docs", "toc.yml");
        Assert.True(File.Exists(indexPath), "Missing docs/index.md");
        Assert.True(File.Exists(tocPath), "Missing docs/toc.yml");

        var indexContent = File.ReadAllText(indexPath);
        var tocContent = File.ReadAllText(tocPath);
        const string historicalDesignDoc = "agibuild_webview_design_doc.md";

        Assert.DoesNotContain(historicalDesignDoc, indexContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(historicalDesignDoc, tocContent, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Agibuild.Fulora.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static void AssertContainsAllTokens(string content, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            Assert.Contains(token, content, StringComparison.Ordinal);
        }
    }

    private static void AssertContainsAllTokensIgnoreCase(string content, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            Assert.Contains(token, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertContainsAnyTokenGroupIgnoreCase(string content, params string[][] groups)
    {
        var matched = groups.Any(group => group.All(token => content.Contains(token, StringComparison.OrdinalIgnoreCase)));
        Assert.True(
            matched,
            $"Content must satisfy at least one semantic token group. Groups: {string.Join(" | ", groups.Select(g => $"[{string.Join(", ", g)}]"))}");
    }
}
