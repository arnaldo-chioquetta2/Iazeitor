using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Public;

namespace Iazeitor.KnowledgeEngine.Contracts.Reading
{
    public sealed class KnowledgePackReadResult
    {
        public IReadOnlyList<PackContract> Packs { get; set; }

        public bool HasUnresolvedPacks { get; set; }

        /// <summary>
        /// Ao menos um Pack explicitamente solicitado esta em formato legado reconhecido
        /// (Content ausente): nao foi reidratado e nao pode ser tratado como corrupcao.
        /// </summary>
        public bool HasLegacyUnsupportedPacks { get; set; }
    }
}
