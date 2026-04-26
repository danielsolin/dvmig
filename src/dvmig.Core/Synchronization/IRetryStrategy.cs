using Polly.Retry;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Defines the contract for retry strategies during synchronization.
    /// </summary>
    public interface IRetryStrategy
    {
        /// <summary>
        /// Determines if an exception represents a transient error that
        /// should trigger a retry attempt (e.g., throttling, timeout).
        /// </summary>
        /// <param name="ex">The exception to evaluate.</param>
        /// <returns>True if the error is transient; otherwise, false.</returns>
        bool IsTransientError(Exception ex);

        /// <summary>
        /// Calculates the delay before the next retry attempt, applying
        /// exponential backoff. Caps the delay for Dataverse Service
        /// Protection Limits (8004410d).
        /// </summary>
        /// <param name="retryCount">The current retry attempt number.</param>
        /// <param name="ex">The exception that triggered the retry.</param>
        /// <returns>The duration to wait before retrying.</returns>
        TimeSpan GetRetryDelay(int retryCount, Exception ex);

        /// <summary>
        /// Creates a retry policy for handling transient errors.
        /// </summary>
        /// <param name="maxRetries">The maximum number of retry attempts.</param>
        /// <returns>A configured retry policy.</returns>
        AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5);
    }
}
