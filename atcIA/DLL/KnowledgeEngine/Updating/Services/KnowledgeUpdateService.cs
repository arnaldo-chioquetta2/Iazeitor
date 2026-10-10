using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Quality;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Updating;
using Iazeitor.KnowledgeEngine.Compatibility.Mappers;
using Iazeitor.KnowledgeEngine.Domain.Discovery;
using Iazeitor.KnowledgeEngine.Domain.Knowledge;
using Iazeitor.KnowledgeEngine.Domain.Session;
using Iazeitor.KnowledgeEngine.Domain.Updates;

namespace Iazeitor.KnowledgeEngine.Updating.Services
{
    public sealed class KnowledgeUpdateService : IKnowledgeUpdateService
    {
        private readonly List<KnowledgePack> updatedPacks = new List<KnowledgePack>();
        private readonly List<KnowledgePack> previousPacks = new List<KnowledgePack>();
        private readonly IKnowledgePackRepository repository;
        private readonly IProjectKnowledgeBindingStore bindingStore;

        public KnowledgeUpdateService(
            IKnowledgePackRepository repository = null,
            IProjectKnowledgeBindingStore bindingStore = null)
        {
            this.repository = repository;
            this.bindingStore = bindingStore;
        }

        public IReadOnlyList<PackContract> UpdatedPacks { get { return updatedPacks.Select(PackMapper.ToContract).ToList().AsReadOnly(); } }

        public IReadOnlyList<PackContract> PreviousPacks { get { return previousPacks.Select(PackMapper.ToContract).ToList().AsReadOnly(); } }

        public KnowledgeUpdateResult Update(
            QualityAssessment assessment,
            SessionContract session,
            IReadOnlyList<PackContract> packs)
        {
            var domainPacks = packs == null
                ? new List<KnowledgePack>()
                : packs.Select(PackMapper.ToDomain).Where(item => item != null).ToList();
            KnowledgeSession domainSession = SessionMapper.ToDomain(session);
            KnowledgeUpdateResult result = Update(assessment, domainSession, domainPacks);
            SessionMapper.CopyToContract(domainSession, session);
            return result;
        }

