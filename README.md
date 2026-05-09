# dvmig (Dataverse Migrator)

Tool for data migration between Dataverse / Dynamics 365 environments.

- `src/dvmig.Cli`: .NET 9.0 CLI application built with `Spectre.Console`.
- `src/dvmig.Core`: .NET Standard 2.0 library containing the migration logic.
- `src/dvmig.Plugins`: Dataverse plugin for preserving audit fields.
- `src/dvmig.Tests`: Unit test project using `xUnit`, `Moq`, and `Bogus`.

## Features

- **High-Fidelity Migration:** Preserves essential metadata and relationships.
- **Audit Preservation:** Uses an auto-deployed plugin to ensure presevation
  of the `CreatedOn` and `ModifiedOn` fields. `CreatedBy` and `ModifiedBy`
  are preserved by auto-mapped impersonation (users are mapped between source
  and target environments by either full name or email).
- **Robust Synchronization:** Built with resilience in mind using `Polly` for 
  handling transient errors and automatic retry strategies.
- **Interactive TUI:** Powered by `Spectre.Console` for easy orchestration.
- **Resilient State Tracking:** Locally tracks successfully migrated records 
  to support resuming interrupted synchronizations.
- **Error Logging:** Logs detailed failures in the target environment via 
  the `dm_migrationfailure` custom entity.

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Source and Target Dataverse/Dynamics 365 environments.

## Installation / Building

Clone the repository and build the solution:

```powershell
# Clone the repository
git clone <repository_url>
cd dvmig

# Build the solution
dotnet build
```

## Usage

You can run the application directly using the .NET CLI from the root 
directory:

```powershell
dotnet run --project src/dvmig.Cli
```

### Application Menus

Once launched, you will be greeted by the interactive TUI:

1. **Synchronization (🚀)**
   - **Sync Recommended:** Synchronizes a curated list of entities.
   - **Sync Selected:** Allows manual entity selection.
   - **Re-sync:** Ignores sync state and forces an update of all records.

2. **Maintenance (🛠️)**
   - **Install DVMig Components:** Installs custom entities and plugin.
   - **Uninstall DVMig Components:** Removes plugin and entities.
   - **View Recorded Migration Failures:** Reads failure logs.

3. **Data Management (🧪)**
   - **Generate Sample Data:** Seeds the source with mock data.
   - **Wipe Data (Source/Target):** Purges data from environments.
