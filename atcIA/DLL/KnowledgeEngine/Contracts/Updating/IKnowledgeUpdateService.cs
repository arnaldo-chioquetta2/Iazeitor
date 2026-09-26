using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Quality;

namespace Iazeitor.KnowledgeEngine.Contracts.Updating
{
    public interface IKnowledgeUpdateService
    {
        KnowledgeUpdateResult Update(
            QualityAssessment assessment,
            SessionContract session,
            IReadOnlyList<PackContract> packs);
    }
}