        internal KnowledgeUpdateResult Update(
            QualityAssessment assessment,
            KnowledgeSession session,
            IReadOnlyList<KnowledgePack> packs)
        {
            var updateId = new KnowledgeIdentifier("knowledge-update-" + DateTime.UtcNow.Ticks);
            if (assessment == null || session == null)
                return Rejected(updateId, "A avaliação ou a sessão não foi informada.", "INVALID_UPDATE_INPUT");
            if (assessment.Status == KnowledgeStatus.Invalid || assessment.Status == KnowledgeStatus.Blocked ||
                String.Equals(assessment.Recommendation, "Reject", StringComparison.OrdinalIgnoreCase))
                return Rejected(updateId, "A avaliação rejeitou a atualização.", "UPDATE_REJECTED_BY_QUALITY");
            if (!String.Equals(assessment.Recommendation, "Use", StringComparison.OrdinalIgnoreCase) ||
                !assessment.CoverageSufficient)
                return Rejected(updateId, "A avaliação exige revisão antes da atualização.", "UPDATE_REQUIRES_REVIEW");
            if (session.Discoveries == null || session.Discoveries.Count == 0)
                return Rejected(updateId, "Não existem descobertas aprováveis para promover.", "NO_DISCOVERIES");

            var candidates = packs == null ? new List<KnowledgePack>() : packs.Where(item => item != null).ToList();
            var promotedIds = session.Discoveries.Where(item => item != null && item.Identifier != null)
                .Select(item => item.Identifier).ToList();
            var updatedIds = new List<string>();
            var createdIds = new List<string>();
            var previousVersions = new List<string>();
            var currentVersions = new List<string>();
            var history = session.Updates == null ? new List<KnowledgeUpdate>() : session.Updates.ToList();
            bool? associationPersisted = null;
            string associationFailureReason = null;

            if (candidates.Count == 0)
            {
                // Fail-closed: sem identidade de projeto confiavel nao existe PackId
                // project-scoped deterministico, e o antigo id global "knowledge-pack"
                // nao e mais produzido.
                if (session.Identifier == null || String.IsNullOrWhiteSpace(session.Identifier.Value))
                {
                    return Rejected(
                        updateId,
                        "A sessao nao possui identidade de projeto confiavel; a criacao de um novo Knowledge Pack foi impedida.",
                        "SESSION_WITHOUT_PROJECT");
                }

                var created = CreatePack(session, promotedIds);
                updatedPacks.Add(created);
                createdIds.Add(created.Identifier.Value);
                currentVersions.Add(VersionText(created.Version));
                history.Add(CreateHistory(updateId, created, null, promotedIds, "Novo Knowledge Pack criado a partir de descobertas aprovadas."));
                KnowledgePackComposedPersistenceResult createResult = PersistCreatedPack(created);
                bool createdPublished = createResult != null &&
                    (createResult.Status == KnowledgePackComposedPersistenceStatus.Persisted ||
                     createResult.Status == KnowledgePackComposedPersistenceStatus.SupersededByExistingState);

                if (createdPublished)
                {
                    // O PackId e derivado do projeto de forma deterministica; portanto um
                    // estado ja existente nesse id pertence ao mesmo projeto. Isso fecha a
                    // janela de crash entre persistir o Pack e gravar a associacao.
                    AssociatePackWithSession(session, created.Identifier);

                    if (bindingStore != null)
                    {
                        string failureReason;
                        bool associationOk = TryPersistAssociation(
                            session.Identifier,
                            created.Identifier,
                            out failureReason);
                        associationPersisted = associationOk;
                        associationFailureReason = failureReason;
                    }
                }
            }
            else
            {
                foreach (var pack in candidates)
                {
                    var previous = ClonePack(pack);
                    var updated = ClonePack(pack);
                    updated.Version = NextVersion(pack.Version);
                    updated.Status = KnowledgeStatus.Active;
                    updated.References = MergeReferences(pack.References, promotedIds);
                    previousPacks.Add(previous);
                    updatedPacks.Add(updated);
                    updatedIds.Add(updated.Identifier.Value);
                    previousVersions.Add(VersionText(previous.Version));
                    currentVersions.Add(VersionText(updated.Version));
                    history.Add(CreateHistory(updateId, updated, previous, promotedIds,
                        "Knowledge Pack atualizado com descobertas aprovadas."));
                    PersistUpdatedPack(updated, previous);
                }
            }

            session.Updates = history.AsReadOnly();
            return new KnowledgeUpdateResult
            {
                UpdateId = updateId.Value,
                Status = KnowledgeStatus.Completed,
                Confidence = assessment.ConsolidatedConfidence,
                UpdatedKnowledgePackIds = updatedIds.AsReadOnly(),
                CreatedKnowledgePackIds = createdIds.AsReadOnly(),
                PreviousVersionIds = previousVersions.AsReadOnly(),
                CurrentVersionIds = currentVersions.AsReadOnly(),
                Reason = "Atualização aplicada conforme avaliação de qualidade.",
                Source = new KnowledgeSource { Kind = "KnowledgeSession", Identifier = session.Identifier == null ? null : session.Identifier.Value, Location = "KnowledgeUpdateService" },
                Error = null,
                AssociationPersisted = associationPersisted,
                AssociationFailureReason = associationFailureReason
            };
        }

        private static KnowledgePack CreatePack(KnowledgeSession session, List<KnowledgeIdentifier> discoveries)
        {
            // PackId project-scoped: derivado da identidade estavel do projeto, nunca de
            // nome/path. Substitui o antigo id global "knowledge-pack".
            var id = new KnowledgeIdentifier(session.Identifier.Value + "-pack");
            return new KnowledgePack
            {
                Identifier = id,
                Name = session.Demand == null ? "Knowledge Pack" : session.Demand.Domain + " Knowledge Pack",
                Version = new KnowledgeVersion(1, 0, 0),
                Scope = new KnowledgeScope(session.Demand == null || String.IsNullOrWhiteSpace(session.Demand.Domain) ? "unknown" : session.Demand.Domain, "KnowledgeSession"),
                Status = KnowledgeStatus.Active,
                References = MergeReferences(new List<KnowledgeReference>(), discoveries),
                Metadata = null
            };
        }

