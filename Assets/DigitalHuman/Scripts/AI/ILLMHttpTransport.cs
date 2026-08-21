using System.Threading;
using System.Threading.Tasks;

namespace DigitalHuman
{
    public interface ILLMHttpTransport
    {
        Task<LLMHttpResponse> PostJsonAsync(
            string url,
            string jsonBody,
            string bearerToken,
            int timeoutSeconds,
            CancellationToken cancellationToken = default);
    }

    public sealed class LLMHttpResponse
    {
        public long StatusCode { get; }
        public string Body { get; }
        public string TransportError { get; }
        public bool TimedOut { get; }
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        public LLMHttpResponse(
            long statusCode,
            string body,
            string transportError = null,
            bool timedOut = false)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            TransportError = transportError;
            TimedOut = timedOut;
        }
    }
}
