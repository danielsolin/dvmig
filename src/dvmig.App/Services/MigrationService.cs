using dvmig.Providers;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace dvmig.App.Services
{
    public interface IMigrationService
    {
        Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default
        );

        Task<List<EntityMetadata>> GetSourceEntitiesAsync(
            CancellationToken ct = default
        );

        void DisconnectSource();

        void DisconnectTarget();

        IDataverseProvider? SourceProvider { get; }
        IDataverseProvider? TargetProvider { get; }

        List<string> SelectedEntities { get; }

        bool IsStandardEntity(string logicalName);
    }

    public class MigrationService : IMigrationService
    {
        private List<EntityMetadata>? _cachedMetadata;

        public IDataverseProvider? SourceProvider { get; private set; }

        public IDataverseProvider? TargetProvider { get; private set; }

        public List<string> SelectedEntities { get; } = new List<string>();

        public void DisconnectSource()
        {
            SourceProvider = null;
            _cachedMetadata = null;
        }

        public void DisconnectTarget()
        {
            TargetProvider = null;
        }

        public async Task<bool> ConnectSourceAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            try
            {
                SourceProvider = isLegacy
                    ? await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new LegacyCrmProvider(connectionString);
                        },
                        ct
                    )
                    : await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new DataverseProvider(connectionString);
                        },
                        ct
                    );

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ConnectTargetAsync(
            string connectionString,
            bool isLegacy,
            CancellationToken ct = default)
        {
            try
            {
                TargetProvider = isLegacy
                    ? await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new LegacyCrmProvider(connectionString);
                        },
                        ct
                    )
                    : await Task.Run(
                        () =>
                        {
                            ct.ThrowIfCancellationRequested();

                            return new DataverseProvider(connectionString);
                        },
                        ct
                    );

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> TestConnectionAsync(
            string connectionString,
            bool isLegacy)
        {
            // Deprecated, using ConnectSource/ConnectTarget
            return Task.FromResult(false);
        }

        public async Task<List<EntityMetadata>> GetSourceEntitiesAsync(
            CancellationToken ct = default)
        {
            if (SourceProvider == null)
            {
                return new List<EntityMetadata>();
            }

            if (_cachedMetadata != null)
            {
                return _cachedMetadata;
            }

            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveAllEntitiesResponse)await SourceProvider
                .ExecuteAsync(request, ct);

            _cachedMetadata = response.EntityMetadata
                .OrderBy(e =>
                    e.DisplayName?.UserLocalizedLabel?.Label ??
                    e.LogicalName
                )
                .ToList();

            return _cachedMetadata;
        }

        public bool IsStandardEntity(string logicalName)
        {
            var standard = new[]
            {
                "account",
                "activitypointer",
                "appointment",
                "assettable",
                "bookableresource",
                "bookableresourcebooking",
                "businessunit",
                "campaign",
                "category",
                "competitor",
                "contact",
                "custtable",
                "email",
                "entitlement",
                "equipment",
                "goal",
                "incident",
                "inventlocation",
                "inventsite",
                "inventtable",
                "invoice",
                "kbarticle",
                "knowledgearticle",
                "lead",
                "list",
                "mainaccount",
                "msdyn_agreement",
                "msdyn_customerasset",
                "msdyn_expense",
                "msdyn_project",
                "msdyn_projecttask",
                "msdyn_timeentry",
                "msdyn_workorder",
                "msdyncrm_customerjourney",
                "msdyncrm_marketingemail",
                "msdyncrm_segment",
                "msevtmgt_event",
                "opportunity",
                "phonecall",
                "pricelevel",
                "product",
                "purchtable",
                "queue",
                "quote",
                "salesorder",
                "salestable",
                "sla",
                "subject",
                "systemuser",
                "task",
                "team",
                "vendtable"
            };

            return standard.Contains(logicalName.ToLower());
        }
    }
}
