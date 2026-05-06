# dvmig (Dataverse Migrator)

A specialized .NET 9.0 CLI tool for high-fidelity data migration between Dataverse and Dynamics 365 environments.

`dvmig` is designed to handle complex Dataverse data movements, including metadata-aware synchronization, relationship preservation, and automatic retry logic. It features a Terminal User Interface (TUI) for interactive migration tasks and maintenance operations.

## Features

- **High-Fidelity Migration:** Preserves essential metadata and relationships across environments.
- **Audit Preservation:** Uses a plugin (`DMPlugin`) to ensure source environment audit fields (`CreatedOn`, `ModifiedOn`, `CreatedBy`, `ModifiedBy`) are preserved on the target environment.
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
   - **Re-sync:** Ignores sync state and forces an update all records of for chosen entities.

2. **Maintenance (🛠️)**
   - **Install DVMig Components:** Installs required custom entities and plugin the target environment.
   - **Uninstall DVMig Components:** Removes plugin and entities from the target environment.
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
