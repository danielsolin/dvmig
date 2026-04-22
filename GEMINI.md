# GEMINI.md

This file contains foundational mandates, architectural decisions, and project-specific constraints for the `dvmig` project.

## Project Context
- **Objective:** Modernize and improve the legacy `CRM2CRM` migration logic.
- **Source Projects:** Found in the `old/` directory. **This directory is for reference only and MUST NOT be modified.**
- **New Stack:** .NET 9, WPF (App), .NET Standard 2.0 (Plugins), .NET 9 (Core/Providers).

## Foundational Mandates
1.  **Legacy Support:** The tool MUST maintain compatibility with legacy CRM OnPrem platforms (including AD/ADFS/IFD authentication).
2.  **Dual UI Strategy:** The project MUST support two separate interfaces: a simplified graphical UI (WPF-based) for non-technical users, and an advanced Terminal User Interface (TUI) for technical power users.
3.  **Code Style:** Adhere strictly to the rules defined in the global `GEMINI.md`.
4.  **Resiliency:** Use `Polly` for retries on transient network errors or Dataverse throttling (including high-precision `8004410d` handling).
5.  **Logging:** Use `Serilog` for structured logging (file + UI sinks).

## Key Architectural Decisions
- **Multi-Provider Pattern:** Separate providers for Dataverse (modern) and Legacy CRM (source).
- **Parallel Processing:** Use `SemaphoreSlim` to control the degree of parallelism.
- **Dependency Management:** Recursive "Fix and Retry" logic to resolve missing lookups on the fly.
- **Date Preservation:** Use a custom entity (`dm_sourcedate`) and a `DMPlugin` on the target side to override system timestamps.
- **Secure Settings:** Local connection strings are encrypted using Windows DPAPI (`ProtectedData`) with entropy "dvmig-entropy" and stored in `%AppData%\dvmig\settings.json`.

## Testing Environment
- **Source:** `dmrndsrc.crm4.dynamics.com` (Test data environment).
- **Target:** `dmrnd.crm22.dynamics.com` (Clean target environment).

## Tooling & Dependencies
- **UI:** Vanilla WPF for standard application; Spectre.Console for the advanced TUI.
- **SDK:** `Microsoft.PowerPlatform.Dataverse.Client`.
- **Legacy SDK:** `Microsoft.CrmSdk.XrmTooling.CoreAssembly` for OnPrem auth.
- **Resiliency:** `Polly`.
- **Logging:** `Serilog`.

## Future Reference
- Always check `MODERNIZATION.md` for the latest roadmap and strategy updates.
- Refer to `old/CRM2CRM/Core/SyncEngine.cs` for original migration logic.
- Refer to `old/CRM2CRM.Plugins/DatesPlugin.cs` for the original date preservation logic.
or original migration logic.
- Refer to `old/CRM2CRM.Plugins/DatesPlugin.cs` for the original date preservation logic.
