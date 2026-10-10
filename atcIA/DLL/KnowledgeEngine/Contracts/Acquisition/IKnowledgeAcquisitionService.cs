using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Public;

namespace Iazeitor.KnowledgeEngine.Contracts.Acquisition
{
    public interface IKnowledgeAcquisitionService
    {
        InvestigationResult Execute(AcquisitionContract contract, SessionContract session);
    }
}
