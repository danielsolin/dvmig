# CLI GUIDE

<figure>
  <img src="/assets/img/sync-recommended-run.png" width="400px" />
</figure>

You can either download a binary release or clone the repo and build it
yourself.

* Download:
   * Get the latest [release](https://github.com/danielsolin/dvmig/releases),
   * Unzip it, right-click `dvmig.Cli.exe` and select "Properties". Check
     "Unblock" at the bottom of the "General" tab and click "Ok".
   * Double-click `dvmig.Cli.exe`.

* Build:
   ```console
   # Clone the repository
   git clone https://github.com/danielsolin/dvmig.git
   cd dvmig

   # Build the solution
   dotnet restore
   dotnet build

   # Run
   dotnet run --project src/dvmig.Cli
   ```

### Usage

When running the app for the first time, start by selecting "Configuration"
on the main menu to set connection strings for Source and Target environments.

Example connection string:

```code
# Will open a login window in your default browser:
AuthType=OAuth;Url=https://<your-instance>.crm.dynamics.com;RedirectUri=http://localhost/;LoginPrompt=Auto;
```

You can also test the connection strings in the app to make sure they work.

### Main Menu

**Synchronization (🚀)**
   - **Sync Recommended:** Synchronizes `Account`, `Contact`, `Task`, `Email`,
     `PhoneCall` and `Appointment`.
   - **Sync Selected:** Allows manual entity selection.
   - **Sync View:** Sync records in a view.
   - **Re-sync:** Ignores sync state and forces an update of all records.

**Maintenance (🛠️)**
   - **Install dvmig Components:** Installs custom entities and plugin.
   - **Uninstall dvmig Components:** Removes plugin and entities.
   - **View Recorded Migration Failures:** Lists failure logs.

**Data Management (🧪)**
   - **Generate Sample Data:** Seeds the source environment with mock
     data.
   - **Wipe Data (Source/Target):** Purges data from environments.

**Settings**
   - Define connection strings, max threads for parallelism etc.

