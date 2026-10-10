using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Session;

namespace Iazeitor.KnowledgeEngine.Compatibility.Mappers
{
    internal static class SessionMapper
    {
        internal static SessionContract ToContract(KnowledgeSession source)
        {
            if (source == null) return null;
            return new SessionContract
            {
                Identifier = source.Identifier,
                Status = source.Status,
                Demand = source.Demand,
                Context = source.Context,
                StartedAt = new DateTimeOffset(source.StartedAtUtc),
                EndedAt = source.EndedAtUtc.HasValue ? new DateTimeOffset(source.EndedAtUtc.Value) : (DateTimeOffset?)null,
                KnowledgePackIds = Ids(source.KnowledgePackIds),
                Metadata = source.Metadata,
                AccumulatedConfidence = source.AccumulatedConfidence,
                Discoveries = source.Discoveries == null ? new List<DiscoveryContract>().AsReadOnly() : source.Discoveries.Where(item => item != null).Select(DiscoveryMapper.ToContract).ToList().AsReadOnly(),
                Evidences = source.Evidences == null ? new List<EvidenceContract>().AsReadOnly() : source.Evidences.Where(item => item != null).Select(EvidenceMapper.ToContract).ToList().AsReadOnly(),
                Relations = source.Relations == null ? new List<RelationContract>().AsReadOnly() : source.Relations.Where(item => item != null).Select(RelationMapper.ToContract).ToList().AsReadOnly(),
                Conflicts = source.Conflicts == null ? new List<ConflictContract>().AsReadOnly() : source.Conflicts.Where(item => item != null).Select(ConflictMapper.ToContract).ToList().AsReadOnly(),
                KnowledgeGaps = source.KnowledgeGaps == null ? new List<GapContract>().AsReadOnly() : source.KnowledgeGaps.Where(item => item != null).Select(GapMapper.ToContract).ToList().AsReadOnly(),
                Updates = source.Updates == null ? new List<UpdateContract>().AsReadOnly() : source.Updates.Where(item => item != null).Select(UpdateMapper.ToContract).ToList().AsReadOnly()
            };
        }

        internal static KnowledgeSession ToDomain(SessionContract source)
        {
            if (source == null) return null;
            return new KnowledgeSession
            {
                Identifier = source.Identifier,
                Status = source.Status,
                Demand = source.Demand,
                Context = source.Context,
                StartedAtUtc = source.StartedAt.UtcDateTime,
                EndedAtUtc = source.EndedAt.HasValue ? source.EndedAt.Value.UtcDateTime : (DateTime?)null,
                KnowledgePackIds = Ids(source.KnowledgePackIds),
                Metadata = source.Metadata,
                AccumulatedConfidence = source.AccumulatedConfidence,
                Discoveries = source.Discoveries == null ? new List<Domain.Discovery.KnowledgeDiscovery>().AsReadOnly() : source.Discoveries.Where(item => item != null).Select(DiscoveryMapper.ToDomain).ToList().AsReadOnly(),
                Evidences = source.Evidences == null ? new List<Domain.Discovery.KnowledgeEvidence>().AsReadOnly() : source.Evidences.Where(item => item != null).Select(EvidenceMapper.ToDomain).ToList().AsReadOnly(),
                Relations = source.Relations == null ? new List<Domain.Conflicts.KnowledgeRelation>().AsReadOnly() : source.Relations.Where(item => item != null).Select(RelationMapper.ToDomain).ToList().AsReadOnly(),
                Conflicts = source.Conflicts == null ? new List<Domain.Conflicts.KnowledgeConflict>().AsReadOnly() : source.Conflicts.Where(item => item != null).Select(ConflictMapper.ToDomain).ToList().AsReadOnly(),
                KnowledgeGaps = source.KnowledgeGaps == null ? new List<Domain.Gaps.KnowledgeGap>().AsReadOnly() : source.KnowledgeGaps.Where(item => item != null).Select(GapMapper.ToDomain).ToList().AsReadOnly(),
                Updates = source.Updates == null ? new List<Domain.Updates.KnowledgeUpdate>().AsReadOnly() : source.Updates.Where(item => item != null).Select(UpdateMapper.ToDomain).ToList().AsReadOnly()
            };
        }

        internal static void CopyToContract(KnowledgeSession source, SessionContract target)
        {
            if (target == null) return;
            var mapped = ToContract(source);
            if (mapped == null) return;
            target.Identifier = mapped.Identifier;
            target.Status = mapped.Status;
            target.Demand = mapped.Demand;
            target.Context = mapped.Context;
            target.StartedAt = mapped.StartedAt;
            target.EndedAt = mapped.EndedAt;
            target.KnowledgePackIds = mapped.KnowledgePackIds;
            target.Metadata = mapped.Metadata;
            target.AccumulatedConfidence = mapped.AccumulatedConfidence;
            target.Discoveries = mapped.Discoveries;
            target.Evidences = mapped.Evidences;
            target.Relations = mapped.Relations;
            target.Conflicts = mapped.Conflicts;
            target.KnowledgeGaps = mapped.KnowledgeGaps;
            target.Updates = mapped.Updates;
        }

        private static IReadOnlyList<KnowledgeIdentifier> Ids(IReadOnlyList<KnowledgeIdentifier> ids)
        {
            return ids == null ? new List<KnowledgeIdentifier>().AsReadOnly() : ids.ToList().AsReadOnly();
        }
    }
}