        private KnowledgePackComposedPersistenceResult PersistCreatedPack(KnowledgePack pack)
        {
            if (repository == null || pack == null || pack.Identifier == null || pack.Version == null)
            {
                return null;
            }

            try
            {
                DateTimeOffset persistedAt = DateTimeOffset.UtcNow;
                KnowledgeIdentifier versionId = CreateVersionIdentifier(pack);
                KnowledgePackVersionContract version = new KnowledgePackVersionContract
                {
                    VersionId = versionId,
                    PackId = pack.Identifier,
                    VersionNumber = VersionText(pack.Version),
                    Revision = 0,
                    LifecycleStatus = KnowledgePackLifecycleStatus.Active,
                    CreatedAt = persistedAt,
                    PromotedAt = null,
                    ArchivedAt = null,
                    IsCurrent = true
                };

                KnowledgePackComposedPersistenceResult result = repository.PersistComposed(
                    new KnowledgePackPersistenceOperationContract
                    {
                        Persistence = PackPersistenceMapper.ToPersistence(
                            PackMapper.ToContract(pack),
                            versionId,
                            persistedAt),
                        Version = version,
                        PreviousVersionIdToArchive = null
                    });

                return result;
            }
            catch (Exception)
            {
                // A persistência é auxiliar nesta etapa; o Pack em memória permanece válido.
                return null;
            }
        }

        private void PersistUpdatedPack(KnowledgePack pack, KnowledgePack previous)
        {
            if (repository == null || pack == null || pack.Identifier == null || pack.Version == null)
            {
                return;
            }

            try
            {
                KnowledgeIdentifier previousVersionId = CreateVersionIdentifier(pack.Identifier, previous == null ? null : previous.Version);
                DateTimeOffset persistedAt = DateTimeOffset.UtcNow;
                KnowledgeIdentifier versionId = CreateVersionIdentifier(pack);
                KnowledgePackVersionContract version = new KnowledgePackVersionContract
                {
                    VersionId = versionId,
                    PackId = pack.Identifier,
                    VersionNumber = VersionText(pack.Version),
                    Revision = 0,
                    LifecycleStatus = KnowledgePackLifecycleStatus.Active,
                    CreatedAt = persistedAt,
                    PromotedAt = null,
                    ArchivedAt = null,
                    IsCurrent = true
                };

                repository.PersistComposed(new KnowledgePackPersistenceOperationContract
                {
                    Persistence = PackPersistenceMapper.ToPersistence(
                        PackMapper.ToContract(pack),
                        versionId,
                        persistedAt),
                    Version = version,
                    PreviousVersionIdToArchive = previousVersionId
                });
            }
            catch (Exception)
            {
                // A persistência é auxiliar nesta etapa; o Pack atualizado permanece em memória.
            }
        }

        private static void AssociatePackWithSession(
            KnowledgeSession session,
            KnowledgeIdentifier packId)
        {
            if (session == null || packId == null)
            {
                return;
            }

            var packIds = session.KnowledgePackIds == null
                ? new List<KnowledgeIdentifier>()
                : session.KnowledgePackIds.ToList();

            if (!packIds.Any(item => item != null &&
                String.Equals(item.Value, packId.Value, StringComparison.Ordinal)))
            {
                packIds.Add(packId);
                session.KnowledgePackIds = packIds.AsReadOnly();
            }
        }

        /// <summary>
        /// Grava a associacao persistente do Pack ao projeto. Nao adquire o PackLock do
        /// Repository (o binding tem lock fisico proprio) e nao inventa recuperacao:
        /// retorna false com motivo deterministico quando a evidencia deve ser preservada.
        /// </summary>
        private bool TryPersistAssociation(
            KnowledgeIdentifier projectIdentifier,
            KnowledgeIdentifier packId,
            out string failureReason)
        {
            failureReason = null;

            if (bindingStore == null ||
                projectIdentifier == null ||
                String.IsNullOrWhiteSpace(projectIdentifier.Value) ||
                packId == null)
            {
                failureReason = "A associacao persistente ao projeto nao pode ser gravada (entrada invalida).";
                return false;
            }

            try
            {
                if (bindingStore.AddAssociation(projectIdentifier.Value, packId))
                {
                    return true;
                }

                failureReason = "O Knowledge Pack foi persistido, mas a associacao persistente ao projeto nao pode ser gravada.";
                return false;
            }
            catch (KnowledgePackStorageLockTimeoutException exception)
            {
                failureReason = "O Knowledge Pack foi persistido, mas a associacao persistente ao projeto excedeu o timeout do lock de binding: " + exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                failureReason = "O Knowledge Pack foi persistido, mas a associacao persistente ao projeto falhou: " + exception.Message;
                return false;
            }
        }

