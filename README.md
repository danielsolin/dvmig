# dvmig (Dataverse Migrator)

A specialized .NET 9.0 CLI tool for high-fidelity data migration between Dataverse and Dynamics 365 environments.

`dvmig` is designed to handle complex Dataverse data movements, including metadata-aware synchronization, relationship preservation, and automatic retry logic. It features a Terminal User Interface (TUI) for interactive migration tasks and maintenance operations.

## Features

- **High-Fidelity Migration:** Preserves essential metadata and relationships across environments.
- **Audit Preservation:** Uses a specialized Dataverse plugin (`DMPlugin`) to ensure source environment audit fields (`CreatedOn`, `ModifiedOn`, `CreatedBy`) are preserved on the target environment.
- **Robust Synchronization:** Built with resilience in mind using `Polly` for handling transient Dataverse errors, automatic retry strategies, and handling of dependencies/recursion.
- **Interactive TUI:** A rich, interactive console interface powered by `Spectre.Console` for easy orchestration of synchronization and maintenance tasks.
- **Resilient State Tracking:** Locally tracks successfully migrated records to support resuming interrupted synchronizations.
- **Error Logging:** Logs detailed migration failures directly in the target environment via the `dm_migrationfailure` custom entity for easy reconciliation.

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

Alternatively, to publish a minimal, single-file executable, you can run the provided deployment script:

```powershell
./publish-minimal.ps1
```

## Configuration

Before running the tool, you will need to configure your environment connections. The application will prompt you or look for a `settings.json` file in its working directory to establish connections to the source and target environments via `ServiceClient`.

## Usage

You can run the application directly using the .NET CLI from the root directory by specifying the CLI project:

```powershell
dotnet run --project src/dvmig.Cli
```

### Application Menus

Once launched, you will be greeted by the interactive TUI. The main menus include:

1. **Synchronization (🚀)**
   - **Sync Recommended:** Synchronizes a curated list of non-system standard entities.
   - **Sync Selected:** Allows you to manually pick which entities to synchronize.
   - **Re-sync:** Ignores local state tracking and forces a fresh synchronization for chosen entities.

2. **Maintenance (🛠️)**
   - **Install DVMig Components:** Installs required custom entities (`dm_sourcedata`, `dm_migrationfailure`) and the `DMPlugin` on the target environment. **(Must be run before first sync)**.
   - **Uninstall DVMig Components:** Removes all deployed structural components from the target.
   - **View Recorded Migration Failures:** Read the failure logs stored in the target environment.

3. **Data Management (🧪)**
   - **Generate Sample Data:** Seeds the source environment with mock data for testing.
   - **Wipe Data (Source/Target):** Dangerously purges data from the respective environments. Use with extreme caution.

## Architecture & Project Structure

- **`src/dvmig.Cli`**: The entry point. Handles the TUI, dependency injection setup, and user input orchestration.
- **`src/dvmig.Core`**: The engine room. Contains the `SyncEngine`, Dataverse providers, retry policies, settings management, and the core domain logic.
- **`src/dvmig.Plugins`**: A Class Library containing `DMPlugin`. This plugin is deployed to the target Dataverse environment to intercept `Create`/`Update` events and manipulate audit data fields securely.
- **`src/dvmig.Tests`**: The xUnit test suite containing tests built using Moq and Bogus.

## Core Concepts

### Audit Preservation (DMPlugin)
Dataverse normally overwrites fields like `CreatedOn` when a record is created via the API. `dvmig` circumvents this by using a target-side plugin. During synchronization, original audit values are temporarily stored in the `dm_sourcedata` entity, which the plugin reads and applies during the main entity's transaction.

### Local File State Tracker
To ensure resilience against network drops or timeouts, `dvmig` records the IDs of successfully migrated items locally (in a `state` folder). If the process is restarted, it will skip records that have already been migrated unless a "Re-sync" action is explicitly chosen.

## License & Support
Internal Tooling. For support, check the internal documentation or contact the maintainers.