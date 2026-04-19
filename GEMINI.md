# GEMINI.md

This file contains foundational mandates, architectural decisions, and project-specific constraints for the `dvmig` project.

## Project Context
- **Objective:** Modernize and improve the legacy `CRM2CRM` migration logic.
- **Source Projects:** Found in the `old/` directory (for reference only).
- **New Stack:** .NET 9, WPF (App), .NET Standard 2.0 (Plugins), .NET 9 (Core/Providers).

## Foundational Mandates
1.  **Legacy Support:** The tool MUST maintain compatibility with legacy CRM OnPrem platforms (including AD/ADFS/IFD authentication).
2.  **Graphical UI:** The UI MUST remain graphical and inviting for non-technical users (WPF-based).
3.  **Code Style:** 
    - No line should exceed **80 characters** in width.
    - Follow standard C# naming conventions and clean architecture principles.
    - No one-line if-statements; minimum is two lines (even without brackets).
    - Always one empty line before return statements, unless the return is the 
      single statement of an if-block.
4.  **Resiliency:** Use `Polly` for retries on transient network errors or Dataverse throttling.
5.  **Logging:** Use `Serilog` for structured logging (file + UI sinks).

## Key Architectural Decisions
- **Multi-Provider Pattern:** Separate providers for Dataverse (modern) and Legacy CRM (source).
- **Parallel Processing:** Use `SemaphoreSlim` to control the degree of parallelism.
- **Dependency Management:** Multi-pass migration or dependency graphing to resolve lookups.
- **Date Preservation:** Use a custom entity (`dm_sourcedate`) and a `DatesPlugin` on the target side to override system timestamps.

## Tooling & Dependencies
- **UI:** MaterialDesignInXaml or MahApps.Metro.
- **SDK:** `Microsoft.PowerPlatform.Dataverse.Client`.
- **Legacy SDK:** May require older `Microsoft.Xrm.Sdk` versions for specific OnPrem auth.
- **Resiliency:** `Polly`.
- **Logging:** `Serilog`.
- **Progress:** `Spectre.Console` (if any CLI fallback is added) or custom WPF progress controls.

## Future Reference
- Always check `MODERNIZATION.md` for the latest roadmap and strategy updates.
- Refer to `old/CRM2CRM/Core/SyncEngine.cs` for original migration logic.
- Refer to `old/CRM2CRM.Plugins/DatesPlugin.cs` for the original date preservation logic.
