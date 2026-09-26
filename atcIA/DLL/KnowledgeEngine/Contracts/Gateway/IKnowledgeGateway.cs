using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Contracts.Public;

namespace Iazeitor.KnowledgeEngine.Contracts.Gateway
{
    public interface IKnowledgeGateway
    {
        KnowledgeGatewayResult Execute(
            KnowledgeDemand demand,
            SessionContract session,
            QueryContract query);
    }
}
