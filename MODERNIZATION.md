# dvmig (Dataverse Migration) Modernization Strategy

## Overview
The goal of `dvmig` is to provide a robust, modern, and user-friendly solution for migrating data from legacy Dynamics CRM OnPremises environments to modern Dynamics 365 / Dataverse cloud environments.

## Core Mandates
1.  **Legacy Compatibility:** Must maintain full support for older OnPrem authentication (AD, ADFS, IFD) to ensure "stuck" customers can migrate.
2.  **User Accessibility:** The UI must be graphical, inviting, and non-intimidating for non-technical users.
3.  **Technical Robustness:** High performance (parallelism), resiliency (retries), and modern .NET standards.

## Proposed Architecture

### 1. Solution Structure (C# / .NET 8)
- **`dvmig.App` (WPF):** 
  - Modern UI (Material Design / MahApps).
  - Wizard-based migration flow.
  - Real-time progress dashboard.
- **`dvmig.Core`:** 
  - `SyncEngine`: Optimized migration loop with parallel processing.
  - `MappingEngine`: Handles attribute mapping, transformation, and lookup resolution.
  - `DependencyResolver`: Intelligent ordering of entities and records.
- **`dvmig.Providers`:**
  - `DataverseProvider`: Modern SDK (`Microsoft.PowerPlatform.Dataverse.Client`).
  - `LegacyProvider`: Legacy-compatible connection logic for older CRM versions.
- **`dvmig.Plugins`:**
  - Modernized `DatesPlugin` for target date preservation.

### 2. Modernization Key Features

#### A. Enhanced Performance
- **Parallel Processing:** Configurable degree of parallelism for record creation.
- **Bulk Operations:** Implementation of `ExecuteMultipleRequest` where beneficial.
- **Efficient Querying:** Optimized FetchXML and paging for large datasets.

#### B. Robustness & Resiliency
- **Polly Integration:** Automatic retries for transient failures and throttling (HTTP 429).
- **Comprehensive Logging:** Structured logging (Serilog) with UI and File outputs.
- **Detailed Error Reporting:** Capture and present specific Dataverse errors in a user-readable format.

#### C. Smart Logic
- **Lookup Resolution:** Multi-pass migration or dependency graphing to handle complex lookup chains.
- **User Mapping:** Improved auto-mapping of users via email, domain name, or full name.
- **Date Preservation:** Improved "SourceDate" helper pattern with automated cleanup options.

## UI/UX Roadmap
1.  **Welcome Screen:** Environment type selection (Source/Target).
2.  **Connection Wizard:** Guided credentials entry with "Test Connection" validation.
3.  **Entity Selector:** Searchable list of entities with recommended order.
4.  **Field Mapper:** Visual mapping of attributes with data type validation.
5.  **Migration Dashboard:** Rich progress bars, success/fail counters, and live log view.

## Legacy Compatibility Notes
- **Source Connection:** Will leverage older CRM SDK libraries if necessary to maintain NTLM/Kerberos/ADFS support for OnPrem versions (2011-2016).
- **Metadata Handling:** Robust handling of deprecated attribute types or platform-specific behaviors.
