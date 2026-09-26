using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Compatibility.Mappers;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Reading;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Application.Reading
{
    public sealed class KnowledgePackReader : IKnowledgePackReader
    {
        private readonly IKnowledgePackRepository repository;

        public KnowledgePackReader(IKnowledgePackRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            this.repository = repository;
        }

        public PackContract Read(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                return null;
            }

            try
            {
                return ReadCore(packId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public IReadOnlyList<PackContract> Read(IReadOnlyList<KnowledgeIdentifier> packIds)
        {
            return ReadWithStatus(packIds).Packs;
        }

        public KnowledgePackReadResult ReadWithStatus(
            IReadOnlyList<KnowledgeIdentifier> packIds)
        {
            var result = new List<PackContract>();
            bool unresolved = false;
            bool legacyUnsupported = false;
            if (packIds == null || packIds.Count == 0)
            {
                return new KnowledgePackReadResult
                {
                    Packs = result.AsReadOnly(),
                    HasUnresolvedPacks = false,
                    HasLegacyUnsupportedPacks = false
                };
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KnowledgeIdentifier packId in packIds)
            {
                if (packId == null || String.IsNullOrWhiteSpace(packId.Value))
                {
                    unresolved = true;
                    continue;
                }

                if (!seen.Add(packId.Value))
                {
                    continue;
                }

                try
                {
                    KnowledgePackReadSnapshot snapshot = repository.ReadComposed(packId);
                    if (snapshot == null)
                    {
                        unresolved = true;
                        continue;
                    }

                    if (snapshot.Status == KnowledgePackReadStatus.LegacyUnsupported)
                    {
                        // Formato legado reconhecido: nao e corrupcao, mas nao pode ser
                        // transformado em PackContract sem inventar Name/Status/References/Metadata.
                        legacyUnsupported = true;
                        continue;
                    }

                    if (snapshot.Status != KnowledgePackReadStatus.Ready)
                    {
                        unresolved = true;
                        continue;
                    }

                    PackContract pack = PackPersistenceRehydrator.ToContract(
                        snapshot.Persistence,
                        snapshot.ActiveVersion);
                    if (pack == null)
                    {
                        unresolved = true;
                        continue;
                    }

                    result.Add(pack);
                }
                catch (Exception)
                {
                    unresolved = true;
                }
            }

            return new KnowledgePackReadResult
            {
                Packs = result.AsReadOnly(),
                HasUnresolvedPacks = unresolved,
                HasLegacyUnsupportedPacks = legacyUnsupported
            };
        }

        private PackContract ReadCore(KnowledgeIdentifier packId)
        {
            // Leitura consistente coordenada: o Repository adquire o PackLock, recupera
            // transacao pendente quando necessario e devolve manifest + versao ativa
            // lidos na mesma regiao critica. O Reader nao conhece journal, staging,
            // previous, StorageLock nem FileStore.
            KnowledgePackReadSnapshot snapshot = repository.ReadComposed(packId);
            return ContractFromSnapshot(snapshot);
        }

        private static PackContract ContractFromSnapshot(KnowledgePackReadSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Status != KnowledgePackReadStatus.Ready)
            {
                return null;
            }

            return PackPersistenceRehydrator.ToContract(snapshot.Persistence, snapshot.ActiveVersion);
        }
    }
}
