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
        ["Framework Capabilities"] = "framework-capabilities.md"
    };

    private static readonly string[] RequiredMachineReadableArtifacts =
    {
        "docs/framework-capabilities.json"
    };

    private static readonly string[] GovernedPublicDocuments =
    {
        "README.md",
        "docs/index.md",
        "docs/framework-capabilities.md",
        "docs/articles/architecture.md",
        "docs/articles/bridge-guide.md",
        "docs/articles/spa-hosting.md",
        "docs/shipping-your-app.md",
        "docs/release-checklist.md",
        "docs/agibuild_webview_design_doc.md",
        "docs/docs-site-deploy.md"
    };

    private static readonly string LegacySpecDirectoryName = string.Concat("open", "spec");
    private static readonly string LegacyRoadmapPath = $"{LegacySpecDirectoryName}/ROADMAP.md";
    private static readonly string LegacyProjectPath = $"{LegacySpecDirectoryName}/PROJECT.md";
    private static readonly string LegacySpecsPath = $"{LegacySpecDirectoryName}/specs/";
    private static readonly string LegacySkillSearchPattern = string.Concat(LegacySpecDirectoryName, "-*");
    private static readonly string LegacyPromptSearchPattern = string.Concat("ops", "x-*");

    [Fact]
    public void Required_platform_documents_exist()
    {
        var repoRoot = FindRepoRoot();
        var requiredFiles = RequiredPlatformDocuments.Values
            .Select(x => $"docs/{x}")
            .Concat(RequiredMachineReadableArtifacts)
            .ToArray();

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

        var platformLinks = ParsePlatformDocumentsTableLinks(File.ReadAllLines(indexPath));
        Assert.Equal(RequiredPlatformDocuments.Count, platformLinks.Count);

        foreach (var expected in RequiredPlatformDocuments)
            Assert.Equal(expected.Value, platformLinks[expected.Key]);
    }

    [Fact]
    public void Docs_index_includes_role_based_entrypoints_for_primary_docs_paths()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        Assert.True(File.Exists(indexPath), "Missing docs/index.md");

        var startBuildingSection = ParseMarkdownSection(File.ReadAllLines(indexPath), "## Start Building");
        var entryRows = ParseMarkdownTableRows(startBuildingSection)
            .ToDictionary(
                row => row[0],
                row => row[1],
                StringComparer.Ordinal);

        Assert.True(
            entryRows.TryGetValue("I am building an app", out var appEntry),
            "docs/index.md Start Building section must include an 'I am building an app' entry.");
        Assert.Contains("articles/getting-started.md", appEntry, StringComparison.Ordinal);

        Assert.True(
            entryRows.TryGetValue("I am building a plugin", out var pluginEntry),
            "docs/index.md Start Building section must include an 'I am building a plugin' entry.");
        Assert.Contains("plugin-authoring-guide.md", pluginEntry, StringComparison.Ordinal);

        Assert.True(
            entryRows.TryGetValue("I am working on the platform", out var platformEntry),
            "docs/index.md Start Building section must include an 'I am working on the platform' entry.");
        Assert.Contains("product-platform-roadmap.md", platformEntry, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_toc_includes_required_platform_navigation_at_top_level()
    {
        var repoRoot = FindRepoRoot();
        var tocPath = Path.Combine(repoRoot, "docs", "toc.yml");
        Assert.True(File.Exists(tocPath), "Missing docs/toc.yml");

        var topLevelItems = ParseTopLevelTocItems(File.ReadAllLines(tocPath));
        foreach (var expected in RequiredPlatformDocuments)
        {
            Assert.True(
                topLevelItems.TryGetValue(expected.Key, out var href),
                $"Top-level TOC item '{expected.Key}' not found.");
            Assert.Equal(expected.Value, href);
        }
    }

    [Fact]
    public void Platform_document_entries_are_consistent_across_index_toc_and_docfx_content()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "index.md");
        var tocPath = Path.Combine(repoRoot, "docs", "toc.yml");
        var docfxPath = Path.Combine(repoRoot, "docs", "docfx.json");

        var indexLinks = ParsePlatformDocumentsTableLinks(File.ReadAllLines(indexPath));
        var tocLinks = ParseTopLevelTocItems(File.ReadAllLines(tocPath));

        Assert.Equal(indexLinks.Count, RequiredPlatformDocuments.Count);
        Assert.Equal(tocLinks.Count(x => RequiredPlatformDocuments.ContainsKey(x.Key)), RequiredPlatformDocuments.Count);

        foreach (var expected in RequiredPlatformDocuments)
        {
            Assert.Equal(expected.Value, indexLinks[expected.Key]);
            Assert.Equal(expected.Value, tocLinks[expected.Key]);
        }

        using var docfx = JsonDocument.Parse(File.ReadAllText(docfxPath));
        var contentFiles = docfx.RootElement.GetProperty("build").GetProperty("content")
            .EnumerateArray()
            .Where(x => x.TryGetProperty("files", out _))
            .SelectMany(x => x.GetProperty("files").EnumerateArray())
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("*.md", contentFiles);
        Assert.Contains("toc.yml", contentFiles);

        var resourceFiles = docfx.RootElement.GetProperty("build").GetProperty("resource")
            .EnumerateArray()
            .Where(x => x.TryGetProperty("files", out _))
            .SelectMany(x => x.GetProperty("files").EnumerateArray())
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("framework-capabilities.json", resourceFiles);
    }

    [Fact]
    public void Docfx_build_content_models_framework_capabilities_as_wrapper_page_with_json_resource()
    {
        var repoRoot = FindRepoRoot();
        var docfxPath = Path.Combine(repoRoot, "docs", "docfx.json");
        Assert.True(File.Exists(docfxPath), "Missing docs/docfx.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(docfxPath));
        var contentEntries = doc.RootElement.GetProperty("build").GetProperty("content");
        var resourceEntries = doc.RootElement.GetProperty("build").GetProperty("resource");

        var contentFiles = contentEntries
            .EnumerateArray()
            .Where(x => x.TryGetProperty("files", out _))
            .SelectMany(x => x.GetProperty("files").EnumerateArray())
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        var resourceFiles = resourceEntries
            .EnumerateArray()
            .Where(x => x.TryGetProperty("files", out _))
            .SelectMany(x => x.GetProperty("files").EnumerateArray())
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            contentFiles.Contains("*.md") || contentFiles.Contains("framework-capabilities.md"),
            "DocFX build content must include markdown conceptual pages, including the framework capabilities wrapper.");
        Assert.False(
            contentFiles.Contains("framework-capabilities.json"),
            "framework-capabilities.json should not be modeled as conceptual content.");
        Assert.True(
            resourceFiles.Contains("framework-capabilities.json"),
            "DocFX build resource must include framework-capabilities.json as downloadable machine-readable artifact.");
    }

    [Fact]
    public void Framework_capabilities_entries_declare_compatibility_scope_and_rollback_strategy()
    {
        var repoRoot = FindRepoRoot();
        var capabilitiesPath = Path.Combine(repoRoot, "docs", "framework-capabilities.json");
        Assert.True(File.Exists(capabilitiesPath), "Missing docs/framework-capabilities.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(capabilitiesPath));
        var capabilities = doc.RootElement.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.NotEmpty(capabilities);

        var validPolicies = new HashSet<string>(StringComparer.Ordinal)
        {
            "architecture-approval-required",
            "release-gate-required",
            "compatibility-note-required"
        };

        foreach (var capability in capabilities)
        {
            var capabilityId = capability.GetProperty("id").GetString();

            Assert.True(
                capability.TryGetProperty("breakingChangePolicy", out var policy)
                && policy.ValueKind == JsonValueKind.String
                && validPolicies.Contains(policy.GetString() ?? string.Empty),
                $"Capability '{capabilityId}' must define a governed breakingChangePolicy.");

            Assert.True(
                capability.TryGetProperty("compatibilityScope", out var compatibilityScope)
                && compatibilityScope.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(compatibilityScope.GetString()),
                $"Capability '{capabilityId}' must define non-empty compatibilityScope.");

            Assert.True(
                capability.TryGetProperty("rollbackStrategy", out var rollbackStrategy)
                && rollbackStrategy.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(rollbackStrategy.GetString()),
                $"Capability '{capabilityId}' must define non-empty rollbackStrategy.");
        }
    }

    [Fact]
    public void Framework_capabilities_registry_exposes_required_keys_and_governed_layers()
    {
        var repoRoot = FindRepoRoot();
        var capabilitiesPath = Path.Combine(repoRoot, "docs", "framework-capabilities.json");
        Assert.True(File.Exists(capabilitiesPath), "Missing docs/framework-capabilities.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(capabilitiesPath));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("schemaVersion", out _), "Capability registry must declare schemaVersion.");
        Assert.True(root.TryGetProperty("updatedAtUtc", out _), "Capability registry must declare updatedAtUtc.");
        Assert.True(root.TryGetProperty("layers", out var layersElement), "Capability registry must declare governed layers.");
        Assert.True(root.TryGetProperty("capabilities", out var capabilitiesElement), "Capability registry must declare capabilities.");

        var governedLayers = layersElement.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Kernel", governedLayers);
        Assert.Contains("Bridge", governedLayers);
        Assert.Contains("Framework", governedLayers);
        Assert.Contains("Plugin", governedLayers);

        var capabilityLayers = capabilitiesElement.EnumerateArray()
            .Select(x => x.GetProperty("layer").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Kernel", capabilityLayers);
        Assert.Contains("Bridge", capabilityLayers);
        Assert.Contains("Framework", capabilityLayers);
        Assert.Contains("Plugin", capabilityLayers);
    }

    [Fact]
    public void Framework_capabilities_wrapper_doc_links_machine_source_and_related_governance_docs()
    {
        var repoRoot = FindRepoRoot();
        var wrapperDocPath = Path.Combine(repoRoot, "docs", "framework-capabilities.md");
        Assert.True(File.Exists(wrapperDocPath), "Missing docs/framework-capabilities.md");

        var content = File.ReadAllText(wrapperDocPath);
        Assert.Contains("framework-capabilities.json", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product-platform-roadmap.md", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("platform-status.md", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-governance.md", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Platform_status_entrypoints_describe_governed_status_location_not_prepopulated_snapshot()
    {
        var repoRoot = FindRepoRoot();
        var entrypointFiles = new[]
        {
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "docs", "index.md"),
            Path.Combine(repoRoot, "docs", "framework-capabilities.md")
        };

        foreach (var path in entrypointFiles)
        {
            Assert.True(File.Exists(path), $"Missing entrypoint doc: {path}");
            var content = File.ReadAllText(path);
            Assert.Contains("platform-status.md", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("current governed platform snapshot", content, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                content.Contains("status page", StringComparison.OrdinalIgnoreCase)
                || content.Contains("snapshot location", StringComparison.OrdinalIgnoreCase)
                || content.Contains("template", StringComparison.OrdinalIgnoreCase),
                "Platform status entrypoints should describe status semantics as a governed page/template/publication location.");
        }
    }

    [Fact]
    public void Platform_status_page_stays_as_governed_template_and_publication_location()
    {
        var repoRoot = FindRepoRoot();
        var platformStatusPath = Path.Combine(repoRoot, "docs", "platform-status.md");
        Assert.True(File.Exists(platformStatusPath), "Missing docs/platform-status.md");

        var content = File.ReadAllText(platformStatusPath);
        Assert.True(
            content.Contains("template", StringComparison.OrdinalIgnoreCase)
            || content.Contains("publication location", StringComparison.OrdinalIgnoreCase)
            || content.Contains("release-line snapshots", StringComparison.OrdinalIgnoreCase),
            "Platform status page should describe template/publication-location semantics.");
        Assert.DoesNotContain("Phase ", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("all roadmap phases", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current governed platform snapshot", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Platform_status_and_roadmap_publish_capability_tiers_and_registry_links()
    {
        var repoRoot = FindRepoRoot();
        var platformStatusPath = Path.Combine(repoRoot, "docs", "platform-status.md");
        var roadmapPath = Path.Combine(repoRoot, "docs", "product-platform-roadmap.md");
        var compatibilityProposalPath = Path.Combine(repoRoot, "docs", "agibuild_webview_compatibility_matrix_proposal.md");

        Assert.True(File.Exists(platformStatusPath), "Missing docs/platform-status.md");
        Assert.True(File.Exists(roadmapPath), "Missing docs/product-platform-roadmap.md");
        Assert.True(File.Exists(compatibilityProposalPath), "Missing docs/agibuild_webview_compatibility_matrix_proposal.md");

        var platformStatus = File.ReadAllText(platformStatusPath);
        Assert.Contains("Tier A", platformStatus, StringComparison.Ordinal);
        Assert.Contains("Tier B", platformStatus, StringComparison.Ordinal);
        Assert.Contains("Tier C", platformStatus, StringComparison.Ordinal);

        var roadmap = File.ReadAllText(roadmapPath);
        Assert.Contains("framework-capabilities.json", roadmap, StringComparison.Ordinal);
        Assert.Contains("platform-status.md", roadmap, StringComparison.Ordinal);

        var compatibilityProposal = File.ReadAllText(compatibilityProposalPath);
        Assert.Contains("framework-capabilities", compatibilityProposal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("platform-status", compatibilityProposal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roadmap_breaking_change_rule_matches_capability_policy_model()
    {
        var repoRoot = FindRepoRoot();
        var roadmapPath = Path.Combine(repoRoot, "docs", "product-platform-roadmap.md");
        Assert.True(File.Exists(roadmapPath), "Missing docs/product-platform-roadmap.md");

        var content = File.ReadAllText(roadmapPath);
        Assert.Contains("breakingChangePolicy", content, StringComparison.Ordinal);
        Assert.True(
            (content.Contains("architecture approval", StringComparison.OrdinalIgnoreCase)
             || content.Contains("approval", StringComparison.OrdinalIgnoreCase))
            && content.Contains("kernel", StringComparison.OrdinalIgnoreCase),
            "Roadmap must define architecture approval requirements for kernel-level changes.");
        Assert.True(
            content.Contains("release-gate", StringComparison.OrdinalIgnoreCase)
            && content.Contains("breaking", StringComparison.OrdinalIgnoreCase),
            "Roadmap must define release-gate evidence requirements for breaking changes.");
    }

    [Fact]
    public void Governed_public_docs_do_not_reference_removed_legacy_spec_paths()
    {
        var repoRoot = FindRepoRoot();
        var blockedPaths = new[]
        {
            LegacyRoadmapPath,
            LegacyProjectPath,
            LegacySpecsPath
        };

        foreach (var relativePath in GovernedPublicDocuments)
        {
            var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Missing governed public doc: {relativePath}");

            var content = File.ReadAllText(fullPath);
            foreach (var blockedPath in blockedPaths)
            {
                Assert.DoesNotContain(
                    blockedPath,
                    content,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Readme_repoints_roadmap_navigation_to_product_platform_roadmap_document()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var content = File.ReadAllText(readmePath);
        Assert.Contains("docs/product-platform-roadmap.md", content, StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyRoadmapPath, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacyProjectPath, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_uses_capability_tiers_language_and_links_new_platform_documents()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var content = File.ReadAllText(readmePath);
        Assert.Contains("Capability Tiers", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/product-platform-roadmap.md", content, StringComparison.Ordinal);
        Assert.Contains("docs/platform-status.md", content, StringComparison.Ordinal);
        Assert.DoesNotContain("you stay on one runtime model", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Two paths, one runtime", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_does_not_publish_legacy_phase_completion_claims()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var content = File.ReadAllText(readmePath);
        Assert.DoesNotContain("Phase 12", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("All roadmap phases through 12 are done", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_preserves_primary_path_quick_start_commands()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), "Missing README.md");

        var quickStartSection = ParseMarkdownSection(File.ReadAllLines(readmePath), "## Start in 60 Seconds");
        Assert.Contains("fulora new", quickStartSection, StringComparison.Ordinal);
        Assert.Contains("fulora dev", quickStartSection, StringComparison.Ordinal);
        Assert.Contains("fulora package", quickStartSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_article_aligns_with_platform_layering_terms()
    {
        var repoRoot = FindRepoRoot();
        var architecturePath = Path.Combine(repoRoot, "docs", "articles", "architecture.md");
        Assert.True(File.Exists(architecturePath), "Missing docs/articles/architecture.md");

        var content = File.ReadAllText(architecturePath);
        Assert.Contains("platform kernel", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adapter layer", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capability", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Architecture_layering_doc_and_pr_template_expose_layering_governance_hook()
    {
        var repoRoot = FindRepoRoot();
        var layeringDocPath = Path.Combine(repoRoot, "docs", "architecture-layering.md");
        var prTemplatePath = Path.Combine(repoRoot, ".github", "PULL_REQUEST_TEMPLATE.md");
        var layeringBuildFilePath = Path.Combine(repoRoot, "build", "Build.LayeringGovernance.cs");
        var layeringTargetsPath = Path.Combine(repoRoot, "build", "LayeringGovernance.targets");

        Assert.True(File.Exists(layeringDocPath), "Missing docs/architecture-layering.md");
        Assert.True(File.Exists(prTemplatePath), "Missing .github/PULL_REQUEST_TEMPLATE.md");

        var layeringDoc = File.ReadAllText(layeringDocPath);
        Assert.Contains("Dependency Policy", layeringDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kernel", layeringDoc, StringComparison.Ordinal);
        Assert.Contains("Bridge", layeringDoc, StringComparison.Ordinal);
        Assert.Contains("Framework", layeringDoc, StringComparison.Ordinal);
        Assert.Contains("Plugin", layeringDoc, StringComparison.Ordinal);

        var prTemplate = File.ReadAllText(prTemplatePath);
        Assert.Contains("Layer Impact", prTemplate, StringComparison.OrdinalIgnoreCase);

        Assert.True(
            File.Exists(layeringBuildFilePath) || File.Exists(layeringTargetsPath),
            "A repo-visible layering governance build hook must exist under build/.");
    }

    [Fact]
    public void Bridge_and_spa_guides_reference_new_platform_roadmap()
    {
        var repoRoot = FindRepoRoot();
        var guidePaths = new[]
        {
            Path.Combine(repoRoot, "docs", "articles", "bridge-guide.md"),
            Path.Combine(repoRoot, "docs", "articles", "spa-hosting.md")
        };

        foreach (var guidePath in guidePaths)
        {
            Assert.True(File.Exists(guidePath), $"Missing guide: {guidePath}");
            var content = File.ReadAllText(guidePath);
            Assert.Contains("../product-platform-roadmap.md", content, StringComparison.Ordinal);
            Assert.DoesNotContain(LegacyRoadmapPath, content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Phase 5", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Phase 8", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Shipping_guide_references_release_governance_document()
    {
        var repoRoot = FindRepoRoot();
        var shippingGuidePath = Path.Combine(repoRoot, "docs", "shipping-your-app.md");
        Assert.True(File.Exists(shippingGuidePath), "Missing docs/shipping-your-app.md");

        var content = File.ReadAllText(shippingGuidePath);
        Assert.Contains("(release-governance.md)", content, StringComparison.Ordinal);
        Assert.DoesNotContain("(docs/release-governance.md)", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySpecsPath, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_checklist_declares_release_governance_as_normative_reference()
    {
        var repoRoot = FindRepoRoot();
        var releaseChecklistPath = Path.Combine(repoRoot, "docs", "release-checklist.md");
        Assert.True(File.Exists(releaseChecklistPath), "Missing docs/release-checklist.md");

        var content = File.ReadAllText(releaseChecklistPath);
        Assert.Contains("[Release Governance](release-governance.md)", content, StringComparison.Ordinal);
        Assert.True(
            content.Contains("source of truth", StringComparison.OrdinalIgnoreCase)
            || content.Contains("governed by", StringComparison.OrdinalIgnoreCase),
            "Release checklist should declare normative governance semantics.");
    }

    [Fact]
    public void Docs_site_deploy_doc_does_not_allow_invalidfilelink_legacy_spec_exceptions()
    {
        var repoRoot = FindRepoRoot();
        var docsSiteDeployPath = Path.Combine(repoRoot, "docs", "docs-site-deploy.md");
        Assert.True(File.Exists(docsSiteDeployPath), "Missing docs/docs-site-deploy.md");

        var content = File.ReadAllText(docsSiteDeployPath);
        Assert.DoesNotContain("InvalidFileLink", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySpecDirectoryName, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_site_deploy_document_matches_independent_docs_workflow_semantics()
    {
        var repoRoot = FindRepoRoot();
        var docsWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "docs.yml");
        var ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        var docsDeployDocPath = Path.Combine(repoRoot, "docs", "docs-site-deploy.md");

        Assert.True(File.Exists(docsWorkflowPath), "Missing .github/workflows/docs.yml");
        Assert.True(File.Exists(ciWorkflowPath), "Missing .github/workflows/ci.yml");
        Assert.True(File.Exists(docsDeployDocPath), "Missing docs/docs-site-deploy.md");

        var docsWorkflow = File.ReadAllText(docsWorkflowPath);
        var ciWorkflow = File.ReadAllText(ciWorkflowPath);
        var deployDoc = File.ReadAllText(docsDeployDocPath);

        Assert.True(
            docsWorkflow.Contains("Deploy Documentation", StringComparison.OrdinalIgnoreCase)
            && docsWorkflow.Contains("push", StringComparison.OrdinalIgnoreCase)
            && docsWorkflow.Contains("main", StringComparison.OrdinalIgnoreCase)
            && docsWorkflow.Contains("docs/**", StringComparison.Ordinal)
            && (docsWorkflow.Contains("GitHub Pages", StringComparison.OrdinalIgnoreCase)
                || docsWorkflow.Contains("deploy-pages", StringComparison.OrdinalIgnoreCase)),
            "docs.yml should model docs deployment on main + docs/** to GitHub Pages.");
        Assert.True(
            ciWorkflow.Contains("paths-ignore:", StringComparison.OrdinalIgnoreCase)
            && ciWorkflow.Contains("docs/**", StringComparison.Ordinal)
            && ciWorkflow.Contains("*.md", StringComparison.Ordinal),
            "ci.yml should ignore docs-only changes.");

        Assert.Contains("docs.yml", deployDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/**", deployDoc, StringComparison.Ordinal);
        Assert.Contains("main", deployDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitHub Pages", deployDoc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("integrated into the unified `ci.yml` workflow", deployDoc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("After approval", deployDoc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_checklist_version_semantics_match_ci_workflow_resolution_rules()
    {
        var repoRoot = FindRepoRoot();
        var ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        var releaseChecklistPath = Path.Combine(repoRoot, "docs", "release-checklist.md");

        Assert.True(File.Exists(ciWorkflowPath), "Missing .github/workflows/ci.yml");
        Assert.True(File.Exists(releaseChecklistPath), "Missing docs/release-checklist.md");

        var ciWorkflow = File.ReadAllText(ciWorkflowPath);
        var checklist = File.ReadAllText(releaseChecklistPath);

        Assert.True(
            ciWorkflow.Contains("is_release", StringComparison.OrdinalIgnoreCase)
            && ciWorkflow.Contains("v$VERSION", StringComparison.Ordinal)
            && ciWorkflow.Contains("ci.${RUN_NUMBER}", StringComparison.Ordinal)
            && ciWorkflow.Contains("prerelease_suffix", StringComparison.OrdinalIgnoreCase),
            "ci.yml should define stable-tag, ci-suffix, and manual-prerelease version semantics.");

        Assert.DoesNotContain("{VersionPrefix}.{run_number}", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v{VersionPrefix}", checklist, StringComparison.Ordinal);
        Assert.Contains("VersionPrefix-ci.{run_number}", checklist, StringComparison.Ordinal);
        Assert.Contains("prerelease", checklist, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_spec_assets_and_prompts_are_removed_and_pr_template_no_longer_requires_legacy_spec_artifacts()
    {
        var repoRoot = FindRepoRoot();
        var legacySpecPath = Path.Combine(repoRoot, LegacySpecDirectoryName);
        var skillsPath = Path.Combine(repoRoot, ".github", "skills");
        var promptsPath = Path.Combine(repoRoot, ".github", "prompts");
        var prTemplatePath = Path.Combine(repoRoot, ".github", "PULL_REQUEST_TEMPLATE.md");

        Assert.False(Directory.Exists(legacySpecPath), $"{LegacySpecDirectoryName}/ directory should be removed.");
        var legacySkillAssets = Directory.Exists(skillsPath)
            ? Directory.EnumerateFileSystemEntries(skillsPath, LegacySkillSearchPattern, SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
        Assert.Empty(legacySkillAssets);

        var legacyPromptAssets = Directory.Exists(promptsPath)
            ? Directory.EnumerateFileSystemEntries(promptsPath, LegacyPromptSearchPattern, SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
        Assert.Empty(legacyPromptAssets);

        Assert.True(File.Exists(prTemplatePath), "Missing .github/PULL_REQUEST_TEMPLATE.md");
        var prTemplate = File.ReadAllText(prTemplatePath);

        Assert.DoesNotContain(string.Concat("Open", "Spec"), prTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySpecDirectoryName, prTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layer Impact", prTemplate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Historical_webview_design_doc_redirects_to_new_platform_documents()
    {
        var repoRoot = FindRepoRoot();
        var designDocPath = Path.Combine(repoRoot, "docs", "agibuild_webview_design_doc.md");
        Assert.True(File.Exists(designDocPath), "Missing docs/agibuild_webview_design_doc.md");

        var content = File.ReadAllText(designDocPath);
        Assert.True(
            content.Contains("保留早期", StringComparison.OrdinalIgnoreCase)
            || content.Contains("历史背景", StringComparison.OrdinalIgnoreCase)
            || content.Contains("historical", StringComparison.OrdinalIgnoreCase),
            "Design doc should clearly indicate it is historical/background context.");
        Assert.Contains("product-platform-roadmap.md", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("platform-status.md", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacyRoadmapPath, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacyProjectPath, content, StringComparison.OrdinalIgnoreCase);
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

    private static Dictionary<string, string> ParseTopLevelTocItems(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pendingName = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmedStart = line.TrimStart();
            var leadingSpaces = line.Length - trimmedStart.Length;

            if (trimmedStart.StartsWith("- name:", StringComparison.Ordinal))
            {
                if (leadingSpaces != 0)
                    continue;

                pendingName = trimmedStart["- name:".Length..].Trim();
                continue;
            }

            if (leadingSpaces == 0)
            {
                pendingName = null;
                continue;
            }

            if (trimmedStart.StartsWith("href:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(pendingName))
            {
                var href = trimmedStart["href:".Length..].Trim();
                result[pendingName] = href;
                pendingName = null;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ParsePlatformDocumentsTableLinks(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!inSection)
            {
                if (string.Equals(line, "## Platform Documents", StringComparison.Ordinal))
                    inSection = true;

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var scanIndex = 0;
            while (scanIndex < line.Length)
            {
                var labelStart = line.IndexOf('[', scanIndex);
                if (labelStart < 0)
                    break;

                var labelEnd = line.IndexOf(']', labelStart + 1);
                var hrefStart = labelEnd >= 0 ? line.IndexOf('(', labelEnd + 1) : -1;
                var hrefEnd = hrefStart >= 0 ? line.IndexOf(')', hrefStart + 1) : -1;

                if (labelEnd < 0 || hrefStart < 0 || hrefEnd < 0)
                    break;

                var label = line[(labelStart + 1)..labelEnd].Trim();
                var href = line[(hrefStart + 1)..hrefEnd].Trim();
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(href))
                    result[label] = href;

                scanIndex = hrefEnd + 1;
            }
        }

        return result;
    }

    private static string ParseMarkdownSection(IEnumerable<string> lines, string heading)
    {
        var sectionLines = new List<string>();
        var inSection = false;
        var headingFound = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (!inSection)
            {
                if (string.Equals(line.Trim(), heading, StringComparison.Ordinal))
                {
                    inSection = true;
                    headingFound = true;
                }

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
                break;

            sectionLines.Add(rawLine);
        }

        Assert.True(headingFound, $"Missing markdown heading: {heading}");
        Assert.True(sectionLines.Count > 0, $"Markdown heading '{heading}' exists but its section is empty.");
        return string.Join(Environment.NewLine, sectionLines);
    }

    private static IReadOnlyList<string[]> ParseMarkdownTableRows(string section)
    {
        var rows = new List<string[]>();

        foreach (var rawLine in section.Split(Environment.NewLine))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.EndsWith("|", StringComparison.Ordinal))
                continue;

            var cells = line[1..^1]
                .Split('|')
                .Select(x => x.Trim())
                .ToArray();

            if (cells.Length < 2)
                continue;

            var isSeparatorRow = cells.All(cell => cell.Length > 0 && cell.All(ch => ch == '-' || ch == ':' || ch == ' '));
            if (isSeparatorRow)
                continue;

            rows.Add(cells);
        }

        return rows;
    }
}
