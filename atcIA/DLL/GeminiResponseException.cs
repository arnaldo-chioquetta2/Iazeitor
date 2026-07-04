using System;

namespace GptBolDll
{
    public sealed class GeminiResponseException : InvalidOperationException
    {
        public GeminiResponseException(string message, string finishReason, string rawResponse)
            : base(message)
        {
            FinishReason = finishReason;
            RawResponse = rawResponse;
        }

        public string FinishReason { get; }

        public string RawResponse { get; }
    }
}
