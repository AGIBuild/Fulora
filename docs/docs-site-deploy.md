# Documentation Site Deployment

The API reference and guides are built with [docfx](https://dotnet.github.io/docfx/) and deployed to GitHub Pages as part of the unified CI/Release pipeline.

## How it works

Documentation deployment is integrated into the unified `ci.yml` workflow:

1. Code is pushed to `main` and the CI stage builds and tests across three platforms (macOS, Windows, Linux).
2. After CI passes, the **Release Promotion** job requires manual approval via the `release` GitHub environment.
3. Once approved, release publishes NuGet/npm packages, creates a Git tag and GitHub Release.
4. The **Deploy Documentation** job runs after release, building the docfx site and deploying it to GitHub Pages.

This ensures documentation is always in sync with the published release — no docs deploy without a successful release.

## GitHub Pages setup (one-time)

1. Go to **Settings → Pages** in the GitHub repository.
2. Under **Build and deployment → Source**, select **GitHub Actions**.
3. No branch selection is needed — the workflow handles deployment via the `github-pages` environment.
4. The site will be available at `https://<org>.github.io/Agibuild.Fulora/` after the first successful deploy.

## Building locally

```bash
# Restore tools (includes docfx)
dotnet tool restore

# Build the docs site
dotnet docfx docs/docfx.json

# Serve locally for preview
dotnet docfx serve docs/_site
```

The site output is in `docs/_site/` (gitignored).

## What the site includes

| Section | Source | Description |
|---------|--------|-------------|
| API Reference | XML docs from `src/` projects | Auto-generated from `///` comments |
| Getting Started | `docs/articles/getting-started.md` | Quick start guide |
| Bridge Guide | `docs/articles/bridge-guide.md` | Type-safe bridge usage |
| SPA Hosting | `docs/articles/spa-hosting.md` | Embedded SPA hosting |
| Architecture | `docs/articles/architecture.md` | System architecture overview |
| AI Integration | `docs/ai-integration-guide.md` | AI chat with streaming bridge |
| Plugin Authoring | `docs/plugin-authoring-guide.md` | Create and publish bridge plugins |

## Troubleshooting

- **Pages not deploying**: Verify that GitHub Pages source is set to "GitHub Actions" in repository settings.
- **Missing API docs**: Ensure `GenerateDocumentationFile` is enabled in `Directory.Build.props` for product projects.
