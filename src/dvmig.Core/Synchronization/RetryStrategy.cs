using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Polly;
using Polly.Retry;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IRetryStrategy"/> that provides retry
   /// logic with exponential backoff for transient errors.
   /// </summary>
   public class RetryStrategy : IRetryStrategy
   {
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="RetryStrategy"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public RetryStrategy(ILogger logger)
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
            msg.Contains(SystemConstants.ErrorKeywords.CombinedExecutionTime) ||
            msg.Contains(SystemConstants.ErrorKeywords.GenericSqlError) ||
            msg.Contains(SystemConstants.ErrorKeywords.Timeout);

         if (isTransient)
            return true;

         return IsTransientError(ex.InnerException!);
      }

      /// <inheritdoc />
      public TimeSpan GetRetryDelay(int retryCount, Exception ex)
      {
         var msg = ex.Message.ToLower();

         bool isThrottled =
            msg.Contains(SystemConstants.ErrorCodes.ServiceProtectionLimit) ||
            msg.Contains(SystemConstants.ErrorCodes.ConnectionTimeout) ||
            msg.Contains(SystemConstants.ErrorKeywords.CombinedExecutionTime);

         if (isThrottled)
         {
            _logger.Information(
               "Service Protection Limit reached. " +
               "Applying throttled backoff."
            );

            return TimeSpan.FromSeconds(
               Math.Min(Math.Pow(2, retryCount), 30)
            );
         }

         return TimeSpan.FromSeconds(Math.Pow(2, retryCount));
      }

      /// <summary>
      /// Creates a retry policy for handling transient errors.
      /// </summary>
      /// <param name="maxRetries">
      /// The maximum number of retry attempts.
      /// </param>
      /// <returns>A configured retry policy.</returns>
      public AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5)
      {
         return Policy
            .Handle<Exception>(IsTransientError)
            .WaitAndRetryAsync(
               maxRetries,
               retryAttempt =>
                  TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
               (ex, time, count, ctx) =>
               {
                  var msg = $"Throttling or transient error. Retry {count} " +
                     $"in {time.TotalMilliseconds}ms";

                  _logger.Warning(ex, $"{SystemConstants.UiMarkup.Yellow}" +
                     $"{SystemConstants.UiMarkup.Wait}[/] {msg}");
               }
            );
      }
   }
}
