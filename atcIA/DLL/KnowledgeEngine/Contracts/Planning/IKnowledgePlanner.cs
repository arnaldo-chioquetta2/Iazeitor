namespace Iazeitor.KnowledgeEngine.Contracts.Planning
{
    public interface IKnowledgePlanner
    {
        InvestigationResult CreatePlan(PlannerContract contract);
    }
}
