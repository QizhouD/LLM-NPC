using System;

namespace DigitalHuman
{
    public enum LLMErrorKind
    {
        Configuration,
        Timeout,
        Network,
        Http,
        Api,
        InvalidJson,
        EmptyResponse
    }

    public sealed class LLMServiceException : Exception
    {
        public LLMErrorKind ErrorKind { get; }
        public long StatusCode { get; }

        public LLMServiceException(
            LLMErrorKind errorKind,
            string message,
            long statusCode = 0,
            Exception innerException = null)
            : base(message, innerException)
        {
            ErrorKind = errorKind;
            StatusCode = statusCode;
        }
    }
}
