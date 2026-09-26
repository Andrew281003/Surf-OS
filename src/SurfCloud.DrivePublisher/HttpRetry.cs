using System.Net;

internal static class HttpRetry
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null, double? jitter = null)
    {
        delay ??= (duration, token) => Task.Delay(duration, token);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpRequestMessage request = createRequest();
            try
            {
                HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == 2) return response;
                TimeSpan wait = GetDelay(attempt, response.Headers.RetryAfter?.Delta,
                    response.Headers.RetryAfter?.Date, jitter ?? Random.Shared.NextDouble());
                response.Dispose();
                await delay(wait, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await delay(GetDelay(attempt, null, null, jitter ?? Random.Shared.NextDouble()), cancellationToken);
            }
        }
        throw new InvalidOperationException("Retry loop exhausted unexpectedly.");
    }

    internal static TimeSpan GetDelay(int attempt, TimeSpan? retryAfter, DateTimeOffset? retryAt, double jitter)
    {
        TimeSpan desired = retryAfter ?? (retryAt - DateTimeOffset.UtcNow) ??
            TimeSpan.FromMilliseconds(200 * (1 << attempt) * (0.5 + Math.Clamp(jitter, 0, 1)));
        return TimeSpan.FromMilliseconds(Math.Clamp(desired.TotalMilliseconds, 0, 5000));
    }

    private static bool IsTransient(HttpStatusCode code) => code is
        HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
