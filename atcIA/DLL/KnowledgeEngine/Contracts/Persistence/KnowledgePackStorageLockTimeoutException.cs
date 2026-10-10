using System;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Falha de aquisição da exclusão física de um Knowledge Pack por timeout.
    /// É distinta de KnowledgePackRecoveryStatus.Unrecoverable: não indica
    /// journal corrompido nem staging inconsistente, apenas contenção de lock.
    /// </summary>
    public sealed class KnowledgePackStorageLockTimeoutException : Exception
    {
        public KnowledgePackStorageLockTimeoutException(KnowledgeIdentifier packId, TimeSpan timeout)
            : base(BuildMessage(packId, timeout))
        {
            PackId = packId;
            AcquisitionTimeout = timeout;
        }

        public KnowledgeIdentifier PackId { get; private set; }

        public TimeSpan AcquisitionTimeout { get; private set; }

        private static string BuildMessage(KnowledgeIdentifier packId, TimeSpan timeout)
        {
            string identifier = packId == null || String.IsNullOrWhiteSpace(packId.Value)
                ? "(desconhecido)"
                : packId.Value;
            return "Não foi possível adquirir a exclusão física do Knowledge Pack '"
                + identifier
                + "' em "
                + timeout
                + ".";
        }
    }
}
