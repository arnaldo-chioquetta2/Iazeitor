namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Resultado de uma operação composta coordenada (recovery + revalidação + write
    /// sob uma única aquisição do PackLock).
    /// </summary>
    public enum KnowledgePackComposedPersistenceStatus
    {
        /// <summary>A operação foi publicada e o journal chegou a Committed.</summary>
        Persisted,

        /// <summary>
        /// A operação não foi aplicada porque o estado persistido já não corresponde:
        /// no CREATE o Pack já existe (antes ou após recovery de criação anterior);
        /// no UPDATE o current/active não corresponde a PreviousVersionIdToArchive.
        /// </summary>
        SupersededByExistingState,

        /// <summary>
        /// Há transação pendente não recuperável; nenhuma nova escrita foi iniciada.
        /// </summary>
        BlockedByUnrecoverableTransaction
    }
}
