using dvmig.Providers;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Serilog;

namespace dvmig.Core.Provisioning
{
    /// <summary>
    /// Manages the creation of required schema components for migration.
    /// </summary>
    public class SchemaManager : ISchemaManager
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SchemaManager"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public SchemaManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task CreateSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            // 1. dm_sourcedate
            await EnsureSourceDateEntityAsync(target, progress, ct);

            // 2. dm_migrationfailure
            await EnsureFailureLogEntityAsync(target, progress, ct);

            _logger.Information("Publishing changes...");
            progress?.Report("Publishing changes...");

            await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

            _logger.Information("Schema creation completed.");
            progress?.Report("Schema creation completed.");
        }

        private async Task EnsureSourceDateEntityAsync(
            IDataverseProvider target,
            IProgress<string>? progress,
            CancellationToken ct
        )
        {
            var existingMeta = await target.GetEntityMetadataAsync(
                "dm_sourcedate",
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information("Creating 'dm_sourcedate' entity...");
                progress?.Report("Creating 'dm_sourcedate' entity...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "dm_sourcedate",
                        LogicalName = "dm_sourcedate",
                        DisplayName = new Label("Source Date Preservation", 1033),
                        DisplayCollectionName = new Label("Source Dates", 1033),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false,
                        HasNotes = false,
                        HasActivities = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "dm_name",
                        LogicalName = "dm_name",
                        DisplayName = new Label("Name", 1033),
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.None
                        ),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);
                await Task.Delay(5000, ct); // Wait for propagation

                existingMeta = await target.GetEntityMetadataAsync(
                    "dm_sourcedate",
                    ct
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                "dm_sourcedate",
                existingMeta!,
                "dm_sourceentityid",
                "Source Entity ID",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_sourcedate",
                existingMeta!,
                "dm_sourceentitylogicalname",
                "Source Entity Logical Name",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_sourcedate",
                existingMeta!,
                "dm_sourcecreateddate",
                "Source Created Date",
                progress,
                ct,
                false // DateTime
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_sourcedate",
                existingMeta!,
                "dm_sourcemodifieddate",
                "Source Modified Date",
                progress,
                ct,
                false // DateTime
            );
        }

        private async Task EnsureFailureLogEntityAsync(
            IDataverseProvider target,
            IProgress<string>? progress,
            CancellationToken ct
        )
        {
            var existingMeta = await target.GetEntityMetadataAsync(
                "dm_migrationfailure",
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information("Creating 'dm_migrationfailure' entity...");
                progress?.Report("Creating 'dm_migrationfailure' entity...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "dm_migrationfailure",
                        LogicalName = "dm_migrationfailure",
                        DisplayName = new Label("Migration Failure", 1033),
                        DisplayCollectionName = new Label(
                            "Migration Failures", 
                            1033
                        ),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "dm_name",
                        LogicalName = "dm_name",
                        DisplayName = new Label("Name", 1033),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);
                await Task.Delay(5000, ct);

                existingMeta = await target.GetEntityMetadataAsync(
                    "dm_migrationfailure",
                    ct
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                "dm_migrationfailure",
                existingMeta!,
                "dm_sourceid",
                "Source Record ID",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_migrationfailure",
                existingMeta!,
                "dm_entitylogicalname",
                "Entity Logical Name",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_migrationfailure",
                existingMeta!,
                "dm_errormessage",
                "Error Message",
                progress,
                ct,
                true, // IsString
                true  // IsMemo/LongText
            );

            await CreateAttributeIfMissingAsync(
                target,
                "dm_migrationfailure",
                existingMeta!,
                "dm_timestamp",
                "Failure Timestamp",
                progress,
                ct,
                false // DateTime
            );
        }

        private async Task CreateAttributeIfMissingAsync(
            IDataverseProvider target,
            string entityLogicalName,
            EntityMetadata entityMeta,
            string schemaName,
            string displayName,
            IProgress<string>? progress,
            CancellationToken ct,
            bool isString = true,
            bool isMemo = false
        )
        {
            if (entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
            {
                return;
            }

            _logger.Information(
                "Creating attribute {Attr} on {Entity}...", 
                schemaName, 
                entityLogicalName
            );
            progress?.Report($"Creating attribute {schemaName}...");

            AttributeMetadata attr;

            if (isString)
            {
                if (isMemo)
                {
                    attr = new MemoAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        MaxLength = 5000
                    };
                }
                else
                {
                    attr = new StringAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        MaxLength = 200
                    };
                }
            }
            else
            {
                attr = new DateTimeAttributeMetadata
                {
                    SchemaName = schemaName,
                    LogicalName = schemaName.ToLower(),
                    DisplayName = new Label(displayName, 1033),
                    Format = DateTimeFormat.DateAndTime
                };
            }

            var req = new CreateAttributeRequest
            {
                EntityName = entityLogicalName,
                Attribute = attr
            };

            await target.ExecuteAsync(req, ct);
            await Task.Delay(2000, ct); // Gap for consistency
        }

        /// <inheritdoc />
        public async Task DropSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            // 1. dm_sourcedate
            await DropEntityIfPresentAsync(target, "dm_sourcedate", progress, ct);

            // 2. dm_migrationfailure
            await DropEntityIfPresentAsync(
                target, 
                "dm_migrationfailure", 
                progress, 
                ct
            );

            _logger.Information("Publishing changes...");
            progress?.Report("Publishing changes...");

            await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

            _logger.Information("Schema removal completed.");
            progress?.Report("Schema removal completed.");
        }

        private async Task DropEntityIfPresentAsync(
            IDataverseProvider target,
            string logicalName,
            IProgress<string>? progress,
            CancellationToken ct
        )
        {
            _logger.Information("Checking for '{Entity}' entity...", logicalName);
            progress?.Report($"Checking for '{logicalName}' entity...");

            var existingMeta = await target.GetEntityMetadataAsync(
                logicalName,
                ct
            );

            if (existingMeta != null)
            {
                _logger.Information("Deleting '{Entity}' entity...", logicalName);
                progress?.Report($"Deleting '{logicalName}' entity...");

                var request = new DeleteEntityRequest
                {
                    LogicalName = logicalName
                };

                await target.ExecuteAsync(request, ct);
            }
            else
            {
                _logger.Information("'{Entity}' entity not found.", logicalName);
                progress?.Report($"'{logicalName}' entity not found.");
            }
        }
    }
}
