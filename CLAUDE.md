# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**dvmig** is a Dataverse migration tool that modernizes legacy CRM2CRM migration logic. It supports migration from legacy CRM OnPrem platforms (including AD/ADFS/IFD authentication) to modern Dataverse environments.

### Architecture

The solution follows a modular, service-oriented architecture with clear separation of concerns:

- **dvmig.Core**: Core business logic including the sync engine, data preservation, metadata services, provisioning, and Dataverse providers (`IDataverseProvider`, `DataverseProvider`, `LegacyCrmProvider`)
- **dvmig.Plugins**: .NET Standard 2.0 plugins deployed to target Dataverse for date preservation (`dm_sourcedate` entity)
- **dvmig.Cli**: Terminal User Interface (TUI) using Spectre.Console - **primary interface for V1.0**
- **dvmig.App**: WPF application using MVVM pattern - maintained but not actively developed for V1.0
- **dvmig.Tests**: Unit tests using xUnit and Moq

### Key Design Patterns

- **Multi-Provider Pattern**: Separate providers for Dataverse (modern) and Legacy CRM (source) via `IDataverseProvider`
- **Dependency Resolution**: Recursive "Fix and Retry" logic in `SyncEngine` to resolve missing lookups on the fly
- **Parallel Processing**: `SemaphoreSlim` controls degree of parallelism during migrations
- **State Tracking**: `LocalFileStateTracker` persists migration progress for resumability
- **Secure Settings**: Connection strings encrypted with Windows DPAPI (entropy: "dvmig-entropy") stored in `%AppData%\dvmig\settings.json`

## Development Commands

### Building

```bash
dotnet build dvmig.sln
```

### Running Tests

```bash
# Run all tests
dotnet test tests/dvmig.Tests/dvmig.Tests.csproj

# Run specific test
dotnet test tests/dvmig.Tests/dvmig.Tests.csproj --filter "FullyQualifiedName~SyncEngineTests"
```

### Running Applications

```bash
# CLI/TUI (primary interface)
dotnet run --project src/dvmig.Cli/dvmig.Cli.csproj

# WPF App (maintained but not prioritized for V1.0)
dotnet run --project src/dvmig.App/dvmig.App.csproj
```

## Code Style Requirements

- **Line Length**: Maximum 80 characters for C# files (enforced via .editorconfig)
- **Indentation**: 4 spaces, UTF-8, LF line endings
- **Nullable Reference Types**: Enabled throughout
- **Using Directives**: System usings first, no separate import directive groups

## Critical Constraints

1. **Legacy Support**: MUST maintain compatibility with legacy CRM OnPrem platforms
2. **V1.0 Priority**: Focus development on CLI/TUI (`dvmig.Cli`) - WPF App is maintenance-only
3. **Resiliency**: Use `Polly` for retries on transient errors and Dataverse throttling (including `8004410d` handling)
4. **Logging**: Use `Serilog` for structured logging (file + UI sinks)
5. **Date Preservation**: Requires custom `dm_sourcedate` entity and `DMPlugin` on target environment

## Testing Environment

- **Source**: `dmrndsrc.crm4.dynamics.com` (test data)
- **Target**: `dmrnd.crm22.dynamics.com` (clean target)

## Important Notes

- The `old/` directory contains legacy source projects for reference only - **DO NOT MODIFY**
- Shared code (e.g., `EntityMetadataHelper.cs`, `SchemaConstants.cs`) lives in `src/shared/` and is linked into multiple projects
- Plugin assembly must be signed (`dvmig.snk`) for deployment to Dataverse
- Metadata caching and ID mapping are critical for performance during large migrations