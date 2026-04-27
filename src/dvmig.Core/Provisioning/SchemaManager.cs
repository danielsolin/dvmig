using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
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
        private const int LanguageCode = 1033;
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
            var entityName = Constants.SourceDate.EntityLogicalName;
            var existingMeta = await target.GetEntityMetadataAsync(
                entityName,
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information("Creating '{Entity}' entity...", entityName);
                progress?.Report($"Creating '{entityName}' entity...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = entityName,
                        LogicalName = entityName,
                        DisplayName = new Label(
                            "DVMig Source Date ", LanguageCode
                        ),
                        DisplayCollectionName = new Label(
                            "DVMig Source Dates", LanguageCode
                        ),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false,
                        HasNotes = false,
                        HasActivities = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = Constants.SourceDate.Name,
                        LogicalName = Constants.SourceDate.Name,
                        DisplayName = new Label("Name", LanguageCode),
                        RequiredLevel =
                            new AttributeRequiredLevelManagedProperty(
                                AttributeRequiredLevel.None
                            ),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);
                await Task.Delay(
                    Constants.AppConstants.MetadataPropagationDelayMs,
                    ct
                ); // Wait for propagation

                existingMeta = await target.GetEntityMetadataAsync(
                    entityName,
                    ct
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.SourceDate.EntityId,
                "Source Entity ID",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.SourceDate.EntityLogicalNameAttr,
                "Source Entity Logical Name",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.SourceDate.CreatedDate,
                "Source Created Date",
                progress,
                ct,
                false // DateTime
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.SourceDate.ModifiedDate,
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
            var entityName = Constants.MigrationFailure.EntityLogicalName;
            var existingMeta = await target.GetEntityMetadataAsync(
                entityName,
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information("Creating '{Entity}' entity...", entityName);
                progress?.Report($"Creating '{entityName}' entity...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = entityName,
                        LogicalName = entityName,
                        DisplayName = new Label("DVMig Failure", LanguageCode),
                        DisplayCollectionName = new Label(
                            "DVMig Failures",
                            LanguageCode
                        ),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = Constants.MigrationFailure.Name,
                        LogicalName = Constants.MigrationFailure.Name,
                        DisplayName = new Label("Name", LanguageCode),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);
                await Task.Delay(
                    Constants.AppConstants.MetadataPropagationDelayMs,
                    ct
                );

                existingMeta = await target.GetEntityMetadataAsync(
                    entityName,
                    ct
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.MigrationFailure.SourceId,
                "Source Record ID",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.MigrationFailure.EntityLogicalNameAttr,
                "Entity Logical Name",
                progress,
                ct
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.MigrationFailure.ErrorMessage,
                "Error Message",
                progress,
                ct,
                true, // IsString
                true  // IsMemo/LongText
            );

            await CreateAttributeIfMissingAsync(
                target,
                entityName,
                existingMeta!,
                Constants.MigrationFailure.Timestamp,
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
            if (entityMeta.Attributes != null &&
                entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
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
                        DisplayName = new Label(displayName, LanguageCode),
                        MaxLength = Constants.AppConstants
                                        .MaxMemoFieldLength
                    };
                }
                else
                {
                    attr = new StringAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, LanguageCode),
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
                    DisplayName = new Label(displayName, LanguageCode),
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
            await DropEntityIfPresentAsync(
                target,
                Constants.SourceDate.EntityLogicalName,
                progress,
                ct
            );

            // 2. dm_migrationfailure
            await DropEntityIfPresentAsync(
                target,
                Constants.MigrationFailure.EntityLogicalName,
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
            _logger.Information(
                "Checking for '{Entity}' entity...",
                logicalName
            );

            progress?.Report($"Checking for '{logicalName}' entity...");

            var existingMeta = await target.GetEntityMetadataAsync(
                logicalName,
                ct
            );

            if (existingMeta != null)
            {
                _logger.Information(
                    "Deleting '{Entity}' entity...",
                    logicalName
                );

                progress?.Report($"Deleting '{logicalName}' entity...");

                var request = new DeleteEntityRequest
                {
                    LogicalName = logicalName
                };

                await target.ExecuteAsync(request, ct);
            }
            else
            {
                _logger.Information(
                    "'{Entity}' entity not found.",
                    logicalName
                );

                progress?.Report($"'{logicalName}' entity not found.");
            }
        }
    }
}
