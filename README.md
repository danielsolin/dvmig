# dvmig (Dataverse Migrator)

`dvmig` is a specialized tool for high-fidelity data migration between 
Dataverse and Dynamics 365 environments. The solution consists of:

- `src/dvmig.Cli`: A .NET 9.0 CLI application built with `Spectre.Console` 
  that orchestrates user interactions, configuration, and migration tasks.
- `src/dvmig.Core`: A .NET Standard 2.0 library handling the migration 
  engine, Dataverse connectivity (`IDataverseProvider`), resilient 
  synchronization logic (`Polly`), local state tracking, and schema validation.
- `src/dvmig.Plugins`: A Class Library containing a Dataverse plugin 
  (`DMPlugin`) deployed to the target environment to preserve audit fields.
- `src/dvmig.Tests`: A unit testing project using `xUnit`, `Moq`, and `Bogus`.

## Features

- **High-Fidelity Migration:** Preserves essential metadata and relationships.
- **Audit Preservation:** Uses an auto-deployed plugin to ensure presevation
  of the `CreatedOn` and `ModifiedOn` fields. The `CreatedBy` and `ModifiedBy`
  fields are preserved by impersonation in `dvmig.Core`.
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

## Architecture

- **`src/dvmig.Cli`**: The entry point. Handles TUI, dependency injection 
  setup, and user input orchestration.
- **`src/dvmig.Core`**: The engine room. Contains `SyncEngine`, providers, 
  retry policies, and domain logic.
- **`src/dvmig.Plugins`**: Contains `DMPlugin`. Intercepts `Create`/`Update` 
  events to manipulate audit data.
- **`src/dvmig.Tests`**: The xUnit test suite.
