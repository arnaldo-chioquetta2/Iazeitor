using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Persistence
{
    internal sealed class KnowledgePackRepository : IKnowledgePackRepository
    {
        private readonly KnowledgePackFileStore fileStore;
        private readonly KnowledgePackSerializer serializer;

        public KnowledgePackRepository(
            KnowledgePackFileStore fileStore,
            KnowledgePackSerializer serializer)
        {
            if (fileStore == null)
            {
                throw new ArgumentNullException(nameof(fileStore));
            }

            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            this.fileStore = fileStore;
            this.serializer = serializer;
        }

        public KnowledgePackPersistenceContract Get(KnowledgeIdentifier packId)
        {
            string path = fileStore.GetManifestPath(packId);
            return fileStore.FileExists(path)
                ? serializer.DeserializePersistenceContract(fileStore.ReadText(path))
                : null;
        }

        public IReadOnlyList<KnowledgePackIdentityContract> ListIdentities()
        {
            var result = new List<KnowledgePackIdentityContract>();
            foreach (KnowledgeIdentifier packId in fileStore.ListPackIds())
            {
                try
                {
                    KnowledgePackPersistenceContract pack = Get(packId);
                    if (pack != null && pack.Identity != null)
                    {
                        result.Add(pack.Identity);
                    }
                }
                catch (Exception)
                {
                    // Um Pack inválido não impede a descoberta dos demais.
                }
            }

            return result.AsReadOnly();
        }

        public KnowledgePackVersionContract GetActiveVersion(KnowledgeIdentifier packId)
        {
            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            return history == null ? null : history.ActiveVersion;
        }

        public IReadOnlyList<KnowledgePackVersionContract> ListVersions(KnowledgeIdentifier packId)
        {
            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            if (history == null)
            {
                return new List<KnowledgePackVersionContract>().AsReadOnly();
            }

            var versions = new List<KnowledgePackVersionContract>();
            AddVersion(versions, history.CurrentVersion);
            AddVersion(versions, history.ActiveVersion);
            AddVersion(versions, history.ArchivedVersion);
            if (history.PreviousVersions != null)
            {
                foreach (KnowledgePackVersionContract version in history.PreviousVersions)
                {
                    AddVersion(versions, version);
                }
            }

            return versions.AsReadOnly();
        }

        public KnowledgePackVersionHistoryContract GetVersionHistory(KnowledgeIdentifier packId)
        {
            string path = fileStore.GetHistoryPath(packId);
            return fileStore.FileExists(path)
                ? serializer.DeserializeVersionHistoryContract(fileStore.ReadText(path))
                : null;
        }

        public void Save(KnowledgePackPersistenceContract pack)
        {
            WritePersistenceContract(pack);
        }

        public void Update(KnowledgePackPersistenceContract pack)
        {
            WritePersistenceContract(pack);
        }

        public void Archive(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            KnowledgePackVersionContract version = ReadVersion(packId, versionId);
            version.LifecycleStatus = KnowledgePackLifecycleStatus.Archived;
            version.ArchivedAt = DateTimeOffset.UtcNow;
            version.IsCurrent = false;
            WriteVersion(version);

            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            if (history == null)
            {
                throw new FileNotFoundException("O histórico do Knowledge Pack não foi encontrado.");
            }

            if (SameVersion(history.CurrentVersion, versionId))
            {
                history.CurrentVersion = null;
            }

            if (SameVersion(history.ActiveVersion, versionId))
            {
                history.ActiveVersion = null;
            }

            history.ArchivedVersion = version;
            WriteHistory(history, packId);
        }

        public void RegisterVersion(KnowledgePackVersionContract version)
        {
            WriteVersion(version);

            KnowledgePackVersionHistoryContract history = GetVersionHistory(version.PackId) ??
                new KnowledgePackVersionHistoryContract
                {
                    PreviousVersions = new List<KnowledgePackVersionContract>().AsReadOnly()
                };

            var previous = history.PreviousVersions == null
                ? new List<KnowledgePackVersionContract>()
                : new List<KnowledgePackVersionContract>(history.PreviousVersions);

            if (history.CurrentVersion != null && !SameVersion(history.CurrentVersion, version.VersionId))
            {
                previous.Add(history.CurrentVersion);
            }

            history.CurrentVersion = version;
            history.PreviousVersions = previous.AsReadOnly();
            WriteHistory(history, version.PackId);
        }

        public void MarkVersionActive(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            KnowledgePackVersionContract version = ReadVersion(packId, versionId);
            version.LifecycleStatus = KnowledgePackLifecycleStatus.Active;
            WriteVersion(version);

            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            if (history == null)
            {
                throw new FileNotFoundException("O histórico do Knowledge Pack não foi encontrado.");
            }

            history.ActiveVersion = version;
            WriteHistory(history, packId);
        }

        public void CreateComposed(KnowledgePackPersistenceOperationContract operation)
        {
            ValidateCreateOperation(operation);

            KnowledgeIdentifier packId = operation.Persistence.Identity.PackId;
            using (fileStore.StorageLock.Acquire(packId))
            {
                CreateComposedCore(operation);
            }
        }

        /// <summary>
        /// Operação composta coordenada: uma única aquisição do PackLock cobre
        /// recovery, revalidação e a escrita composta (CREATE ou UPDATE).
        /// Não chama os wrappers públicos, para não readquirir FileShare.None.
        /// </summary>
        public KnowledgePackComposedPersistenceResult PersistComposed(
            KnowledgePackPersistenceOperationContract operation)
        {
            bool isCreate = operation == null || operation.PreviousVersionIdToArchive == null;
            if (isCreate)
            {
                ValidateCreateOperation(operation);
            }
            else
            {
                ValidateUpdateOperation(operation);
            }

            KnowledgeIdentifier packId = operation.Persistence.Identity.PackId;
            using (fileStore.StorageLock.Acquire(packId))
            {
                KnowledgePackRecoveryResult recovery = isCreate
                    ? RecoverCreateTransactionsCore(packId)
                    : RecoverUpdateTransactionsCore(packId);

                if (recovery == null || recovery.Status == KnowledgePackRecoveryStatus.Unrecoverable)
                {
                    return new KnowledgePackComposedPersistenceResult
                    {
                        Status = KnowledgePackComposedPersistenceStatus.BlockedByUnrecoverableTransaction,
                        RecoveryStatus = recovery == null
                            ? KnowledgePackRecoveryStatus.Unrecoverable
                            : recovery.Status,
                        TransactionId = recovery == null ? null : recovery.TransactionId,
                        FailureReason = recovery == null
                            ? "A recuperação da transação pendente não retornou resultado."
                            : recovery.FailureReason
                    };
                }

                if (isCreate)
                {
                    if (PublishedDataExists(packId, operation.Version.VersionId))
                    {
                        return new KnowledgePackComposedPersistenceResult
                        {
                            Status = KnowledgePackComposedPersistenceStatus.SupersededByExistingState,
                            RecoveryStatus = recovery.Status,
                            TransactionId = recovery.TransactionId,
                            FailureReason = "O Knowledge Pack já possui dados persistidos."
                        };
                    }

                    CreateComposedCore(operation);
                }
                else
                {
                    if (!PersistedStateMatchesPreviousVersion(packId, operation.PreviousVersionIdToArchive))
                    {
                        return new KnowledgePackComposedPersistenceResult
                        {
                            Status = KnowledgePackComposedPersistenceStatus.SupersededByExistingState,
                            RecoveryStatus = recovery.Status,
                            TransactionId = recovery.TransactionId,
                            FailureReason = "O estado persistido do Pack não corresponde mais à versão anterior informada."
                        };
                    }

                    UpdateComposedCore(operation);
                }

                return new KnowledgePackComposedPersistenceResult
                {
                    Status = KnowledgePackComposedPersistenceStatus.Persisted,
                    RecoveryStatus = recovery.Status,
                    TransactionId = recovery.TransactionId
                };
            }
        }

        /// <summary>
        /// Leitura coordenada: uma unica aquisicao do PackLock cobre deteccao de
        /// transacao pendente, recovery (quando necessario), validacao minima e a
        /// leitura do manifest + versao ativa fisica. Nao existe release/reacquire
        /// entre recovery e leitura, e nenhum wrapper publico e chamado (evita
        /// reabrir o mesmo FileShare.None).
        /// </summary>
        public KnowledgePackReadSnapshot ReadComposed(KnowledgeIdentifier packId)
        {
            if (packId == null || String.IsNullOrWhiteSpace(packId.Value))
            {
                throw new ArgumentException("O identificador do Pack deve ser informado.", nameof(packId));
            }

            // Validacao do identificador (mesma regra de path da IAZ:25) antes de
            // qualquer path de lock/recovery: identificador invalido nao pode virar
            // Unresolved nem alcancar o diretorio de locks.
            fileStore.GetPackDirectory(packId);

            IDisposable lockHandle;
            try
            {
                lockHandle = fileStore.StorageLock.Acquire(packId);
            }
            catch (KnowledgePackStorageLockTimeoutException exception)
            {
                // Contencao de lock nao e corrupcao: indisponibilidade temporaria.
                return UnresolvedSnapshot(null, "O PackLock do Pack nao pode ser adquirido para leitura: " + exception.Message);
            }

            using (lockHandle)
            {
                KnowledgePackRecoveryResult recovery = RecoverPendingTransactionUnderLock(packId);
                if (recovery != null && recovery.Status == KnowledgePackRecoveryStatus.Unrecoverable)
                {
                    return UnresolvedSnapshot(
                        recovery,
                        recovery.FailureReason ?? "A transacao pendente do Pack nao pode ser recuperada.");
                }

                try
                {
                    return BuildReadSnapshotUnderLock(packId, recovery);
                }
                catch (Exception exception)
                {
                    // Arquivo fisico ilegivel/corrompido nao pode virar leitura parcial:
                    // devolve Unresolved preservando o estado no disco.
                    return UnresolvedSnapshot(recovery, "O estado fisico do Pack nao pode ser lido com seguranca: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// Detecta e recupera a transacao pendente usando SOMENTE os Core (sem
        /// readquirir o PackLock). O journal e a fonte da classificacao CREATE x UPDATE;
        /// os proprios Core recusam o tipo errado como Unrecoverable.
        /// Nao adquire lock: deve ser chamado dentro da regiao critica.
        /// </summary>
        private KnowledgePackRecoveryResult RecoverPendingTransactionUnderLock(KnowledgeIdentifier packId)
        {
            IReadOnlyList<KnowledgeIdentifier> transactionIds;
            try
            {
                transactionIds = fileStore.ListTransactionIds(packId);
            }
            catch (Exception exception)
            {
                return Unrecoverable(null, exception.Message);
            }

            if (transactionIds == null || transactionIds.Count == 0)
            {
                return new KnowledgePackRecoveryResult
                {
                    Status = KnowledgePackRecoveryStatus.NothingToRecover
                };
            }

            if (transactionIds.Count != 1)
            {
                return Unrecoverable(null, "Existem mÃºltiplas transaÃ§Ãµes pendentes para o mesmo Pack.");
            }

            KnowledgeIdentifier transactionId = transactionIds[0];
            try
            {
                if (!fileStore.JournalExists(packId, transactionId))
                {
                    return Unrecoverable(transactionId, "A transaÃ§Ã£o nÃ£o possui journal legÃ­vel.");
                }

                KnowledgePackJournalContract journal = ReadJournal(packId, transactionId);
                ValidateJournal(packId, transactionId, journal);
                fileStore.GetIncomingDirectory(packId, transactionId, journal.StagingDirectoryName);

                return journal.PreviousVersionIdToArchive == null
                    ? RecoverCreateTransactionsCore(packId)
                    : RecoverUpdateTransactionsCore(packId);
            }
            catch (Exception exception)
            {
                return Unrecoverable(transactionId, exception.Message);
            }
        }

        /// <summary>
        /// Validacao minima de read safety, executada sob o PackLock ja adquirido.
        /// Nao cobre integridade semantica completa (Current/Active unicos,
        /// PreviousVersions, flags de lifecycle): isso pertence a etapa de integridade.
        /// </summary>
        private KnowledgePackReadSnapshot BuildReadSnapshotUnderLock(
            KnowledgeIdentifier packId,
            KnowledgePackRecoveryResult recovery)
        {
            KnowledgePackPersistenceContract persistence = Get(packId);
            if (persistence == null)
            {
                if (GetVersionHistory(packId) != null || fileStore.AnyVersionFileExists(packId))
                {
                    return UnresolvedSnapshot(recovery, "O Pack possui histÃ³rico ou versÃµes fÃ­sicas sem manifest coerente.");
                }

                return NotFoundSnapshot(recovery);
            }

            if (persistence.Identity == null ||
                persistence.Identity.PackId == null ||
                !SameIdentifier(persistence.Identity.PackId, packId))
            {
                return UnresolvedSnapshot(recovery, "O manifest do Pack nÃ£o corresponde ao identificador solicitado.");
            }

            string formatFailureReason;
            KnowledgePackPersistenceFormat format = ClassifyPersistenceFormat(
                persistence,
                packId,
                out formatFailureReason);
            if (format == KnowledgePackPersistenceFormat.Corrupt)
            {
                return UnresolvedSnapshot(recovery, formatFailureReason);
            }

            if (format == KnowledgePackPersistenceFormat.LegacyRecognized)
            {
                // Formato legado reconhecido: nao e corrupcao, mas tambem nao pode ser
                // reidratado sem inventar Name, Status, References ou Metadata. Nenhuma
                // escrita e feita aqui; apenas a coerencia estrutural minima e verificada.
                return BuildLegacyReadSnapshotUnderLock(packId, persistence, recovery);
            }

            if (persistence.Content == null || persistence.Content.VersionId == null)
            {
                return UnresolvedSnapshot(recovery, "O manifest do Pack nÃ£o possui conteÃºdo reidratÃ¡vel.");
            }

            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            if (history == null || history.ActiveVersion == null || history.ActiveVersion.VersionId == null)
            {
                return UnresolvedSnapshot(recovery, "O histÃ³rico do Pack nÃ£o define uma versÃ£o ativa.");
            }

            if (!SameIdentifier(history.ActiveVersion.PackId, packId))
            {
                return UnresolvedSnapshot(recovery, "A versÃ£o ativa do histÃ³rico pertence a outro Pack.");
            }

            if (!SameIdentifier(persistence.Content.VersionId, history.ActiveVersion.VersionId))
            {
                return UnresolvedSnapshot(recovery, "O manifest e o histÃ³rico divergem quanto Ã  versÃ£o ativa.");
            }

            KnowledgeIdentifier activeVersionId = history.ActiveVersion.VersionId;
            if (!fileStore.FileExists(fileStore.GetVersionPath(packId, activeVersionId)))
            {
                return UnresolvedSnapshot(recovery, "O arquivo fÃ­sico da versÃ£o ativa nÃ£o existe.");
            }

            KnowledgePackVersionContract physicalVersion = ReadVersion(packId, activeVersionId);
            if (physicalVersion == null ||
                physicalVersion.VersionId == null ||
                !SameIdentifier(physicalVersion.VersionId, activeVersionId) ||
                !SameIdentifier(physicalVersion.PackId, packId))
            {
                return UnresolvedSnapshot(recovery, "O arquivo fÃ­sico da versÃ£o ativa Ã© incoerente com o Pack.");
            }

            if (history.CurrentVersion == null || history.CurrentVersion.VersionId == null)
            {
                return UnresolvedSnapshot(recovery, "O historico do Pack nao define uma versao corrente.");
            }

            if (!SameIdentifier(history.CurrentVersion.PackId, packId))
            {
                return UnresolvedSnapshot(recovery, "A versao corrente do historico pertence a outro Pack.");
            }

            if (!SameIdentifier(history.CurrentVersion.VersionId, activeVersionId))
            {
                return UnresolvedSnapshot(recovery, "O historico do Pack possui versao corrente divergente da versao ativa.");
            }

            if (!SameIdentifier(persistence.Content.VersionId, history.CurrentVersion.VersionId))
            {
                return UnresolvedSnapshot(recovery, "O manifest e o historico divergem quanto a versao corrente.");
            }

            if (!physicalVersion.IsCurrent || physicalVersion.LifecycleStatus != KnowledgePackLifecycleStatus.Active)
            {
                return UnresolvedSnapshot(recovery, "O arquivo fisico da versao corrente nao esta marcado como corrente e ativo.");
            }

            if (String.IsNullOrWhiteSpace(physicalVersion.VersionNumber))
            {
                return UnresolvedSnapshot(recovery, "A versao corrente do Pack nao possui numero de versao.");
            }

            return new KnowledgePackReadSnapshot
            {
                Status = KnowledgePackReadStatus.Ready,
                Persistence = persistence,
                ActiveVersion = physicalVersion,
                Recovery = recovery
            };
        }

        private static KnowledgePackReadSnapshot NotFoundSnapshot(KnowledgePackRecoveryResult recovery)
        {
            return new KnowledgePackReadSnapshot
            {
                Status = KnowledgePackReadStatus.NotFound,
                Recovery = recovery
            };
        }

        private static KnowledgePackReadSnapshot UnresolvedSnapshot(
            KnowledgePackRecoveryResult recovery,
            string reason)
        {
            return new KnowledgePackReadSnapshot
            {
                Status = KnowledgePackReadStatus.Unresolved,
                Recovery = recovery,
                FailureReason = reason
            };
        }

        private static KnowledgePackReadSnapshot LegacyUnsupportedSnapshot(
            KnowledgePackPersistenceContract persistence,
            KnowledgePackRecoveryResult recovery)
        {
            return new KnowledgePackReadSnapshot
            {
                Status = KnowledgePackReadStatus.LegacyUnsupported,
                Persistence = persistence,
                Recovery = recovery,
                FailureReason = "O manifest do Pack esta no formato legado reconhecido (sem conteudo estruturado); " +
                    "a reidratacao exigiria inventar Name, Status, References ou Metadata."
            };
        }

        /// <summary>
        /// Classificacao estrutural do manifest sob o PackLock. Content ausente/null e o
        /// formato legado conhecido; Content presente mas incompleto/incoerente e corrupcao
        /// do formato moderno. Nao cria geracao intermediaria sem evidencia real.
        /// </summary>
        private static KnowledgePackPersistenceFormat ClassifyPersistenceFormat(
            KnowledgePackPersistenceContract persistence,
            KnowledgeIdentifier packId,
            out string failureReason)
        {
            failureReason = null;

            if (persistence == null)
            {
                failureReason = "O manifest do Pack nao pode ser lido.";
                return KnowledgePackPersistenceFormat.Corrupt;
            }

            if (persistence.Identity == null ||
                persistence.Identity.PackId == null ||
                !SameIdentifier(persistence.Identity.PackId, packId))
            {
                failureReason = "O manifest do Pack nao corresponde ao identificador solicitado.";
                return KnowledgePackPersistenceFormat.Corrupt;
            }

            if (persistence.Content == null)
            {
                return KnowledgePackPersistenceFormat.LegacyRecognized;
            }

            if (persistence.Content.VersionId == null)
            {
                failureReason = "O manifest possui conteudo sem versao de conteudo.";
                return KnowledgePackPersistenceFormat.Corrupt;
            }

            if (!IsKnowledgeStatusDefined(persistence.Content.Status))
            {
                failureReason = "O conteudo do manifest possui status fora do dominio conhecido.";
                return KnowledgePackPersistenceFormat.Corrupt;
            }

            return KnowledgePackPersistenceFormat.Modern;
        }

        /// <summary>
        /// Snapshot para manifest legado reconhecido. Verifica somente o suficiente para
        /// separar legado de corrupcao: historico presente, versao corrente igual a ativa,
        /// PackIds coerentes e arquivo fisico da versao ativa existente, legivel e pertencente
        /// ao mesmo Pack. Nao reescreve manifest, nao migra e nao inventa campos.
        /// </summary>
        private KnowledgePackReadSnapshot BuildLegacyReadSnapshotUnderLock(
            KnowledgeIdentifier packId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackRecoveryResult recovery)
        {
            string incoherence = DescribeLegacyIncoherenceUnderLock(packId);
            if (incoherence != null)
            {
                return UnresolvedSnapshot(recovery, incoherence);
            }

            return LegacyUnsupportedSnapshot(persistence, recovery);
        }

        private string DescribeLegacyIncoherenceUnderLock(KnowledgeIdentifier packId)
        {
            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            if (history == null)
            {
                return "O Pack legado nao possui historico persistido.";
            }

            if (history.CurrentVersion == null || history.CurrentVersion.VersionId == null)
            {
                return "O historico do Pack legado nao define versao corrente.";
            }

            if (history.ActiveVersion == null || history.ActiveVersion.VersionId == null)
            {
                return "O historico do Pack legado nao define versao ativa.";
            }

            if (!SameIdentifier(history.CurrentVersion.PackId, packId) ||
                !SameIdentifier(history.ActiveVersion.PackId, packId))
            {
                return "O historico do Pack legado referencia versao de outro Pack.";
            }

            if (!SameIdentifier(history.CurrentVersion.VersionId, history.ActiveVersion.VersionId))
            {
                return "O historico do Pack legado diverge entre versao corrente e versao ativa.";
            }

            KnowledgeIdentifier activeVersionId = history.ActiveVersion.VersionId;
            if (!fileStore.FileExists(fileStore.GetVersionPath(packId, activeVersionId)))
            {
                return "O arquivo fisico da versao ativa do Pack legado nao existe.";
            }

            string activeIncoherence = DescribeLegacyVersionFileIncoherence(packId, activeVersionId);
            if (activeIncoherence != null)
            {
                return activeIncoherence;
            }

            if (history.PreviousVersions != null)
            {
                foreach (KnowledgePackVersionContract previous in history.PreviousVersions)
                {
                    if (previous == null || previous.VersionId == null)
                    {
                        return "O historico do Pack legado possui entrada invalida em PreviousVersions.";
                    }

                    if (SameIdentifier(previous.VersionId, activeVersionId))
                    {
                        return "O historico do Pack legado referencia a versao corrente em PreviousVersions.";
                    }

                    string previousIncoherence = DescribeOptionalLegacyVersionFileIncoherence(
                        packId,
                        previous.VersionId);
                    if (previousIncoherence != null)
                    {
                        return previousIncoherence;
                    }
                }
            }

            if (history.ArchivedVersion != null && history.ArchivedVersion.VersionId != null)
            {
                if (SameIdentifier(history.ArchivedVersion.VersionId, activeVersionId))
                {
                    return "A versao arquivada do historico legado coincide com a versao corrente.";
                }

                string archivedIncoherence = DescribeOptionalLegacyVersionFileIncoherence(
                    packId,
                    history.ArchivedVersion.VersionId);
                if (archivedIncoherence != null)
                {
                    return archivedIncoherence;
                }
            }

            return null;
        }

        private string DescribeLegacyVersionFileIncoherence(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier versionId)
        {
            KnowledgePackVersionContract version;
            try
            {
                version = ReadVersion(packId, versionId);
            }
            catch (Exception)
            {
                return "O arquivo fisico da versao " + versionId.Value + " do Pack legado nao pode ser lido.";
            }

            if (version == null ||
                version.VersionId == null ||
                !SameIdentifier(version.VersionId, versionId) ||
                !SameIdentifier(version.PackId, packId))
            {
                return "O arquivo fisico da versao " + versionId.Value + " do Pack legado e incoerente com o Pack.";
            }

            if (!IsLifecycleStatusDefined(version.LifecycleStatus))
            {
                return "A versao " + versionId.Value + " do Pack legado possui lifecycle fora do dominio conhecido.";
            }

            return null;
        }

        private string DescribeOptionalLegacyVersionFileIncoherence(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier versionId)
        {
            // Referencia historica sem arquivo fisico nao invalida, por si so, a classificacao
            // de legado: nesta etapa apenas arquivos que existem sao verificados.
            if (!fileStore.FileExists(fileStore.GetVersionPath(packId, versionId)))
            {
                return null;
            }

            return DescribeLegacyVersionFileIncoherence(packId, versionId);
        }

        private static bool IsLifecycleStatusDefined(KnowledgePackLifecycleStatus status)
        {
            return Enum.IsDefined(typeof(KnowledgePackLifecycleStatus), status);
        }

        private static bool IsKnowledgeStatusDefined(KnowledgeStatus status)
        {
            return Enum.IsDefined(typeof(KnowledgeStatus), status);
        }

        /// <summary>
        /// Diagnostico DEEP sob uma unica aquisicao do PackLock. Executa recovery seguro
        /// quando ha transacao pendente e recusa o diagnostico quando ela e irrecuperavel.
        /// Nao executa nada fora do lock e nao altera o estado alem do roll-forward ja
        /// previsto pelo recovery.
        /// </summary>
        public KnowledgePackIntegrityReport ValidateComposed(KnowledgeIdentifier packId)
        {
            if (packId == null || String.IsNullOrWhiteSpace(packId.Value))
            {
                throw new ArgumentException("O identificador do Pack deve ser informado.", nameof(packId));
            }

            fileStore.GetPackDirectory(packId);

            using (fileStore.StorageLock.Acquire(packId))
            {
                KnowledgePackRecoveryResult recovery = RecoverPendingTransactionUnderLock(packId);
                if (recovery != null && recovery.Status == KnowledgePackRecoveryStatus.Unrecoverable)
                {
                    var blocked = new List<string> { "A transacao pendente do Pack nao pode ser recuperada." };
                    return InconsistentIntegrityReport(packId, blocked, recovery.FailureReason);
                }

                return ValidateVersionIntegrityUnderLock(packId);
            }
        }

        /// <summary>
        /// DEEP de escrita: executada com o PackLock ja adquirido, antes de Committed e
        /// cleanup. Em caso de divergencia lanca, preservando journal e staging para
        /// diagnostico e recovery. Nenhum rollback novo e iniciado.
        /// </summary>
        private void EnsureIntegrityAfterPublish(KnowledgeIdentifier packId)
        {
            KnowledgePackIntegrityReport integrity = ValidateVersionIntegrityUnderLock(packId);
            if (integrity == null || integrity.Status != KnowledgePackIntegrityStatus.Consistent)
            {
                throw new InvalidOperationException(
                    "A integridade semantica do Pack nao foi confirmada apos a publicacao: " +
                    (integrity == null ? "relatorio ausente" : integrity.FailureReason));
            }
        }

        /// <summary>
        /// DEEP de recovery: executada com o PackLock ja adquirido, antes de Committed e
        /// cleanup. Quando a integridade final nao e confirmada, devolve Unrecoverable sem
        /// apagar journal nem staging, preservando evidencia para diagnostico. Nao inicia
        /// rollback e nao dispara novo recovery (evita loop recovery -> DEEP -> recovery).
        /// </summary>
        private KnowledgePackRecoveryResult EnsureIntegrityAfterRecovery(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId)
        {
            KnowledgePackIntegrityReport integrity = ValidateVersionIntegrityUnderLock(packId);
            if (integrity != null && integrity.Status == KnowledgePackIntegrityStatus.Consistent)
            {
                return null;
            }

            return Unrecoverable(
                transactionId,
                "A integridade semantica do Pack nao foi confirmada apos a recuperacao: " +
                (integrity == null ? "relatorio ausente" : integrity.FailureReason));
        }

        /// <summary>
        /// Validacao DEEP de integridade semantica de versoes. Nao adquire PackLock, nao
        /// executa recovery, nao altera e nao apaga arquivos. Deve ser chamada somente com
        /// o PackLock ja adquirido (write, recovery ou diagnostico) para nao criar TOCTOU
        /// com writers que modificam versions/.
        /// </summary>
        private KnowledgePackIntegrityReport ValidateVersionIntegrityUnderLock(KnowledgeIdentifier packId)
        {
            var violations = new List<string>();

            KnowledgePackPersistenceContract persistence = Get(packId);
            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            IReadOnlyList<KnowledgeIdentifier> physicalIds = fileStore.ListVersionIds(packId);

            if (persistence == null)
            {
                if (history != null)
                {
                    violations.Add("Existe historico persistido sem manifest.");
                }

                if (physicalIds.Count > 0)
                {
                    violations.Add("Existem arquivos de versao sem manifest.");
                }

                return violations.Count == 0
                    ? ConsistentIntegrityReport(packId)
                    : InconsistentIntegrityReport(packId, violations, "O Pack nao possui manifest coerente.");
            }

            if (persistence.Identity == null || persistence.Identity.PackId == null ||
                !SameIdentifier(persistence.Identity.PackId, packId))
            {
                violations.Add("O manifest nao corresponde ao Pack solicitado.");
            }

            if (persistence.Content == null)
            {
                // Formato legado reconhecido: a validacao DEEP moderna nao se aplica. Nao e
                // "Consistent moderno" e nao e divergencia por si so; a classificacao retorna
                // LegacyUnsupported com diagnostico proprio e sem inventar campos.
                if (violations.Count > 0)
                {
                    return InconsistentIntegrityReport(packId, violations, violations[0]);
                }

                string legacyIncoherence = DescribeLegacyIncoherenceUnderLock(packId);
                if (legacyIncoherence != null)
                {
                    return InconsistentIntegrityReport(
                        packId,
                        new List<string> { legacyIncoherence },
                        legacyIncoherence);
                }

                return LegacyUnsupportedIntegrityReport(packId);
            }

            if (persistence.Content.VersionId == null)
            {
                violations.Add("O manifest possui conteudo sem versao de conteudo.");
            }

            if (!IsKnowledgeStatusDefined(persistence.Content.Status))
            {
                violations.Add("O conteudo do manifest possui status fora do dominio conhecido.");
            }

            if (history == null)
            {
                violations.Add("O Pack nao possui historico persistido.");
                return InconsistentIntegrityReport(packId, violations, "O Pack nao possui historico persistido.");
            }

            if (history.CurrentVersion == null || history.CurrentVersion.VersionId == null)
            {
                violations.Add("O historico nao possui versao corrente.");
            }

            if (history.ActiveVersion == null || history.ActiveVersion.VersionId == null)
            {
                violations.Add("O historico nao possui versao ativa.");
            }

            if (violations.Count > 0)
            {
                return InconsistentIntegrityReport(packId, violations, "O historico do Pack nao define versao corrente e ativa.");
            }

            KnowledgeIdentifier currentVersionId = history.CurrentVersion.VersionId;
            KnowledgeIdentifier activeVersionId = history.ActiveVersion.VersionId;

            if (!SameIdentifier(currentVersionId, activeVersionId))
            {
                violations.Add("A versao corrente diverge da versao ativa no historico.");
            }

            if (persistence.Content.VersionId != null &&
                !SameIdentifier(persistence.Content.VersionId, currentVersionId))
            {
                violations.Add("O manifest diverge do historico quanto a versao corrente.");
            }

            var physicalVersions = new List<KnowledgePackVersionContract>();
            foreach (KnowledgeIdentifier physicalId in physicalIds)
            {
                KnowledgePackVersionContract version;
                try
                {
                    version = ReadVersion(packId, physicalId);
                }
                catch (Exception)
                {
                    violations.Add("O arquivo da versao " + physicalId.Value + " nao pode ser lido.");
                    continue;
                }

                if (version == null || version.VersionId == null || !SameIdentifier(version.VersionId, physicalId))
                {
                    violations.Add("O arquivo versions/" + physicalId.Value + ".json possui VersionId interno divergente.");
                    continue;
                }

                if (!SameIdentifier(version.PackId, packId))
                {
                    violations.Add("O arquivo versions/" + physicalId.Value + ".json pertence a outro Pack.");
                    continue;
                }

                if (!IsVersionNumberValid(version.VersionNumber))
                {
                    violations.Add("O arquivo versions/" + physicalId.Value + ".json possui numero de versao invalido.");
                }

                physicalVersions.Add(version);
            }

            int currentCount = 0;
            int activeCount = 0;
            bool currentExists = false;

            foreach (KnowledgePackVersionContract version in physicalVersions)
            {
                if (version.IsCurrent)
                {
                    currentCount++;
                }

                if (version.LifecycleStatus == KnowledgePackLifecycleStatus.Active)
                {
                    activeCount++;
                }

                if (!SameIdentifier(version.VersionId, currentVersionId))
                {
                    continue;
                }

                currentExists = true;
                if (!version.IsCurrent)
                {
                    violations.Add("A versao corrente nao esta marcada como corrente.");
                }

                if (version.LifecycleStatus != KnowledgePackLifecycleStatus.Active)
                {
                    violations.Add("A versao corrente nao possui lifecycle Active.");
                }
            }

            if (currentCount != 1)
            {
                violations.Add("O Pack possui " + currentCount + " versoes fisicas marcadas como correntes.");
            }

            if (activeCount != 1)
            {
                violations.Add("O Pack possui " + activeCount + " versoes fisicas marcadas como ativas.");
            }

            if (!currentExists)
            {
                violations.Add("O arquivo fisico da versao corrente nao existe.");
            }

            var previousIds = new List<string>();
            if (history.PreviousVersions != null)
            {
                foreach (KnowledgePackVersionContract previous in history.PreviousVersions)
                {
                    if (previous == null || previous.VersionId == null)
                    {
                        violations.Add("O historico possui entrada invalida em PreviousVersions.");
                        continue;
                    }

                    if (previousIds.Contains(previous.VersionId.Value))
                    {
                        violations.Add("O historico possui VersionId duplicado em PreviousVersions.");
                        continue;
                    }

                    previousIds.Add(previous.VersionId.Value);

                    if (SameIdentifier(previous.VersionId, currentVersionId))
                    {
                        violations.Add("A versao corrente aparece em PreviousVersions.");
                        continue;
                    }

                    KnowledgePackVersionContract physicalPrevious = null;
                    foreach (KnowledgePackVersionContract version in physicalVersions)
                    {
                        if (SameIdentifier(version.VersionId, previous.VersionId))
                        {
                            physicalPrevious = version;
                            break;
                        }
                    }

                    if (physicalPrevious == null)
                    {
                        violations.Add("PreviousVersions referencia a versao " + previous.VersionId.Value + " sem arquivo fisico.");
                        continue;
                    }

                    if (physicalPrevious.IsCurrent)
                    {
                        violations.Add("A versao " + previous.VersionId.Value + " esta marcada como corrente fora do historico.");
                    }

                    if (physicalPrevious.LifecycleStatus != KnowledgePackLifecycleStatus.Archived)
                    {
                        violations.Add("A versao " + previous.VersionId.Value + " nao esta marcada como Archived.");
                    }
                }
            }

            if (history.ArchivedVersion != null && history.ArchivedVersion.VersionId != null)
            {
                if (SameIdentifier(history.ArchivedVersion.VersionId, currentVersionId))
                {
                    violations.Add("A versao arquivada do historico coincide com a versao corrente.");
                }

                if (!previousIds.Contains(history.ArchivedVersion.VersionId.Value))
                {
                    violations.Add("A versao arquivada do historico nao aparece em PreviousVersions.");
                }
                else
                {
                    KnowledgePackVersionContract physicalArchived = FindPhysicalVersion(
                        physicalVersions, history.ArchivedVersion.VersionId);
                    if (physicalArchived == null)
                    {
                        violations.Add("A versao arquivada do historico nao possui arquivo fisico.");
                    }
                    else
                    {
                        if (physicalArchived.IsCurrent)
                        {
                            violations.Add("A versao arquivada do historico esta marcada como corrente.");
                        }

                        if (physicalArchived.LifecycleStatus != KnowledgePackLifecycleStatus.Archived)
                        {
                            violations.Add("A versao arquivada do historico nao esta marcada como Archived.");
                        }
                    }
                }
            }

            foreach (KnowledgePackVersionContract version in physicalVersions)
            {
                if (SameIdentifier(version.VersionId, currentVersionId))
                {
                    continue;
                }

                if (version.VersionId != null && previousIds.Contains(version.VersionId.Value))
                {
                    continue;
                }

                violations.Add("Existe versao fisica fora do historico: " + (version.VersionId == null ? "?" : version.VersionId.Value) + ".");
            }

            if (violations.Count == 0)
            {
                return ConsistentIntegrityReport(packId);
            }

            return InconsistentIntegrityReport(packId, violations, violations[0]);
        }

        private static KnowledgePackIntegrityReport ConsistentIntegrityReport(KnowledgeIdentifier packId)
        {
            return new KnowledgePackIntegrityReport
            {
                Status = KnowledgePackIntegrityStatus.Consistent,
                PackId = packId,
                Violations = new List<string>().AsReadOnly()
            };
        }

        private static KnowledgePackIntegrityReport LegacyUnsupportedIntegrityReport(KnowledgeIdentifier packId)
        {
            return new KnowledgePackIntegrityReport
            {
                Status = KnowledgePackIntegrityStatus.LegacyUnsupported,
                PackId = packId,
                Violations = new List<string>().AsReadOnly(),
                FailureReason = "O Pack esta no formato legado reconhecido (manifest sem conteudo estruturado); " +
                    "a integridade moderna nao se aplica e nenhum campo foi inventado."
            };
        }

        private static KnowledgePackVersionContract FindPhysicalVersion(
            IReadOnlyList<KnowledgePackVersionContract> physicalVersions,
            KnowledgeIdentifier versionId)
        {
            if (physicalVersions == null || versionId == null)
            {
                return null;
            }

            foreach (KnowledgePackVersionContract version in physicalVersions)
            {
                if (version != null && SameIdentifier(version.VersionId, versionId))
                {
                    return version;
                }
            }

            return null;
        }

        private static KnowledgePackIntegrityReport InconsistentIntegrityReport(
            KnowledgeIdentifier packId,
            IReadOnlyList<string> violations,
            string failureReason)
        {
            return new KnowledgePackIntegrityReport
            {
                Status = KnowledgePackIntegrityStatus.Inconsistent,
                PackId = packId,
                Violations = violations,
                FailureReason = failureReason
            };
        }

        private static bool IsVersionNumberValid(string versionNumber)
        {
            if (String.IsNullOrWhiteSpace(versionNumber))
            {
                return false;
            }

            string[] parts = versionNumber.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            int major;
            int minor;
            int patch;
            return Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
                Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) &&
                Int32.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch) &&
                major >= 0 && minor >= 0 && patch >= 0;
        }

        private void CreateComposedCore(KnowledgePackPersistenceOperationContract operation)
        {
            ValidateCreateOperation(operation);

            KnowledgeIdentifier packId = operation.Persistence.Identity.PackId;
            KnowledgeIdentifier versionId = operation.Version.VersionId;
            if (PublishedDataExists(packId, versionId))
            {
                throw new InvalidOperationException("O Knowledge Pack já possui dados persistidos.");
            }

            // Identificador de transacao compacto (16 hex): e puramente interno e
            // transitorio. Com os PackIds project-scoped da IAZ:36 o nome longo do
            // diretorio de transacao empurrava o staging contra MAX_PATH.
            KnowledgeIdentifier transactionId = new KnowledgeIdentifier("transaction-" + Guid.NewGuid().ToString("N").Substring(0, 16));
            const string stagingDirectoryName = "incoming";
            fileStore.EnsureTransactionStructure(packId, transactionId, stagingDirectoryName);

            KnowledgePackVersionContract finalVersion = ActiveVersion(operation.Version);
            var finalHistory = new KnowledgePackVersionHistoryContract
            {
                CurrentVersion = finalVersion,
                ActiveVersion = finalVersion,
                ArchivedVersion = null,
                PreviousVersions = new List<KnowledgePackVersionContract>().AsReadOnly()
            };

            string stagedManifestPath = fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName);
            string stagedVersionPath = fileStore.GetIncomingVersionPath(packId, transactionId, versionId, stagingDirectoryName);
            string stagedHistoryPath = fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName);

            fileStore.WriteTransactionTextAtomically(
                stagedManifestPath,
                serializer.SerializePersistenceContract(operation.Persistence));
            fileStore.WriteTransactionTextAtomically(
                stagedVersionPath,
                serializer.SerializeVersionContract(finalVersion));
            fileStore.WriteTransactionTextAtomically(
                stagedHistoryPath,
                serializer.SerializeVersionHistoryContract(finalHistory));

            ValidateStagedCreate(packId, transactionId, operation.Persistence, finalVersion, finalHistory, stagingDirectoryName);

            var journal = new KnowledgePackJournalContract
            {
                SchemaVersion = 1,
                TransactionId = transactionId,
                PackId = packId,
                State = KnowledgePackJournalState.Prepared,
                VersionId = versionId,
                PreviousVersionIdToArchive = null,
                StagingDirectoryName = stagingDirectoryName
            };
            WriteJournal(journal);

            journal.State = KnowledgePackJournalState.Publishing;
            WriteJournal(journal);

            PublishCreate(packId, transactionId, versionId, stagingDirectoryName);
            ValidatePublishedCreate(packId, operation.Persistence, finalVersion, finalHistory);
            EnsureIntegrityAfterPublish(packId);

            journal.State = KnowledgePackJournalState.Committed;
            WriteJournal(journal);
            fileStore.DeleteTransaction(packId, transactionId);
        }

        public KnowledgePackRecoveryResult RecoverCreateTransactions(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                return Unrecoverable(null, "O Pack da recuperação não foi informado.");
            }

            using (fileStore.StorageLock.Acquire(packId))
            {
                return RecoverCreateTransactionsCore(packId);
            }
        }

        private KnowledgePackRecoveryResult RecoverCreateTransactionsCore(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                return Unrecoverable(null, "O Pack da recuperação não foi informado.");
            }

            IReadOnlyList<KnowledgeIdentifier> transactionIds;
            try
            {
                transactionIds = fileStore.ListTransactionIds(packId);
            }
            catch (Exception exception)
            {
                return Unrecoverable(null, exception.Message);
            }

            if (transactionIds == null || transactionIds.Count == 0)
            {
                return new KnowledgePackRecoveryResult
                {
                    Status = KnowledgePackRecoveryStatus.NothingToRecover
                };
            }

            if (transactionIds.Count != 1)
            {
                return Unrecoverable(null, "Existem múltiplas transações pendentes para o mesmo Pack.");
            }

            KnowledgeIdentifier transactionId = transactionIds[0];
            try
            {
                if (!fileStore.JournalExists(packId, transactionId))
                {
                    return Unrecoverable(transactionId, "A transação não possui journal legível.");
                }

                KnowledgePackJournalContract journal = ReadJournal(packId, transactionId);
                ValidateJournal(packId, transactionId, journal);
                fileStore.GetIncomingDirectory(packId, transactionId, journal.StagingDirectoryName);

                if (journal.PreviousVersionIdToArchive != null)
                {
                    return Unrecoverable(transactionId, "A transação pertence a UPDATE e não pode ser recuperada como CREATE.");
                }

                if (journal.State == KnowledgePackJournalState.Prepared)
                {
                    StagedCreate staged = ReadStagedCreate(packId, transactionId, journal);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    if (PublishedFilesExist(packId, journal.VersionId))
                    {
                        return Unrecoverable(transactionId, "A transação Prepared encontrou arquivos finais parciais.");
                    }

                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                if (journal.State == KnowledgePackJournalState.Publishing)
                {
                    StagedCreate staged = ReadStagedCreate(packId, transactionId, journal);
                    PublishCreate(packId, transactionId, journal.VersionId, journal.StagingDirectoryName);
                    ValidatePublishedCreate(packId, staged.Persistence, staged.Version, staged.History);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    journal.State = KnowledgePackJournalState.Committed;
                    WriteJournal(journal);
                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                if (journal.State == KnowledgePackJournalState.Committed)
                {
                    ValidatePublishedCreateState(packId, journal.VersionId);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                return Unrecoverable(transactionId, "O estado do journal não é suportado.");
            }
            catch (Exception exception)
            {
                return Unrecoverable(transactionId, exception.Message);
            }
        }

        public KnowledgePackRecoveryResult RecoverUpdateTransactions(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                return Unrecoverable(null, "O Pack da recuperação não foi informado.");
            }

            using (fileStore.StorageLock.Acquire(packId))
            {
                return RecoverUpdateTransactionsCore(packId);
            }
        }

        private KnowledgePackRecoveryResult RecoverUpdateTransactionsCore(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                return Unrecoverable(null, "O Pack da recuperação não foi informado.");
            }

            IReadOnlyList<KnowledgeIdentifier> transactionIds;
            try
            {
                transactionIds = fileStore.ListTransactionIds(packId);
            }
            catch (Exception exception)
            {
                return Unrecoverable(null, exception.Message);
            }

            if (transactionIds == null || transactionIds.Count == 0)
            {
                return new KnowledgePackRecoveryResult
                {
                    Status = KnowledgePackRecoveryStatus.NothingToRecover
                };
            }

            if (transactionIds.Count != 1)
            {
                return Unrecoverable(null, "Existem múltiplas transações pendentes para o mesmo Pack.");
            }

            KnowledgeIdentifier transactionId = transactionIds[0];
            try
            {
                if (!fileStore.JournalExists(packId, transactionId))
                {
                    return Unrecoverable(transactionId, "A transação não possui journal legível.");
                }

                KnowledgePackJournalContract journal = ReadJournal(packId, transactionId);
                ValidateJournal(packId, transactionId, journal);
                fileStore.GetIncomingDirectory(packId, transactionId, journal.StagingDirectoryName);

                if (journal.PreviousVersionIdToArchive == null)
                {
                    return Unrecoverable(transactionId, "A transação pertence a CREATE e não pode ser recuperada como UPDATE.");
                }

                if (journal.State == KnowledgePackJournalState.Prepared)
                {
                    StagedUpdate staged = ReadStagedUpdate(packId, transactionId, journal);
                    PreviousUpdateState previous = ReadPreviousUpdateState(packId, transactionId, journal);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    if (!FinalStateMatchesPrevious(packId, previous) ||
                        fileStore.FileExists(fileStore.GetVersionPath(packId, journal.VersionId)))
                    {
                        return Unrecoverable(transactionId, "O estado Prepared apresenta sinais de publicação parcial.");
                    }

                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                if (journal.State == KnowledgePackJournalState.Publishing)
                {
                    StagedUpdate staged = ReadStagedUpdate(packId, transactionId, journal);
                    ReadPreviousUpdateState(packId, transactionId, journal);
                    PublishUpdate(packId, transactionId, journal.VersionId, journal.PreviousVersionIdToArchive, journal.StagingDirectoryName);
                    ValidatePublishedUpdate(packId, staged.Persistence, staged.NewVersion, staged.ArchivedVersion, staged.History);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    journal.State = KnowledgePackJournalState.Committed;
                    WriteJournal(journal);
                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                if (journal.State == KnowledgePackJournalState.Committed)
                {
                    ValidatePublishedUpdateState(packId, journal.VersionId, journal.PreviousVersionIdToArchive);
                    KnowledgePackRecoveryResult integrityFailure = EnsureIntegrityAfterRecovery(packId, transactionId);
                    if (integrityFailure != null)
                    {
                        return integrityFailure;
                    }

                    fileStore.DeleteTransaction(packId, transactionId);
                    return Recovered(transactionId);
                }

                return Unrecoverable(transactionId, "O estado do journal não é suportado.");
            }
            catch (Exception exception)
            {
                return Unrecoverable(transactionId, exception.Message);
            }
        }

        public void UpdateComposed(KnowledgePackPersistenceOperationContract operation)
        {
            ValidateUpdateOperation(operation);

            KnowledgeIdentifier packId = operation.Persistence.Identity.PackId;
            using (fileStore.StorageLock.Acquire(packId))
            {
                UpdateComposedCore(operation);
            }
        }

        private void UpdateComposedCore(KnowledgePackPersistenceOperationContract operation)
        {
            ValidateUpdateOperation(operation);

            KnowledgeIdentifier packId = operation.Persistence.Identity.PackId;
            KnowledgeIdentifier newVersionId = operation.Version.VersionId;
            KnowledgeIdentifier previousVersionId = operation.PreviousVersionIdToArchive;
            fileStore.GetTransactionsDirectory(packId);

            if (fileStore.ListTransactionIds(packId).Count != 0)
            {
                throw new InvalidOperationException("Já existe uma transação pendente para o Knowledge Pack.");
            }

            KnowledgePackPersistenceContract currentPersistence = Get(packId);
            KnowledgePackVersionHistoryContract currentHistory = GetVersionHistory(packId);
            KnowledgePackVersionContract currentVersion = ReadVersion(packId, previousVersionId);
            ValidateCurrentUpdateState(packId, previousVersionId, currentPersistence, currentHistory, currentVersion);

            if (fileStore.FileExists(fileStore.GetVersionPath(packId, newVersionId)))
            {
                throw new InvalidOperationException("A nova versão já possui um arquivo persistido.");
            }

            // Identificador de transacao compacto (16 hex): e puramente interno e
            // transitorio. Com os PackIds project-scoped da IAZ:36 o nome longo do
            // diretorio de transacao empurrava o staging contra MAX_PATH.
            KnowledgeIdentifier transactionId = new KnowledgeIdentifier("transaction-" + Guid.NewGuid().ToString("N").Substring(0, 16));
            const string stagingDirectoryName = "incoming";
            fileStore.EnsureTransactionStructure(packId, transactionId, stagingDirectoryName);

            KnowledgePackVersionContract archivedVersion = ArchivedVersion(currentVersion);
            KnowledgePackVersionHistoryContract finalHistory = BuildUpdateHistory(currentHistory, operation.Version, archivedVersion);

            fileStore.WriteTransactionTextAtomically(
                fileStore.GetPreviousManifestPath(packId, transactionId),
                serializer.SerializePersistenceContract(currentPersistence));
            fileStore.WriteTransactionTextAtomically(
                fileStore.GetPreviousHistoryPath(packId, transactionId),
                serializer.SerializeVersionHistoryContract(currentHistory));
            fileStore.WriteTransactionTextAtomically(
                fileStore.GetPreviousVersionPath(packId, transactionId, previousVersionId),
                serializer.SerializeVersionContract(currentVersion));
            ValidatePreviousSnapshot(packId, transactionId, currentPersistence, currentHistory, currentVersion);

            fileStore.WriteTransactionTextAtomically(
                fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName),
                serializer.SerializePersistenceContract(operation.Persistence));
            fileStore.WriteTransactionTextAtomically(
                fileStore.GetIncomingVersionPath(packId, transactionId, newVersionId, stagingDirectoryName),
                serializer.SerializeVersionContract(operation.Version));
            fileStore.WriteTransactionTextAtomically(
                fileStore.GetIncomingVersionPath(packId, transactionId, previousVersionId, stagingDirectoryName),
                serializer.SerializeVersionContract(archivedVersion));
            fileStore.WriteTransactionTextAtomically(
                fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName),
                serializer.SerializeVersionHistoryContract(finalHistory));
            ValidateStagedUpdate(packId, transactionId, operation.Persistence, operation.Version, archivedVersion, finalHistory, stagingDirectoryName);

            var journal = new KnowledgePackJournalContract
            {
                SchemaVersion = 1,
                TransactionId = transactionId,
                PackId = packId,
                State = KnowledgePackJournalState.Prepared,
                VersionId = newVersionId,
                PreviousVersionIdToArchive = previousVersionId,
                StagingDirectoryName = stagingDirectoryName
            };
            WriteJournal(journal);

            journal.State = KnowledgePackJournalState.Publishing;
            WriteJournal(journal);

            PublishUpdate(packId, transactionId, newVersionId, previousVersionId, stagingDirectoryName);
            ValidatePublishedUpdate(packId, operation.Persistence, operation.Version, archivedVersion, finalHistory);
            EnsureIntegrityAfterPublish(packId);

            journal.State = KnowledgePackJournalState.Committed;
            WriteJournal(journal);
            fileStore.DeleteTransaction(packId, transactionId);
        }

        private void WritePersistenceContract(KnowledgePackPersistenceContract pack)
        {
            string path = fileStore.GetManifestPath(pack.Identity.PackId);
            fileStore.EnsurePackStructure(pack.Identity.PackId);
            fileStore.WriteTextAtomically(path, serializer.SerializePersistenceContract(pack));
        }

        private void WriteVersion(KnowledgePackVersionContract version)
        {
            fileStore.EnsurePackStructure(version.PackId);
            string path = fileStore.GetVersionPath(version.PackId, version.VersionId);
            fileStore.WriteTextAtomically(path, serializer.SerializeVersionContract(version));
        }

        private void WriteHistory(KnowledgePackVersionHistoryContract history, KnowledgeIdentifier packId)
        {
            fileStore.EnsurePackStructure(packId);
            string path = fileStore.GetHistoryPath(packId);
            fileStore.WriteTextAtomically(path, serializer.SerializeVersionHistoryContract(history));
        }

        private void WriteJournal(KnowledgePackJournalContract journal)
        {
            string path = fileStore.GetJournalPath(journal.PackId, journal.TransactionId);
            fileStore.WriteTransactionTextAtomically(path, serializer.SerializeJournalContract(journal));
        }

        private KnowledgePackJournalContract ReadJournal(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId)
        {
            return serializer.DeserializeJournalContract(
                fileStore.ReadTransactionText(fileStore.GetJournalPath(packId, transactionId)));
        }

        private static void ValidateJournal(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackJournalContract journal)
        {
            if (journal == null || journal.SchemaVersion != 1 || journal.PackId == null ||
                journal.TransactionId == null || journal.VersionId == null ||
                String.IsNullOrWhiteSpace(journal.StagingDirectoryName) ||
                !Enum.IsDefined(typeof(KnowledgePackJournalState), journal.State) ||
                !SameIdentifier(journal.PackId, packId) ||
                !SameIdentifier(journal.TransactionId, transactionId))
            {
                throw new InvalidOperationException("O journal da transação é inválido.");
            }
        }

        private StagedCreate ReadStagedCreate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackJournalContract journal)
        {
            string manifestPath = fileStore.GetIncomingManifestPath(packId, transactionId, journal.StagingDirectoryName);
            string versionPath = fileStore.GetIncomingVersionPath(packId, transactionId, journal.VersionId, journal.StagingDirectoryName);
            string historyPath = fileStore.GetIncomingHistoryPath(packId, transactionId, journal.StagingDirectoryName);

            if (!fileStore.FileExists(manifestPath) || !fileStore.FileExists(versionPath) || !fileStore.FileExists(historyPath))
            {
                throw new InvalidOperationException("O staging da transação está incompleto.");
            }

            var staged = new StagedCreate
            {
                Persistence = serializer.DeserializePersistenceContract(fileStore.ReadTransactionText(manifestPath)),
                Version = serializer.DeserializeVersionContract(fileStore.ReadTransactionText(versionPath)),
                History = serializer.DeserializeVersionHistoryContract(fileStore.ReadTransactionText(historyPath))
            };

            ValidateCreateState(packId, staged.Persistence, staged.Version, staged.History);
            if (!SameIdentifier(staged.Version.VersionId, journal.VersionId))
            {
                throw new InvalidOperationException("A versão do staging diverge do journal.");
            }

            return staged;
        }

        private void PublishCreate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgeIdentifier versionId,
            string stagingDirectoryName)
        {
            // A publicação escreve fora da transação: garante a estrutura normal do Pack
            // (mesmo comportamento de WriteVersion/WriteHistory/WritePersistenceContract).
            fileStore.EnsurePackStructure(packId);

            string stagedVersionPath = fileStore.GetIncomingVersionPath(packId, transactionId, versionId, stagingDirectoryName);
            string stagedHistoryPath = fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName);
            string stagedManifestPath = fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName);

            fileStore.WriteTextAtomically(
                fileStore.GetVersionPath(packId, versionId),
                fileStore.ReadTransactionText(stagedVersionPath));
            fileStore.WriteTextAtomically(
                fileStore.GetHistoryPath(packId),
                fileStore.ReadTransactionText(stagedHistoryPath));
            fileStore.WriteTextAtomically(
                fileStore.GetManifestPath(packId),
                fileStore.ReadTransactionText(stagedManifestPath));
        }

        private void ValidateStagedCreate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract version,
            KnowledgePackVersionHistoryContract history,
            string stagingDirectoryName)
        {
            KnowledgePackPersistenceContract stagedPersistence = serializer.DeserializePersistenceContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName)));
            KnowledgePackVersionContract stagedVersion = serializer.DeserializeVersionContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingVersionPath(packId, transactionId, version.VersionId, stagingDirectoryName)));
            KnowledgePackVersionHistoryContract stagedHistory = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName)));

            ValidateCreateState(packId, persistence, version, history);
            ValidateCreateState(packId, stagedPersistence, stagedVersion, stagedHistory);
        }

        private void ValidatePublishedCreate(
            KnowledgeIdentifier packId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract version,
            KnowledgePackVersionHistoryContract history)
        {
            if (!fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, version.VersionId)) ||
                !fileStore.FileExists(fileStore.GetHistoryPath(packId)))
            {
                throw new InvalidOperationException("O conjunto publicado do Knowledge Pack está incompleto.");
            }

            KnowledgePackPersistenceContract publishedPersistence = serializer.DeserializePersistenceContract(
                fileStore.ReadText(fileStore.GetManifestPath(packId)));
            KnowledgePackVersionContract publishedVersion = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, version.VersionId)));
            KnowledgePackVersionHistoryContract publishedHistory = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadText(fileStore.GetHistoryPath(packId)));

            ValidateCreateState(packId, publishedPersistence, publishedVersion, publishedHistory);
            if (!String.Equals(serializer.SerializePersistenceContract(persistence), serializer.SerializePersistenceContract(publishedPersistence), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(version), serializer.SerializeVersionContract(publishedVersion), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionHistoryContract(history), serializer.SerializeVersionHistoryContract(publishedHistory), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("O conjunto publicado diverge do estado esperado.");
            }
        }

        private void ValidatePublishedCreateState(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            if (!fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, versionId)) ||
                !fileStore.FileExists(fileStore.GetHistoryPath(packId)))
            {
                throw new InvalidOperationException("O conjunto Committed está incompleto.");
            }

            KnowledgePackPersistenceContract persistence = serializer.DeserializePersistenceContract(
                fileStore.ReadText(fileStore.GetManifestPath(packId)));
            KnowledgePackVersionContract version = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, versionId)));
            KnowledgePackVersionHistoryContract history = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadText(fileStore.GetHistoryPath(packId)));
            ValidateCreateState(packId, persistence, version, history);
            if (!SameIdentifier(version.VersionId, versionId))
            {
                throw new InvalidOperationException("A versão Committed diverge do journal.");
            }
        }

        private void PublishUpdate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgeIdentifier newVersionId,
            KnowledgeIdentifier previousVersionId,
            string stagingDirectoryName)
        {
            // A publicação escreve fora da transação: garante a estrutura normal do Pack.
            fileStore.EnsurePackStructure(packId);

            fileStore.WriteTextAtomically(
                fileStore.GetVersionPath(packId, newVersionId),
                fileStore.ReadTransactionText(fileStore.GetIncomingVersionPath(packId, transactionId, newVersionId, stagingDirectoryName)));
            fileStore.WriteTextAtomically(
                fileStore.GetVersionPath(packId, previousVersionId),
                fileStore.ReadTransactionText(fileStore.GetIncomingVersionPath(packId, transactionId, previousVersionId, stagingDirectoryName)));
            fileStore.WriteTextAtomically(
                fileStore.GetHistoryPath(packId),
                fileStore.ReadTransactionText(fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName)));
            fileStore.WriteTextAtomically(
                fileStore.GetManifestPath(packId),
                fileStore.ReadTransactionText(fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName)));
        }

        private void ValidatePreviousSnapshot(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionHistoryContract history,
            KnowledgePackVersionContract version)
        {
            KnowledgePackPersistenceContract snapshotPersistence = serializer.DeserializePersistenceContract(
                fileStore.ReadTransactionText(fileStore.GetPreviousManifestPath(packId, transactionId)));
            KnowledgePackVersionHistoryContract snapshotHistory = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadTransactionText(fileStore.GetPreviousHistoryPath(packId, transactionId)));
            KnowledgePackVersionContract snapshotVersion = serializer.DeserializeVersionContract(
                fileStore.ReadTransactionText(fileStore.GetPreviousVersionPath(packId, transactionId, version.VersionId)));

            if (!String.Equals(serializer.SerializePersistenceContract(persistence), serializer.SerializePersistenceContract(snapshotPersistence), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionHistoryContract(history), serializer.SerializeVersionHistoryContract(snapshotHistory), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(version), serializer.SerializeVersionContract(snapshotVersion), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("O snapshot anterior diverge do estado atual.");
            }
        }

        private void ValidateStagedUpdate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract newVersion,
            KnowledgePackVersionContract archivedVersion,
            KnowledgePackVersionHistoryContract history,
            string stagingDirectoryName)
        {
            KnowledgePackPersistenceContract stagedPersistence = serializer.DeserializePersistenceContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingManifestPath(packId, transactionId, stagingDirectoryName)));
            KnowledgePackVersionContract stagedNewVersion = serializer.DeserializeVersionContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingVersionPath(packId, transactionId, newVersion.VersionId, stagingDirectoryName)));
            KnowledgePackVersionContract stagedArchivedVersion = serializer.DeserializeVersionContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingVersionPath(packId, transactionId, archivedVersion.VersionId, stagingDirectoryName)));
            KnowledgePackVersionHistoryContract stagedHistory = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadTransactionText(fileStore.GetIncomingHistoryPath(packId, transactionId, stagingDirectoryName)));

            ValidateUpdateState(packId, persistence, newVersion, archivedVersion, history);
            ValidateUpdateState(packId, stagedPersistence, stagedNewVersion, stagedArchivedVersion, stagedHistory);
            if (!String.Equals(serializer.SerializePersistenceContract(persistence), serializer.SerializePersistenceContract(stagedPersistence), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(newVersion), serializer.SerializeVersionContract(stagedNewVersion), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(archivedVersion), serializer.SerializeVersionContract(stagedArchivedVersion), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionHistoryContract(history), serializer.SerializeVersionHistoryContract(stagedHistory), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("O estado staged do UPDATE diverge do estado esperado.");
            }
        }

        private StagedUpdate ReadStagedUpdate(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackJournalContract journal)
        {
            string manifestPath = fileStore.GetIncomingManifestPath(packId, transactionId, journal.StagingDirectoryName);
            string newVersionPath = fileStore.GetIncomingVersionPath(packId, transactionId, journal.VersionId, journal.StagingDirectoryName);
            string archivedVersionPath = fileStore.GetIncomingVersionPath(packId, transactionId, journal.PreviousVersionIdToArchive, journal.StagingDirectoryName);
            string historyPath = fileStore.GetIncomingHistoryPath(packId, transactionId, journal.StagingDirectoryName);

            if (!fileStore.FileExists(manifestPath) || !fileStore.FileExists(newVersionPath) ||
                !fileStore.FileExists(archivedVersionPath) || !fileStore.FileExists(historyPath))
            {
                throw new InvalidOperationException("O staging do UPDATE está incompleto.");
            }

            var staged = new StagedUpdate
            {
                Persistence = serializer.DeserializePersistenceContract(fileStore.ReadTransactionText(manifestPath)),
                NewVersion = serializer.DeserializeVersionContract(fileStore.ReadTransactionText(newVersionPath)),
                ArchivedVersion = serializer.DeserializeVersionContract(fileStore.ReadTransactionText(archivedVersionPath)),
                History = serializer.DeserializeVersionHistoryContract(fileStore.ReadTransactionText(historyPath))
            };

            ValidateUpdateState(packId, staged.Persistence, staged.NewVersion, staged.ArchivedVersion, staged.History);
            ValidateIncomingReferences(packId, transactionId, journal, staged.History);
            if (!SameIdentifier(staged.NewVersion.VersionId, journal.VersionId) ||
                !SameIdentifier(staged.ArchivedVersion.VersionId, journal.PreviousVersionIdToArchive))
            {
                throw new InvalidOperationException("As versões do staging divergem do journal.");
            }

            return staged;
        }

        private PreviousUpdateState ReadPreviousUpdateState(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackJournalContract journal)
        {
            string manifestPath = fileStore.GetPreviousManifestPath(packId, transactionId);
            string historyPath = fileStore.GetPreviousHistoryPath(packId, transactionId);
            string versionPath = fileStore.GetPreviousVersionPath(packId, transactionId, journal.PreviousVersionIdToArchive);
            if (!fileStore.FileExists(manifestPath) || !fileStore.FileExists(historyPath) || !fileStore.FileExists(versionPath))
            {
                throw new InvalidOperationException("O snapshot anterior do UPDATE está incompleto.");
            }

            var previous = new PreviousUpdateState
            {
                Persistence = serializer.DeserializePersistenceContract(fileStore.ReadTransactionText(manifestPath)),
                History = serializer.DeserializeVersionHistoryContract(fileStore.ReadTransactionText(historyPath)),
                Version = serializer.DeserializeVersionContract(fileStore.ReadTransactionText(versionPath))
            };
            ValidatePreviousUpdateState(packId, journal.PreviousVersionIdToArchive, previous);
            return previous;
        }

        private void ValidatePreviousUpdateState(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier previousVersionId,
            PreviousUpdateState previous)
        {
            if (previous == null || previous.Persistence == null || previous.History == null || previous.Version == null ||
                previous.Persistence.Identity == null || previous.Persistence.Content == null ||
                !SameIdentifier(previous.Persistence.Identity.PackId, packId) ||
                !SameIdentifier(previous.Persistence.Content.VersionId, previousVersionId) ||
                !SameIdentifier(previous.Version.PackId, packId) ||
                !SameIdentifier(previous.Version.VersionId, previousVersionId) ||
                previous.Version.LifecycleStatus == KnowledgePackLifecycleStatus.Archived ||
                !previous.Version.IsCurrent || previous.History.CurrentVersion == null ||
                previous.History.ActiveVersion == null ||
                !SameIdentifier(previous.History.CurrentVersion.VersionId, previousVersionId) ||
                !SameIdentifier(previous.History.ActiveVersion.VersionId, previousVersionId))
            {
                throw new InvalidOperationException("O snapshot anterior do UPDATE é incoerente.");
            }
        }

        private bool FinalStateMatchesPrevious(
            KnowledgeIdentifier packId,
            PreviousUpdateState previous)
        {
            if (!fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                !fileStore.FileExists(fileStore.GetHistoryPath(packId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, previous.Version.VersionId)))
            {
                return false;
            }

            KnowledgePackPersistenceContract persistence = serializer.DeserializePersistenceContract(
                fileStore.ReadText(fileStore.GetManifestPath(packId)));
            KnowledgePackVersionHistoryContract history = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadText(fileStore.GetHistoryPath(packId)));
            KnowledgePackVersionContract version = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, previous.Version.VersionId)));

            return String.Equals(serializer.SerializePersistenceContract(previous.Persistence), serializer.SerializePersistenceContract(persistence), StringComparison.Ordinal) &&
                String.Equals(serializer.SerializeVersionHistoryContract(previous.History), serializer.SerializeVersionHistoryContract(history), StringComparison.Ordinal) &&
                String.Equals(serializer.SerializeVersionContract(previous.Version), serializer.SerializeVersionContract(version), StringComparison.Ordinal);
        }

        private void ValidateIncomingReferences(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgePackJournalContract journal,
            KnowledgePackVersionHistoryContract history)
        {
            if (history.PreviousVersions == null)
            {
                return;
            }

            foreach (KnowledgePackVersionContract version in history.PreviousVersions)
            {
                if (version == null || version.VersionId == null)
                {
                    throw new InvalidOperationException("O histórico staged possui referência inválida.");
                }

                bool staged = SameIdentifier(version.VersionId, journal.VersionId) ||
                    SameIdentifier(version.VersionId, journal.PreviousVersionIdToArchive);
                string path = staged
                    ? fileStore.GetIncomingVersionPath(packId, transactionId, version.VersionId, journal.StagingDirectoryName)
                    : fileStore.GetVersionPath(packId, version.VersionId);
                if (!fileStore.FileExists(path))
                {
                    throw new InvalidOperationException("O histórico staged referencia uma versão sem arquivo.");
                }
            }
        }

        private void ValidatePublishedUpdateState(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier newVersionId,
            KnowledgeIdentifier previousVersionId)
        {
            if (!fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                !fileStore.FileExists(fileStore.GetHistoryPath(packId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, newVersionId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, previousVersionId)))
            {
                throw new InvalidOperationException("O conjunto Committed do UPDATE está incompleto.");
            }

            KnowledgePackPersistenceContract persistence = serializer.DeserializePersistenceContract(
                fileStore.ReadText(fileStore.GetManifestPath(packId)));
            KnowledgePackVersionContract newVersion = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, newVersionId)));
            KnowledgePackVersionContract archivedVersion = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, previousVersionId)));
            KnowledgePackVersionHistoryContract history = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadText(fileStore.GetHistoryPath(packId)));

            ValidateUpdateState(packId, persistence, newVersion, archivedVersion, history);
            ValidateHistoryReferences(packId, history);
            if (!SameIdentifier(newVersion.VersionId, newVersionId) ||
                !SameIdentifier(archivedVersion.VersionId, previousVersionId))
            {
                throw new InvalidOperationException("O estado Committed diverge do journal.");
            }
        }

        private void ValidatePublishedUpdate(
            KnowledgeIdentifier packId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract newVersion,
            KnowledgePackVersionContract archivedVersion,
            KnowledgePackVersionHistoryContract history)
        {
            if (!fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                !fileStore.FileExists(fileStore.GetHistoryPath(packId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, newVersion.VersionId)) ||
                !fileStore.FileExists(fileStore.GetVersionPath(packId, archivedVersion.VersionId)))
            {
                throw new InvalidOperationException("O conjunto publicado do UPDATE está incompleto.");
            }

            KnowledgePackPersistenceContract publishedPersistence = serializer.DeserializePersistenceContract(
                fileStore.ReadText(fileStore.GetManifestPath(packId)));
            KnowledgePackVersionContract publishedNewVersion = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, newVersion.VersionId)));
            KnowledgePackVersionContract publishedArchivedVersion = serializer.DeserializeVersionContract(
                fileStore.ReadText(fileStore.GetVersionPath(packId, archivedVersion.VersionId)));
            KnowledgePackVersionHistoryContract publishedHistory = serializer.DeserializeVersionHistoryContract(
                fileStore.ReadText(fileStore.GetHistoryPath(packId)));

            ValidateUpdateState(packId, publishedPersistence, publishedNewVersion, publishedArchivedVersion, publishedHistory);
            ValidateHistoryReferences(packId, publishedHistory);
            if (!String.Equals(serializer.SerializePersistenceContract(persistence), serializer.SerializePersistenceContract(publishedPersistence), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(newVersion), serializer.SerializeVersionContract(publishedNewVersion), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionContract(archivedVersion), serializer.SerializeVersionContract(publishedArchivedVersion), StringComparison.Ordinal) ||
                !String.Equals(serializer.SerializeVersionHistoryContract(history), serializer.SerializeVersionHistoryContract(publishedHistory), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("O conjunto publicado do UPDATE diverge do estado esperado.");
            }
        }

        private static void ValidateUpdateOperation(KnowledgePackPersistenceOperationContract operation)
        {
            if (operation == null || operation.Persistence == null || operation.Persistence.Identity == null ||
                operation.Persistence.Content == null || operation.Version == null ||
                operation.Persistence.Identity.PackId == null || operation.Version.PackId == null ||
                operation.Version.VersionId == null || operation.Persistence.Content.VersionId == null ||
                operation.PreviousVersionIdToArchive == null)
            {
                throw new ArgumentException("A operação composta de atualização é inválida.", nameof(operation));
            }

            if (!SameIdentifier(operation.Persistence.Identity.PackId, operation.Version.PackId) ||
                !SameIdentifier(operation.Persistence.Content.VersionId, operation.Version.VersionId) ||
                SameIdentifier(operation.Version.VersionId, operation.PreviousVersionIdToArchive) ||
                operation.Version.LifecycleStatus != KnowledgePackLifecycleStatus.Active ||
                !operation.Version.IsCurrent ||
                String.IsNullOrWhiteSpace(operation.Version.VersionNumber))
            {
                throw new ArgumentException("A operação composta de atualização não possui uma versão coerente.", nameof(operation));
            }
        }

        private void ValidateCurrentUpdateState(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier previousVersionId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionHistoryContract history,
            KnowledgePackVersionContract version)
        {
            if (persistence == null || persistence.Identity == null || persistence.Content == null ||
                !SameIdentifier(persistence.Identity.PackId, packId) ||
                !SameIdentifier(persistence.Content.VersionId, previousVersionId) ||
                history == null || history.CurrentVersion == null || history.ActiveVersion == null ||
                !SameIdentifier(history.CurrentVersion.VersionId, previousVersionId) ||
                !SameIdentifier(history.ActiveVersion.VersionId, previousVersionId) ||
                version == null || !SameIdentifier(version.PackId, packId) ||
                !SameIdentifier(version.VersionId, previousVersionId) ||
                version.LifecycleStatus == KnowledgePackLifecycleStatus.Archived ||
                !version.IsCurrent)
            {
                throw new InvalidOperationException("O estado atual do Pack não permite UPDATE seguro.");
            }
        }

        private static KnowledgePackVersionContract ArchivedVersion(KnowledgePackVersionContract version)
        {
            return new KnowledgePackVersionContract
            {
                VersionId = version.VersionId,
                PackId = version.PackId,
                VersionNumber = version.VersionNumber,
                Revision = version.Revision,
                LifecycleStatus = KnowledgePackLifecycleStatus.Archived,
                CreatedAt = version.CreatedAt,
                PromotedAt = version.PromotedAt,
                ArchivedAt = DateTimeOffset.UtcNow,
                IsCurrent = false
            };
        }

        private static KnowledgePackVersionHistoryContract BuildUpdateHistory(
            KnowledgePackVersionHistoryContract currentHistory,
            KnowledgePackVersionContract newVersion,
            KnowledgePackVersionContract archivedVersion)
        {
            var previousVersions = currentHistory.PreviousVersions == null
                ? new List<KnowledgePackVersionContract>()
                : new List<KnowledgePackVersionContract>(currentHistory.PreviousVersions);
            if (!SameVersion(previousVersions, archivedVersion.VersionId))
            {
                previousVersions.Add(archivedVersion);
            }

            return new KnowledgePackVersionHistoryContract
            {
                CurrentVersion = newVersion,
                ActiveVersion = newVersion,
                ArchivedVersion = archivedVersion,
                PreviousVersions = previousVersions.AsReadOnly()
            };
        }

        private void ValidateUpdateState(
            KnowledgeIdentifier packId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract newVersion,
            KnowledgePackVersionContract archivedVersion,
            KnowledgePackVersionHistoryContract history)
        {
            if (persistence == null || persistence.Identity == null || persistence.Content == null ||
                !SameIdentifier(persistence.Identity.PackId, packId) ||
                !SameIdentifier(persistence.Content.VersionId, newVersion == null ? null : newVersion.VersionId) ||
                newVersion == null || archivedVersion == null ||
                !SameIdentifier(newVersion.PackId, packId) ||
                !SameIdentifier(archivedVersion.PackId, packId) ||
                SameIdentifier(newVersion.VersionId, archivedVersion.VersionId) ||
                newVersion.LifecycleStatus != KnowledgePackLifecycleStatus.Active ||
                !newVersion.IsCurrent ||
                archivedVersion.LifecycleStatus != KnowledgePackLifecycleStatus.Archived ||
                archivedVersion.IsCurrent ||
                history == null || history.CurrentVersion == null || history.ActiveVersion == null ||
                history.ArchivedVersion == null ||
                !SameIdentifier(history.CurrentVersion.VersionId, newVersion.VersionId) ||
                !SameIdentifier(history.ActiveVersion.VersionId, newVersion.VersionId) ||
                !SameIdentifier(history.ArchivedVersion.VersionId, archivedVersion.VersionId))
            {
                throw new InvalidOperationException("O estado final do UPDATE é incoerente.");
            }

        }

        private void ValidateHistoryReferences(
            KnowledgeIdentifier packId,
            KnowledgePackVersionHistoryContract history)
        {
            ValidateVersionFile(packId, history.CurrentVersion.VersionId);
            ValidateVersionFile(packId, history.ActiveVersion.VersionId);
            ValidateVersionFile(packId, history.ArchivedVersion.VersionId);
            if (history.PreviousVersions != null)
            {
                foreach (KnowledgePackVersionContract version in history.PreviousVersions)
                {
                    if (version != null)
                    {
                        ValidateVersionFile(packId, version.VersionId);
                    }
                }
            }
        }

        private void ValidateVersionFile(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            if (versionId == null || !fileStore.FileExists(fileStore.GetVersionPath(packId, versionId)))
            {
                throw new InvalidOperationException("O histórico referencia uma versão sem arquivo físico.");
            }
        }

        private bool PublishedFilesExist(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            return fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                fileStore.FileExists(fileStore.GetHistoryPath(packId)) ||
                fileStore.FileExists(fileStore.GetVersionPath(packId, versionId));
        }

        private static KnowledgePackRecoveryResult Recovered(KnowledgeIdentifier transactionId)
        {
            return new KnowledgePackRecoveryResult
            {
                Status = KnowledgePackRecoveryStatus.Recovered,
                TransactionId = transactionId
            };
        }

        private static KnowledgePackRecoveryResult Unrecoverable(
            KnowledgeIdentifier transactionId,
            string reason)
        {
            return new KnowledgePackRecoveryResult
            {
                Status = KnowledgePackRecoveryStatus.Unrecoverable,
                TransactionId = transactionId,
                FailureReason = reason
            };
        }

        private sealed class StagedCreate
        {
            public KnowledgePackPersistenceContract Persistence { get; set; }
            public KnowledgePackVersionContract Version { get; set; }
            public KnowledgePackVersionHistoryContract History { get; set; }
        }

        private sealed class StagedUpdate
        {
            public KnowledgePackPersistenceContract Persistence { get; set; }
            public KnowledgePackVersionContract NewVersion { get; set; }
            public KnowledgePackVersionContract ArchivedVersion { get; set; }
            public KnowledgePackVersionHistoryContract History { get; set; }
        }

        private sealed class PreviousUpdateState
        {
            public KnowledgePackPersistenceContract Persistence { get; set; }
            public KnowledgePackVersionHistoryContract History { get; set; }
            public KnowledgePackVersionContract Version { get; set; }
        }

        private static KnowledgePackVersionContract ActiveVersion(KnowledgePackVersionContract version)
        {
            return new KnowledgePackVersionContract
            {
                VersionId = version.VersionId,
                PackId = version.PackId,
                VersionNumber = version.VersionNumber,
                Revision = version.Revision,
                LifecycleStatus = KnowledgePackLifecycleStatus.Active,
                CreatedAt = version.CreatedAt,
                PromotedAt = version.PromotedAt,
                ArchivedAt = null,
                IsCurrent = true
            };
        }

        private static void ValidateCreateOperation(KnowledgePackPersistenceOperationContract operation)
        {
            if (operation == null || operation.Persistence == null || operation.Persistence.Identity == null ||
                operation.Persistence.Content == null || operation.Version == null ||
                operation.Persistence.Identity.PackId == null || operation.Version.PackId == null ||
                operation.Version.VersionId == null || operation.Persistence.Content.VersionId == null ||
                operation.PreviousVersionIdToArchive != null)
            {
                throw new ArgumentException("A operação composta de criação é inválida.", nameof(operation));
            }

            if (!SameIdentifier(operation.Persistence.Identity.PackId, operation.Version.PackId) ||
                !SameIdentifier(operation.Persistence.Content.VersionId, operation.Version.VersionId) ||
                operation.Version.LifecycleStatus != KnowledgePackLifecycleStatus.Active ||
                !operation.Version.IsCurrent ||
                String.IsNullOrWhiteSpace(operation.Version.VersionNumber))
            {
                throw new ArgumentException("A operação composta de criação não possui uma versão ativa coerente.", nameof(operation));
            }
        }

        private bool PublishedDataExists(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            return fileStore.FileExists(fileStore.GetManifestPath(packId)) ||
                fileStore.FileExists(fileStore.GetHistoryPath(packId)) ||
                fileStore.FileExists(fileStore.GetVersionPath(packId, versionId));
        }

        /// <summary>
        /// Revalidação física do UPDATE sob o PackLock: o estado persistido precisa
        /// apontar para PreviousVersionIdToArchive como current e active.
        /// A validação profunda (arquivo da versão, coerência completa) permanece em
        /// ValidateCurrentUpdateState, executada por UpdateComposedCore.
        /// </summary>
        private bool PersistedStateMatchesPreviousVersion(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier previousVersionId)
        {
            if (previousVersionId == null)
            {
                return false;
            }

            KnowledgePackPersistenceContract persistence = Get(packId);
            if (persistence == null || persistence.Content == null || persistence.Content.VersionId == null ||
                !SameIdentifier(persistence.Content.VersionId, previousVersionId))
            {
                return false;
            }

            KnowledgePackVersionHistoryContract history = GetVersionHistory(packId);
            return history != null &&
                history.CurrentVersion != null &&
                history.ActiveVersion != null &&
                SameIdentifier(history.CurrentVersion.VersionId, previousVersionId) &&
                SameIdentifier(history.ActiveVersion.VersionId, previousVersionId);
        }

        private static void ValidateCreateState(
            KnowledgeIdentifier packId,
            KnowledgePackPersistenceContract persistence,
            KnowledgePackVersionContract version,
            KnowledgePackVersionHistoryContract history)
        {
            if (persistence == null || persistence.Identity == null || persistence.Content == null ||
                version == null || history == null || persistence.Identity.PackId == null ||
                persistence.Content.VersionId == null || version.PackId == null || version.VersionId == null ||
                !SameIdentifier(persistence.Identity.PackId, packId) ||
                !SameIdentifier(version.PackId, packId) ||
                !SameIdentifier(persistence.Content.VersionId, version.VersionId) ||
                !SameIdentifier(history.CurrentVersion == null ? null : history.CurrentVersion.VersionId, version.VersionId) ||
                !SameIdentifier(history.ActiveVersion == null ? null : history.ActiveVersion.VersionId, version.VersionId) ||
                version.LifecycleStatus != KnowledgePackLifecycleStatus.Active ||
                !version.IsCurrent)
            {
                throw new InvalidOperationException("O estado persistido do CREATE é incoerente.");
            }
        }

        private static bool SameIdentifier(KnowledgeIdentifier first, KnowledgeIdentifier second)
        {
            return first != null && second != null &&
                String.Equals(first.Value, second.Value, StringComparison.Ordinal);
        }

        private KnowledgePackVersionContract ReadVersion(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            string path = fileStore.GetVersionPath(packId, versionId);
            if (!fileStore.FileExists(path))
            {
                throw new FileNotFoundException("A versão do Knowledge Pack não foi encontrada.", path);
            }

            return serializer.DeserializeVersionContract(fileStore.ReadText(path));
        }

        private static void AddVersion(List<KnowledgePackVersionContract> versions, KnowledgePackVersionContract version)
        {
            if (version == null || SameVersion(versions, version.VersionId))
            {
                return;
            }

            versions.Add(version);
        }

        private static bool SameVersion(List<KnowledgePackVersionContract> versions, KnowledgeIdentifier versionId)
        {
            foreach (KnowledgePackVersionContract version in versions)
            {
                if (SameVersion(version, versionId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameVersion(KnowledgePackVersionContract version, KnowledgeIdentifier versionId)
        {
            return version != null && version.VersionId != null && versionId != null &&
                String.Equals(version.VersionId.Value, versionId.Value, StringComparison.Ordinal);
        }
    }
}
