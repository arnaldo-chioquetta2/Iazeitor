using System.Collections.Generic;
using System.Linq;

namespace GptBolDll.Deploy
{
    public sealed class RemoteDeployResult
    {
        public List<RemoteDeployFileResult> Files { get; } = new List<RemoteDeployFileResult>();

        public int SuccessCount => Files.Count(f => f != null && f.Success);
        public int FailureCount => Files.Count(f => f != null && !f.Success);
        public bool Succeeded => Files.Count > 0 && FailureCount == 0;
    }
}
