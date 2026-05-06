using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Provides utility methods for working with Dataverse entities.
   /// </summary>
   public static class EntityHelper
   {
      /// <summary>
      /// Generates a standardized record key for tracking and caching.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="id">The unique identifier of the record.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(string logicalName, System.Guid id)
      {
         return $"{logicalName.ToLowerInvariant()}:{id}";
      }

      /// <summary>
      /// Generates a standardized record key for an entity.
      /// </summary>
      /// <param name="entity">The entity record.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(Entity entity)
      {
         return GetRecordKey(entity.LogicalName, entity.Id);
      }

      /// <summary>
      /// Generates a standardized record key for an entity reference.
      /// </summary>
      /// <param name="er">The entity reference.</param>
      /// <returns>A formatted string key.</returns>
      public static string GetRecordKey(EntityReference er)
      {
         return GetRecordKey(er.LogicalName, er.Id);
      }

      /// <summary>
      /// Determines whether an exception represents a transient Dataverse 
      /// error.
      /// </summary>
      /// <param name="ex">The exception to check.</param>
      /// <returns>True if the error is transient; otherwise, false.</returns>
      public static bool IsTransientError(System.Exception ex)
      {
         if (ex == null)
            return false;

         var msg = ex.Message.ToLower();

         bool isTransient =
            msg.Contains(SystemConstants.ErrorCodes.ServiceProtectionLimit) ||
            msg.Contains(SystemConstants.ErrorCodes.ConnectionTimeout) ||
            msg.Contains(SystemConstants.ErrorKeywords.TooManyRequests) ||
            msg.Contains("exceeded the limit") ||
            msg.Contains(SystemConstants.ErrorKeywords.CombinedExecutionTime) ||
            msg.Contains(SystemConstants.ErrorKeywords.GenericSqlError) ||
            msg.Contains(SystemConstants.ErrorKeywords.Timeout);

         if (isTransient)
            return true;

         return ex.InnerException != null &&
            IsTransientError(ex.InnerException);
      }
   }
}
