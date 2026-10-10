namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Classificacao estrutural do manifest persistido, feita sob o PackLock.
    /// Distingue o formato moderno do formato legado reconhecido (sem Content) e da
    /// corrupcao, sem inferir geracoes intermediarias que nao existem no codigo real.
    /// </summary>
    public enum KnowledgePackPersistenceFormat
    {
        /// <summary>Manifest com Identity valida e Content estruturado suficiente para reidratacao.</summary>
        Modern,

        /// <summary>
        /// Manifest com Identity valida e Content ausente/null: formato legado conhecido.
        /// Reconhecido, porem insuficiente para reidratar um PackContract sem inventar dados.
        /// </summary>
        LegacyRecognized,

        /// <summary>
        /// JSON invalido, Identity invalida/incoerente, IDs divergentes ou estrutura que
        /// afirma ser moderna mas esta incompleta/incoerente.
        /// </summary>
        Corrupt
    }
}
