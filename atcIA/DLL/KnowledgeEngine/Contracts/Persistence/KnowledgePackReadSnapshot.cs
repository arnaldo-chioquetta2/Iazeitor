using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Snapshot de leitura produzido sob uma unica aquisicao do PackLock.
    /// Manifest e versao ativa fisica sao lidos na mesma regiao critica, portanto
    /// nao podem pertencer a geracoes diferentes de publicacao.
    /// </summary>
    public sealed class KnowledgePackReadSnapshot
    {
        public KnowledgePackReadStatus Status { get; set; }

        public KnowledgePackPersistenceContract Persistence { get; set; }

        public KnowledgePackVersionContract ActiveVersion { get; set; }

        public KnowledgePackRecoveryResult Recovery { get; set; }

        public string FailureReason { get; set; }
    }
}
