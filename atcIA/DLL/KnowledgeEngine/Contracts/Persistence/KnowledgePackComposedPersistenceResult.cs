using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Resultado pequeno da operação composta coordenada do Repository.
    /// Timeout de PackLock continua sendo KnowledgePackStorageLockTimeoutException
    /// e erros de persistência continuam sendo exceções — nunca convertidos aqui.
    /// </summary>
    public sealed class KnowledgePackComposedPersistenceResult
    {
        public KnowledgePackComposedPersistenceStatus Status { get; set; }

        public KnowledgePackRecoveryStatus RecoveryStatus { get; set; }

        public KnowledgeIdentifier TransactionId { get; set; }

        public string FailureReason { get; set; }
    }
}
