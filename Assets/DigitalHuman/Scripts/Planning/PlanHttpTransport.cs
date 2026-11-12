using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace DigitalHuman.Planning
{
    /// <summary>Raised when the planner service cannot be reached or rejects a request.</summary>
    public sealed class PlanServiceException : Exception
    {
        public long StatusCode { get; }

        public PlanServiceException(string message, long statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>Seam between the HTTP plan source and the network, so tests need no server.</summary>
    public interface IPlanHttpTransport
    {
        /// <summary>Posts JSON and returns the response body, or throws <see cref="PlanServiceException"/>.</summary>
        Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            int timeoutSeconds,
            CancellationToken cancellationToken = default);
    }

    public sealed class UnityWebRequestPlanTransport : IPlanHttpTransport
    {
        public async Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Math.Max(1, timeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");

                TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    request.Abort();
                    completion.TrySetCanceled();
                });

                try
                {
                    await completion.Task;
                }
                finally
                {
                    registration.Dispose();
                }

                cancellationToken.ThrowIfCancellationRequested();

                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string detail = string.IsNullOrEmpty(body) ? request.error : $"{request.error}: {body}";
                    throw new PlanServiceException(
                        $"Planner service request failed ({detail}).", request.responseCode);
                }

                return body;
            }
        }
    }
}
