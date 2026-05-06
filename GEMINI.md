# Project Overview: dvmig (Dataverse Migrator)

`dvmig` is a .NET 9.0-based CLI application designed for high-fidelity data migration between Dataverse and Dynamics 365 environments. It facilitates metadata-aware synchronization, relationship preservation, and automatic retry logic for transient errors.

The architecture is divided into three primary components:
- **`src/dvmig.Cli`**: A Terminal User Interface (TUI) built with `Spectre.Console` that orchestrates user interactions, configuration, and migration tasks.
- **`src/dvmig.Core`**: The main migration engine handling Dataverse connectivity (`IDataverseProvider`, `ServiceClient`), resilient synchronization logic (`Polly`, `ISyncEngine`), local state tracking, schema validation, and logging (`Serilog`).
- **`src/dvmig.Plugins`**: A Class Library containing a Dataverse plugin (`DMPlugin`). This plugin is deployed to the target environment to preserve crucial source audit fields (like `CreatedOn`, `ModifiedOn`, `CreatedBy`) during record creation and updates.
- **`src/dvmig.Tests`**: A unit testing project using `xUnit`, `Moq`, and `Bogus` to validate core behavior.

## Building and Running

**Build the solution:**
```powershell
dotnet build
```

**Run the CLI application:**
```powershell
dotnet run --project src/dvmig.Cli
```
*(Alternatively, you can run the `rr.bat` script in the root directory)*

**Run tests:**
```powershell
dotnet test
```

**Publish:**
A PowerShell script is available for minimal, single-file releases:
```powershell
./publish-minimal.ps1
```

## Development Conventions

Adhere strictly to the project's `.editorconfig` rules and established coding patterns:

### C# Coding Style
- **Indentation:** 3 spaces strictly. No tabs.
- **Line Length:** Maximum 80 characters per line. Break long statements appropriately.
- **Braces:** Omit curly braces `{}` for `if`, `foreach`, and `while` statements if the body consists of a single line.
- **Usings:** `System` using directives must be sorted first. Always remove unused usings.
- **Line spacing:** Always leave one empty line before `return` statements, unless the return is the single statement of an `if` block.
- **Argument Lists:** Use "Hanging Indent" (or Allman-Adjacent) for multiline argument lists or collection initializers. If wrapped, every item AND the closing parenthesis/bracket must be on its own line.

### Project Specific Standards
- **Naming Conventions:** Use standard C# conventions. The core services follow the "Service/Provider" pattern (e.g., `UserService`, `DataverseProvider`, `SyncEngine`). 
- **Dataverse Interaction:** Application logic should always interact with Dataverse via the abstracted `IDataverseProvider` rather than utilizing `ServiceClient` directly.
- **Schema Definitions:** All Dataverse logical names, custom attribute names, and specific error code strings must be defined and referenced from `src/dvmig.Core/Shared/SystemConstants.cs`. Magic strings for schemas are forbidden.
- **Custom Entities:** The tool relies on custom entities (`dm_sourcedata` for holding timestamps during plugin execution and `dm_migrationfailure` for error logging in the target environment).
- **UI Interaction:** All console outputs and prompts should utilize `Spectre.Console` via the wrappers in `dvmig.Cli` (like `CliUI`). Avoid raw `Console.WriteLine` usage to maintain theme consistency.
