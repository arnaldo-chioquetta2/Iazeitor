using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Boundary de persistencia da associacao ProjectId -> PackIds.
    /// Load nunca inventa Pack nem integridade: apenas devolve a associacao
    /// persistida (ou null quando ausente/ilegivel/invalida). AddAssociation
    /// grava de forma atomica e idempotente.
    /// </summary>
    public interface IProjectKnowledgeBindingStore
    {
        ProjectKnowledgeBindingContract Load(string projectId);

        bool AddAssociation(string projectId, KnowledgeIdentifier packId);
    }
}
