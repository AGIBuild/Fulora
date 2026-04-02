# Documentation Site Deployment

The API reference and guides are built with [docfx](https://dotnet.github.io/docfx/) and deployed to GitHub Pages via the dedicated `.github/workflows/docs.yml` workflow.

## How it works

Documentation deployment uses an independent workflow:

1. `docs.yml` triggers on `push` to `main` when changed files match `docs/**`.
2. The workflow builds `docs/docfx.json`.
3. The built site is uploaded as an artifact.
4. A separate deploy job publishes the artifact to GitHub Pages.

This means docs-only changes on `main` can deploy the docs site without going through the release promotion path.

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
