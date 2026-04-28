using dvmig.Core.Synchronization;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that retrieves and manages 
   /// migration failure records.
   /// </summary>
   public interface IReconciliationService
   {
      /// <summary>
      /// Checks if the failure logging entity is installed on the target.
      /// </summary>
      Task<bool> IsInitializedAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves all recorded migration failures from the target.
      /// </summary>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded migration failures from the target.
      /// </summary>
      Task ClearFailuresAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );
   }
}
