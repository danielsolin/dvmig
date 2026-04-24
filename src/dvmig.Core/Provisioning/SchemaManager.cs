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
            var existingMeta = await target.GetEntityMetadataAsync(
                "dm_sourcedate",
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information(
                    "Creating 'dm_sourcedate' entity schema..."
                );

                progress?.Report("Creating 'dm_sourcedate' entity schema...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "dm_sourcedate",
                        LogicalName = "dm_sourcedate",
                        DisplayName = new Label(
                            "Source Date Preservation",
                            1033
                        ),
                        DisplayCollectionName = new Label(
                            "Source Dates",
                            1033
                        ),
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
                        RequiredLevel =
                            new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.None
                        ),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);

                _logger.Information(
                    "Entity created. Waiting for metadata propagation..."
                );

                progress?.Report("Waiting for metadata propagation...");

                // Mandatory wait for Dataverse Online metadata propagation
                await Task.Delay(5000, ct);

                existingMeta = await target.GetEntityMetadataAsync(
                    "dm_sourcedate",
                    ct
                );
            }
            else
            {
                _logger.Information(
                    "'dm_sourcedate' entity already exists. " +
                    "Checking attributes."
                );

                progress?.Report(
                    "'dm_sourcedate' already exists. Checking attributes."
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourceentityid",
                "Source Entity ID",
                true,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourceentitylogicalname",
                "Source Entity Logical Name",
                true,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourcecreateddate",
                "Source Created Date",
                false,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourcemodifieddate",
                "Source Modified Date",
                false,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            _logger.Information("Publishing changes...");
            progress?.Report("Publishing changes...");

            await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

            _logger.Information("Schema creation completed.");
            progress?.Report("Schema creation completed.");
        }

        /// <summary>
        /// Creates an attribute on the 'dm_sourcedate' entity if it does not 
        /// already exist in the provided entity metadata.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="entityMeta">The existing entity metadata.</param>
        /// <param name="schemaName">The schema name of the attribute.</param>
        /// <param name="displayName">The display name of the attribute.</param>
        /// <param name="isString">True to create a string attribute; false
        /// for datetime.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        private async Task CreateAttributeIfMissingAsync(
            IDataverseProvider target,
            EntityMetadata entityMeta,
            string schemaName,
            string displayName,
            bool isString,
            IProgress<string>? progress,
            CancellationToken ct
        )
        {
            if (entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
            {
                return;
            }

            _logger.Information("Creating attribute {Attr}...", schemaName);
            progress?.Report($"Creating attribute {schemaName}...");

            var req = new CreateAttributeRequest
            {
                EntityName = "dm_sourcedate",
                Attribute = isString
                    ? (AttributeMetadata)new StringAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        MaxLength = 100
                    }
                    : new DateTimeAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        Format = DateTimeFormat.DateAndTime
                    }
            };

            await target.ExecuteAsync(req, ct);
        }
    }
}
