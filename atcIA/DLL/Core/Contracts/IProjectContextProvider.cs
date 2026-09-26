using System;

namespace GptBolDll.Core.Contracts
{
    public interface IProjectContextProvider
    {
        ProjectContextSnapshot Build(
            string projectRoot,
            IContextBuilder contextBuilder,
            Action<string> reportStatus,
            ExecutionLogger log,
            string operationId = null);
    }
}
