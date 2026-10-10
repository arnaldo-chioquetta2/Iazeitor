using System;
using GptBolDll.Core.Contracts;

namespace GptBolDll.Core.Services
{
    public sealed class ProjectContextProvider : IProjectContextProvider
    {
        public ProjectContextSnapshot Build(
            string projectRoot,
            IContextBuilder contextBuilder,
            Action<string> reportStatus,
            ExecutionLogger log,
            string operationId = null)
        {
            reportStatus("Montando contexto do projeto");

            string context = contextBuilder.Build(projectRoot) ?? string.Empty;

            log.Info("Contexto do projeto montado. Caracteres: " + context.Length);

            return new ProjectContextSnapshot
            {
                Context = context,
                ProjectRoot = projectRoot,
                OperationId = operationId,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