        private void PersistPreviousVersionArchive(KnowledgeIdentifier packId, KnowledgePack previous)
        {
            if (previous == null || previous.Version == null)
            {
                return;
            }

            try
            {
                repository.Archive(packId, CreateVersionIdentifier(packId, previous.Version));
            }
            catch (Exception)
            {
                // A persistência é auxiliar nesta etapa; o estado em memória permanece válido.
            }
        }

        private static KnowledgeIdentifier CreateVersionIdentifier(KnowledgePack pack)
        {
            return new KnowledgeIdentifier(pack.Identifier.Value + "-version-" + VersionText(pack.Version));
        }

        private static KnowledgeIdentifier CreateVersionIdentifier(KnowledgeIdentifier packId, KnowledgeVersion version)
        {
            if (packId == null || version == null)
            {
                return null;
            }

            return new KnowledgeIdentifier(packId.Value + "-version-" + VersionText(version));
        }

        private static KnowledgePack ClonePack(KnowledgePack pack)
        {
            return new KnowledgePack
            {
                Identifier = pack.Identifier,
                Name = pack.Name,
                Version = pack.Version == null ? new KnowledgeVersion(1, 0, 0) : new KnowledgeVersion(pack.Version.Major, pack.Version.Minor, pack.Version.Patch),
                Scope = pack.Scope,
                Metadata = pack.Metadata,
                Status = pack.Status,
                References = pack.References == null ? new List<KnowledgeReference>().AsReadOnly() : pack.References.ToList().AsReadOnly()
            };
        }

        private static KnowledgeVersion NextVersion(KnowledgeVersion version)
        {
            return version == null ? new KnowledgeVersion(1, 0, 0) : new KnowledgeVersion(version.Major, version.Minor, version.Patch + 1);
        }

        private static IReadOnlyList<KnowledgeReference> MergeReferences(IReadOnlyList<KnowledgeReference> existing, List<KnowledgeIdentifier> discoveries)
        {
            var references = existing == null ? new List<KnowledgeReference>() : existing.ToList();
            foreach (var discovery in discoveries)
                if (!references.Any(item => item != null && item.Identifier != null && item.Identifier.Value == discovery.Value))
                    references.Add(new KnowledgeReference("KnowledgeDiscovery", discovery, "KnowledgeSession"));
            return references.AsReadOnly();
        }

        private static KnowledgeUpdate CreateHistory(KnowledgeIdentifier updateId, KnowledgePack current, KnowledgePack previous, List<KnowledgeIdentifier> discoveries, string reason)
        {
            return new KnowledgeUpdate
            {
                Identifier = updateId,
                KnowledgePackId = current.Identifier,
                PreviousVersion = previous == null ? null : previous.Version,
                CurrentVersion = current.Version,
                Status = KnowledgeStatus.Completed,
                Reason = reason,
                CreatedAtUtc = DateTime.UtcNow,
                PromotedDiscoveryIds = discoveries.AsReadOnly()
            };
        }

        private static string VersionText(KnowledgeVersion version)
        {
            return version == null ? null : version.Major + "." + version.Minor + "." + version.Patch;
        }

        private static KnowledgeUpdateResult Rejected(KnowledgeIdentifier id, string message, string code)
        {
            return new KnowledgeUpdateResult
            {
                UpdateId = id.Value,
                Status = KnowledgeStatus.Invalid,
                Confidence = KnowledgeConfidence.Unknown,
                UpdatedKnowledgePackIds = new List<string>().AsReadOnly(),
                CreatedKnowledgePackIds = new List<string>().AsReadOnly(),
                PreviousVersionIds = new List<string>().AsReadOnly(),
                CurrentVersionIds = new List<string>().AsReadOnly(),
                Reason = message,
                Error = new KnowledgeError { Code = code, Message = message, Status = KnowledgeStatus.Invalid }
            };
        }
    }
}
