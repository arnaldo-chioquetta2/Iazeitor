using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Acquisition
{
    public sealed class AcquisitionContract
    {
        public InvestigationPlan Plan { get; set; }

        public KnowledgeIdentifier SessionId { get; set; }
    }
}
