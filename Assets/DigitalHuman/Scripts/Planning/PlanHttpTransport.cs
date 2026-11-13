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

        /// <summary>True when the request was abandoned because it exceeded its timeout.</summary>
        public bool IsTimeout { get; }

        public PlanServiceException(string message, long statusCode = 0, bool isTimeout = false) : base(message)
        {
            StatusCode = statusCode;
            IsTimeout = isTimeout;
        }
    }

    /// <summary>Seam between the HTTP plan source and the network, so tests need no server.</summary>
    public interface IPlanHttpTransport
    {
        /// <summary>
        /// Posts JSON and returns the response body, or throws <see cref="PlanServiceException"/>,
        /// with <see cref="PlanServiceException.IsTimeout"/> set when no answer arrived within
        /// <paramref name="timeoutMilliseconds"/>.
        /// </summary>
        Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            int timeoutMilliseconds,
            CancellationToken cancellationToken = default);
    }

    public sealed class UnityWebRequestPlanTransport : IPlanHttpTransport
    {
        public async Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            int timeoutMilliseconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int timeout = Math.Max(1, timeoutMilliseconds);

            byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                // UnityWebRequest.timeout only takes whole seconds, so it is only a backstop,
                // set past the real deadline; the millisecond timeout is enforced below.
                request.timeout = (int)Math.Ceiling(timeout / 1000.0) + 1;
                request.SetRequestHeader("Content-Type", "application/json");

                TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    request.Abort();
                    completion.TrySetCanceled();
                });

                using (CancellationTokenSource timer = new CancellationTokenSource())
                {
                    try
                    {
                        Task deadline = Task.Delay(timeout, timer.Token);
                        // The continuation resumes on Unity's main thread, where Abort is legal.
                        Task finished = await Task.WhenAny(completion.Task, deadline);
                        if (finished == deadline && !completion.Task.IsCompleted)
                        {
                            request.Abort();
                            throw new PlanServiceException(
                                $"Planner service request timed out after {timeout} ms.", 0, true);
                        }

                        await completion.Task;
                    }
                    finally
                    {
                        timer.Cancel();
                        registration.Dispose();
                    }
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
