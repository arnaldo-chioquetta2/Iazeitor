namespace Iazeitor.KnowledgeEngine.Contracts.Query
{
    public interface IKnowledgeQueryService
    {
        QueryResult Execute(QueryContract query);

        QueryResult Execute(QueryContract query, Iazeitor.KnowledgeEngine.Contracts.Public.SessionContract session);
    }
}
