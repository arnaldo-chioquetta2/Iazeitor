using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    public sealed class KnowledgePackRecoveryResult
    {
        public KnowledgePackRecoveryStatus Status { get; set; }

        public KnowledgeIdentifier TransactionId { get; set; }

        public string FailureReason { get; set; }
    }
}
