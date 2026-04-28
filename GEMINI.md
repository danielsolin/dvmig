# GEMINI.md

## Project Overview

**dvmig (Dataverse Migrator)** is a specialized tool for migrating data from legacy Microsoft CRM On-Premise environments (AD, ADFS, IFD) to modern Microsoft Dataverse environments. It focuses on maintaining data integrity, preserving audit dates, and resolving complex record dependencies during the migration process.

### Core Architecture

The solution is built on .NET 9.0 and follows a service-oriented architecture:

-   **dvmig.Core**: The engine room. Contains the `SyncEngine`, Dataverse providers (`LegacyCrmProvider`, `DataverseProvider`), metadata services, and resiliency logic.
-   **dvmig.Cli**: The primary interface for V1.0. A Terminal User Interface (TUI) built with **Spectre.Console**.
-   **dvmig.Plugins**: .NET Standard 2.0 plugins for Dataverse that facilitate date preservation via the `dm_sourcedate` entity.
-   **dvmig.App**: A WPF application (MVVM) maintained for legacy reasons but not the primary focus.
-   **src/dvmig.Core/Shared**: Shared code for metadata constants and helpers under the `dvmig.Core.Shared` namespace.

### Key Technical Features

-   **Fix and Retry Dependency Resolution**: The `SyncEngine` uses a recursive strategy (up to 3 levels) to identify and sync missing lookup dependencies on-the-fly when a record creation fails.
-   **Date Preservation**: Custom logic and plugins allow for preserving `createdon` and `modifiedon` dates from the source system.
-   **Resiliency**: Integrated **Polly** retry policies to handle transient network issues and Dataverse service protection limits.
-   **State Tracking**: Persistent `LocalFileStateTracker` ensures migrations can be resumed from the last successful record.
-   **Security**: Connection strings are encrypted using **Windows DPAPI** (entropy: "dvmig-entropy") and stored in `%AppData%\dvmig\settings.json`.

---

## Building and Running

### Prerequisites
-   .NET 9.0 SDK
-   Windows OS (required for DPAPI and WPF)

### Commands

| Task | Command |
| :--- | :--- |
| **Build Solution** | `dotnet build dvmig.sln` |
| **Run CLI (Primary)** | `dotnet run --project src/dvmig.Cli/dvmig.Cli.csproj` |
| **Run WPF App** | `dotnet run --project src/dvmig.App/dvmig.App.csproj` |
| **Run All Tests** | `dotnet test tests/dvmig.Tests/dvmig.Tests.csproj` |
| **Run Specific Test** | `dotnet test tests/dvmig.Tests/dvmig.Tests.csproj --filter "Name~SearchTerm"` |

---

## Development Conventions

### Code Style (Strictly Enforced)
-   **Line Width**: Maximum **80 characters**. This is a hard limit.
-   **Indentation**: 3 spaces (no tabs).
-   **Line Endings**: LF (Line Feed).
-   **Naming**: Standard C# PascalCase for classes/methods, camelCase for private fields (prefixed with `_`).
-   **List Formatting**: For multi-line lists (arguments, initializers), place every item AND the closing brace/bracket on its own line.
-   **Control Flow**: No one-line `if`, `foreach`, or `while` statements. Omit curly braces only if the body is a single statement.

### Architecture Guidelines
-   **Nullable Reference Types**: MUST be enabled and respected throughout the solution.
-   **Interface Segregation**: Prefer small, focused interfaces in `dvmig.Core.Interfaces`.
-   **Dependency Injection**: Use constructor injection for all services.
-   **Logging**: Use `Serilog` for structured logging. Avoid `Console.WriteLine` in core logic.

### Migration Specifics
-   **Schema Constants**: Always use `Constants.cs` (in `dvmig.Core.Shared`) for entity and attribute logical names.
-   **Provider Abstraction**: Never use `IOrganizationService` directly in the `SyncEngine`; use `IDataverseProvider`.
-   **Failure Handling**: Records that fail to sync must be logged via `IFailureLogger` to the `dm_migrationfailure` entity on the target.
