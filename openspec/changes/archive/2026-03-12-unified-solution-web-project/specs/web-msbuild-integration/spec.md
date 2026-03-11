## ADDED Requirements

### Requirement: Web frontend project uses NoTargets SDK

Each web frontend directory SHALL contain a `.csproj` file using `Microsoft.Build.NoTargets` SDK that wraps the npm build lifecycle into MSBuild targets.

#### Scenario: Web.csproj exists in frontend directory

- **WHEN** a Fulora hybrid project is created via `dotnet new agibuild-hybrid --framework react` or `--framework vue`
- **THEN** the generated `MyApp.Web/` directory SHALL contain `MyApp.Web.csproj` using `Microsoft.Build.NoTargets` SDK
- **AND** the `.csproj` SHALL declare `<IsPackable>false</IsPackable>`

#### Scenario: Web.csproj exists in sample frontend directory

- **WHEN** a sample project (e.g., `avalonia-react`) is present in the repository
- **THEN** the sample's web directory (e.g., `AvaloniReact.Web/`) SHALL contain a `.csproj` using `Microsoft.Build.NoTargets` SDK

### Requirement: Web project is included in .sln

The solution file SHALL include the Web project alongside Desktop, Bridge, and Tests projects.

#### Scenario: Template solution includes Web project

- **WHEN** `dotnet new agibuild-hybrid --framework react` generates a solution
- **THEN** the `.sln` file SHALL contain a project entry for `MyApp.Web.csproj`
- **AND** the project SHALL be listed in both Debug and Release solution configurations

#### Scenario: Sample solution includes Web project

- **WHEN** a sample `.sln` file is opened (e.g., `AvaloniReact.sln`)
- **THEN** the solution SHALL contain a project entry for the sample's Web `.csproj`

### Requirement: Incremental npm install

The Web project SHALL run `npm ci` only when `package.json` has changed since the last successful install.

#### Scenario: First build runs npm ci

- **WHEN** `dotnet build` is executed on a solution containing a Web project
- **AND** `node_modules/.install-stamp` does not exist
- **THEN** `npm ci` SHALL execute in the Web project directory
- **AND** a stamp file `node_modules/.install-stamp` SHALL be created

#### Scenario: Subsequent build skips npm ci

- **WHEN** `dotnet build` is executed on a solution containing a Web project
- **AND** `package.json` has not changed since `node_modules/.install-stamp` was created
- **THEN** `npm ci` SHALL NOT execute

#### Scenario: npm ci re-runs when package.json changes

- **WHEN** `package.json` is modified after the last `node_modules/.install-stamp`
- **AND** `dotnet build` is executed
- **THEN** `npm ci` SHALL execute again
- **AND** the stamp file SHALL be updated

### Requirement: Incremental web production build

The Web project SHALL run `npm run build` only in Release configuration, and only when source files have changed.

#### Scenario: Release build produces dist output

- **WHEN** `dotnet build -c Release` is executed on a solution containing a Web project
- **THEN** `npm run build` SHALL execute in the Web project directory
- **AND** the output SHALL be placed in the `dist/` directory
- **AND** a stamp file `dist/.build-stamp` SHALL be created

#### Scenario: Debug build does not run npm build

- **WHEN** `dotnet build` (Debug configuration) is executed on a solution containing a Web project
- **THEN** `npm run build` SHALL NOT execute

#### Scenario: Release build skips when source unchanged

- **WHEN** `dotnet build -c Release` is executed
- **AND** no files in `src/`, `package.json`, `vite.config.ts`, or `tsconfig.json` have changed since `dist/.build-stamp`
- **THEN** `npm run build` SHALL NOT execute

### Requirement: Desktop project references Web for build order

The Desktop `.csproj` SHALL reference the Web project to ensure correct build ordering, without referencing its output assembly.

#### Scenario: ProjectReference ensures build order

- **WHEN** `dotnet build` is executed on the solution
- **THEN** the Web project SHALL build before the Desktop project
- **AND** the Desktop project SHALL NOT reference any assembly from the Web project

#### Scenario: Desktop embeds web dist in Release

- **WHEN** `dotnet build -c Release` is executed
- **THEN** the Desktop project SHALL embed files from `Web/dist/**` as `EmbeddedResource` under `wwwroot`

### Requirement: Desktop does not contain inline web build targets

The Desktop `.csproj` SHALL NOT contain any MSBuild targets that execute npm commands directly.

#### Scenario: No BuildWebApp target in Desktop

- **WHEN** a Desktop `.csproj` is inspected (in templates or samples)
- **THEN** it SHALL NOT contain a `<Target Name="BuildWebApp">` or any `<Exec Command="npm ...">` element

### Requirement: Template renames web directory to clean name

The `agibuild-hybrid` template SHALL rename framework-specific web directories to `HybridApp.Web` so that after `sourceName` replacement, users get `MyApp.Web`.

#### Scenario: React template produces MyApp.Web

- **WHEN** `dotnet new agibuild-hybrid -n MyApp --framework react` is executed
- **THEN** the generated project SHALL contain `MyApp.Web/` (not `MyApp.Web.Vite.React/`)
- **AND** `MyApp.Web/MyApp.Web.csproj` SHALL exist

#### Scenario: Vue template produces MyApp.Web

- **WHEN** `dotnet new agibuild-hybrid -n MyApp --framework vue` is executed
- **THEN** the generated project SHALL contain `MyApp.Web/` (not `MyApp.Web.Vite.Vue/`)
- **AND** `MyApp.Web/MyApp.Web.csproj` SHALL exist

#### Scenario: Vanilla template has no Web project

- **WHEN** `dotnet new agibuild-hybrid -n MyApp --framework vanilla` is executed
- **THEN** the generated project SHALL NOT contain a `MyApp.Web/` directory or Web `.csproj`
- **AND** the `.sln` SHALL NOT reference a Web project

### Requirement: Cross-platform compatibility

The Web `.csproj` and its MSBuild targets SHALL work correctly on Windows, macOS, and Linux.

#### Scenario: Build succeeds on macOS

- **WHEN** `dotnet build` is executed on macOS with Node.js installed
- **THEN** the Web project SHALL build without errors

#### Scenario: Build succeeds on Linux

- **WHEN** `dotnet build` is executed on Linux with Node.js installed
- **THEN** the Web project SHALL build without errors

#### Scenario: Build succeeds on Windows

- **WHEN** `dotnet build` is executed on Windows with Node.js installed
- **THEN** the Web project SHALL build without errors
