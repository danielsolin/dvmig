using Polly;
using Polly.Retry;
using Serilog;

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
            var msg = ex.Message.ToLower();

            if (msg.Contains("8004410d") || msg.Contains("too many requests"))
            {
                return true;
            }

            return msg.Contains("generic sql error") ||
                   msg.Contains("timeout");
        }

        /// <inheritdoc />
        public TimeSpan GetRetryDelay(int retryCount, Exception ex)
        {
            if (ex.Message.Contains("8004410d"))
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
        /// <param name="maxRetries">The maximum number of retry attempts.</param>
        /// <returns>A configured retry policy.</returns>
        public AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5)
        {
            return Policy
                .Handle<Exception>(IsTransientError)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, time, count, ctx) =>
                    {
                        _logger.Warning(
                            ex,
                            "Throttling or transient error. " +
                            "Retry {Count} in {Time}ms",
                            count,
                            time.TotalMilliseconds
                        );
                    }
                );
        }
    }
}