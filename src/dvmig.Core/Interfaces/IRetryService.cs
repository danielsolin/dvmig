using Polly.Retry;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that provides retry
   /// logic and policies for handling transient errors.
   /// </summary>
   public interface IRetryService
   {
      /// <summary>
      /// Determines whether an exception represents a transient error 
      /// suitable for retry.
      /// </summary>
      /// <param name="ex">The exception to check.</param>
      /// <returns>True if the error is transient; otherwise, false.</returns>
      bool IsTransientError(Exception ex);

      /// <summary>
      /// Calculates the delay before the next retry attempt.
      /// </summary>
      /// <param name="retryCount">The current retry count.</param>
      /// <param name="ex">The exception that triggered the retry.</param>
      /// <returns>The calculated delay.</returns>
      TimeSpan GetRetryDelay(int retryCount, Exception? ex);

      /// <summary>
      /// Creates an asynchronous retry policy configured with the 
      /// service's rules.
      /// </summary>
      /// <param name="maxRetries">The maximum number of retries.</param>
      /// <returns>A configured AsyncRetryPolicy.</returns>
      AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5);
   }
}
