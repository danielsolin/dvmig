# dvmig (Dataverse Migration)

`dvmig` is a modern, robust, and user-friendly tool designed for migrating data from legacy Dynamics CRM OnPremises environments to modern Dynamics 365 / Dataverse cloud environments.

## Overview
Many organizations are "stuck" on outdated CRM OnPrem systems because migration is complex, time-consuming, and prone to error. `dvmig` aims to simplify this process with a modern, high-performance migration engine and an intuitive graphical interface.

## Key Features
- **Legacy Compatibility:** Full support for older OnPrem authentication (AD, ADFS, IFD).
- **Modern UI:** A polished, wizard-based WPF application for a non-intimidating user experience.
- **High Performance:** Parallel processing and bulk operations for faster migrations.
- **Resiliency:** Automatic retries and robust error handling for large-scale data moves.
- **Smart Mapping:** Intelligent lookup resolution and user mapping.
- **Date Preservation:** Preserves `CreatedOn` and `ModifiedOn` timestamps via target-side plugins.

## Architecture
- **`dvmig.App` (WPF):** The modern, inviting user interface.
- **`dvmig.Core`:** The central migration and mapping engine.
- **`dvmig.Providers`:** Specialized adapters for different CRM versions and auth types.
- **`dvmig.Plugins`:** Dataverse-side plugins for metadata preservation.

## Getting Started
(Development in progress)
1. Open `dvmig.sln` in Visual Studio 2022.
2. Build and run the `dvmig.App` project.
3. Follow the connection wizard to start your migration.

## License
(License information to be determined)
