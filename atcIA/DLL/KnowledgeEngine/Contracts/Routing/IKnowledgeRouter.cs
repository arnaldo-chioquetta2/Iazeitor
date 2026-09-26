namespace Iazeitor.KnowledgeEngine.Contracts.Routing
{
    public interface IKnowledgeRouter
    {
        RouterDecision Route(RouterContract contract);
    }
}