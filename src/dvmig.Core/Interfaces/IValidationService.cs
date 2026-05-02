namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that validates environment readiness
   /// and component versions.
   /// </summary>
   public interface IValidationService
   {
      /// <summary>
      /// Validates that the target environment is ready for migration.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is valid.</returns>
      Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Validates that the source environment is accessible.
      /// </summary>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is valid.</returns>
      Task<bool> ValidateSourceEnvironmentAsync(
         IDataverseProvider source,
         CancellationToken ct = default
      );
   }
}
