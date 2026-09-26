using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Relatorio tipado da validacao DEEP. Preserva diagnostico sem expor conteudo
    /// sensivel: as violacoes sao descricoes curtas de invariantes.
    /// </summary>
    public sealed class KnowledgePackIntegrityReport
    {
        public KnowledgePackIntegrityStatus Status { get; set; }

        public KnowledgeIdentifier PackId { get; set; }

        public IReadOnlyList<string> Violations { get; set; }

        public string FailureReason { get; set; }
    }
}
