using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Polly;
using Polly.Retry;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IRetryService"/> that provides retry
   /// logic with exponential backoff for transient errors.
   /// </summary>
   public class RetryService : IRetryService
   {
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="RetryService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public RetryService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public bool IsTransientError(Exception ex)
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

         return IsTransientError(ex.InnerException!);
      }

      /// <inheritdoc />
      public AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5)
      {
         return Policy
            .Handle<Exception>(IsTransientError)
            .WaitAndRetryAsync(
               maxRetries,
               retryAttempt => GetRetryDelay(retryAttempt, null!),
               (ex, time, count, ctx) =>
               {
                  var msg = $"Transient error. Retry {count} " +
                     $"in {time.TotalSeconds:F0}s";

                  _logger.Warning(
                     ex,
                     "Transient error detected. {0}",
                     msg
                  );
               }
            );
      }

      /// <inheritdoc />
      public TimeSpan GetRetryDelay(int retryCount, Exception? ex)
      {
         bool isThrottled = false;

         if (ex != null)
         {
            var msg = ex.Message.ToLower();

            isThrottled =
               msg.Contains(SystemConstants.ErrorCodes.ServiceProtectionLimit) ||
               msg.Contains("8004410d") ||
               msg.Contains(SystemConstants.ErrorCodes.ConnectionTimeout) ||
               msg.Contains(SystemConstants.ErrorKeywords.CombinedExecutionTime);
         }
         else
         {
            isThrottled = true;
         }

         if (isThrottled)
            return TimeSpan.FromSeconds(
               Math.Min(Math.Pow(2, retryCount + 3), 30)
            );

         return TimeSpan.FromSeconds(Math.Pow(2, retryCount));
      }
   }
}
