using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Updating
{
    public sealed class KnowledgeUpdateResult
    {
        public string UpdateId { get; set; }

        public KnowledgeStatus Status { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public IReadOnlyList<string> UpdatedKnowledgePackIds { get; set; }

        public IReadOnlyList<string> CreatedKnowledgePackIds { get; set; }

        public IReadOnlyList<string> PreviousVersionIds { get; set; }

        public IReadOnlyList<string> CurrentVersionIds { get; set; }

        public string Reason { get; set; }

        public KnowledgeSource Source { get; set; }

        public KnowledgeError Error { get; set; }

        /// <summary>
        /// Resultado da gravacao da associacao persistente ao projeto.
        /// true = associacao gravada; false = Pack persistido, mas associacao falhou;
        /// null = nao aplicavel (ex.: UPDATE nao regrava o binding).
        /// </summary>
        public bool? AssociationPersisted { get; set; }

        /// <summary>Motivo deterministico da falha de associacao, sem payload/recurso sensivel.</summary>
        public string AssociationFailureReason { get; set; }
    }
}
