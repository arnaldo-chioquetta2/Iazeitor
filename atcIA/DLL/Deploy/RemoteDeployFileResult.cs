using System;

namespace GptBolDll.Deploy
{
    public sealed class RemoteDeployFileResult
    {
        public string RelativePath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}
