using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace DigitalHuman
{
    public sealed class UnityWebRequestLLMTransport : ILLMHttpTransport
    {
        public async Task<LLMHttpResponse> PostJsonAsync(
            string url,
            string jsonBody,
            string bearerToken,
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
                request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

                await AwaitRequestAsync(request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                string error = null;
                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    error = request.error;
                }

                bool timedOut = !string.IsNullOrEmpty(request.error) &&
                    request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                return new LLMHttpResponse(
                    request.responseCode,
                    request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                    error,
                    timedOut);
            }
        }

        private static async Task AwaitRequestAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
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
        }
    }
}
