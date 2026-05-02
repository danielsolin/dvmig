using dvmig.Core.Interfaces;
using dvmig.Core.Shared;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IValidationService"/> that validates 
   /// environment readiness.
   /// </summary>
   public class ValidationService : IValidationService
   {
      /// <inheritdoc />
      public async Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         try
         {
            var meta = await target.GetEntityMetadataAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               ct
            );

            return meta != null;
         }
         catch
         {
            return false;
         }
      }

      /// <inheritdoc />
      public async Task<bool> ValidateSourceEnvironmentAsync(
         IDataverseProvider source,
         CancellationToken ct = default
      )
      {
         try
         {
            await source.GetRecordCountAsync(
               SystemConstants.DataverseEntities.SystemUser,
               ct
            );

            return true;
         }
         catch
         {
            return false;
         }
      }
   }
}
