using Iazeitor.KnowledgeEngine.Contracts.Public;

namespace Iazeitor.KnowledgeEngine.Contracts.Quality
{
    public interface IKnowledgeQualityService
    {
        QualityAssessment Assess(SessionContract session);
    }
}
