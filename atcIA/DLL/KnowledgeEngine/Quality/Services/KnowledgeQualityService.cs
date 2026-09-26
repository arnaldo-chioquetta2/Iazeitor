using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Quality;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Compatibility.Mappers;
using Iazeitor.KnowledgeEngine.Domain.Session;

namespace Iazeitor.KnowledgeEngine.Quality.Services
{
    public sealed class KnowledgeQualityService : IKnowledgeQualityService
    {
        public QualityAssessment Assess(SessionContract session)
        {
            return Assess(SessionMapper.ToDomain(session));
        }

        internal QualityAssessment Assess(KnowledgeSession session)
        {
            if (session == null)
            {
                return new QualityAssessment
                {
                    Status = KnowledgeStatus.Invalid,
                    ConsolidatedConfidence = KnowledgeConfidence.Unknown,
                    Recommendation = "Reject",
                    CoverageSufficient = false,
                    Limitations = new List<string> { "A KnowledgeSession não foi informada." }.AsReadOnly(),
                    Error = new KnowledgeError
                    {
                        Code = "INVALID_SESSION",
                        Message = "A KnowledgeSession não foi informada.",
                        Status = KnowledgeStatus.Invalid
                    }
                };
            }

            var discoveries = session.Discoveries == null
                ? new List<Domain.Discovery.KnowledgeDiscovery>()
                : session.Discoveries.Where(item => item != null).ToList();
            var evidences = session.Evidences == null
                ? new List<Domain.Discovery.KnowledgeEvidence>()
                : session.Evidences.Where(item => item != null).ToList();
            var relations = session.Relations == null
                ? new List<Domain.Conflicts.KnowledgeRelation>()
                : session.Relations.Where(item => item != null).ToList();
            var conflicts = session.Conflicts == null
                ? new List<Domain.Conflicts.KnowledgeConflict>()
                : session.Conflicts.Where(item => item != null).ToList();
            var gaps = session.KnowledgeGaps == null
                ? new List<Domain.Gaps.KnowledgeGap>()
                : session.KnowledgeGaps.Where(item => item != null).ToList();
            var limitations = new List<string>();
            var coverage = discoveries.Count > 0 && evidences.Count >= discoveries.Count &&
                relations.Count >= discoveries.Count && conflicts.Count == 0 && gaps.Count == 0;

            if (discoveries.Count == 0)
                limitations.Add("Nenhuma descoberta foi registrada.");
            if (evidences.Count < discoveries.Count)
                limitations.Add("A quantidade de evidências é inferior à quantidade de descobertas.");
            if (relations.Count < discoveries.Count)
                limitations.Add("Existem descobertas sem relação registrada.");
            if (conflicts.Count > 0)
                limitations.Add("Existem conflitos que exigem revisão.");
            if (gaps.Count > 0)
                limitations.Add("Existem lacunas de conhecimento pendentes.");

            var confidence = CalculateConfidence(discoveries, evidences, conflicts, gaps);
            var status = conflicts.Count > 0 ? KnowledgeStatus.Blocked
                : discoveries.Count == 0 ? KnowledgeStatus.Partial
                : coverage ? KnowledgeStatus.Completed : KnowledgeStatus.Partial;
            return new QualityAssessment
            {
                SessionId = session.Identifier == null ? null : session.Identifier.Value,
                Status = status,
                ConsolidatedConfidence = confidence,
                CoverageSufficient = coverage,
                DiscoveryCount = discoveries.Count,
                EvidenceCount = evidences.Count,
                RelationCount = relations.Count,
                ConflictCount = conflicts.Count,
                KnowledgeGapCount = gaps.Count,
                Recommendation = conflicts.Count > 0 ? "Reject"
                    : coverage && confidence == KnowledgeConfidence.High ? "Use"
                    : "Review",
                Limitations = limitations.AsReadOnly(),
                Error = null
            };
        }

        private static KnowledgeConfidence CalculateConfidence(
            IEnumerable<Domain.Discovery.KnowledgeDiscovery> discoveries,
            IEnumerable<Domain.Discovery.KnowledgeEvidence> evidences,
            IEnumerable<Domain.Conflicts.KnowledgeConflict> conflicts,
            IEnumerable<Domain.Gaps.KnowledgeGap> gaps)
        {
            var values = discoveries.Select(item => (int)item.Confidence)
                .Concat(evidences.Select(item => (int)item.Confidence))
                .Where(value => value > 0)
                .ToList();
            if (values.Count == 0)
                return KnowledgeConfidence.Unknown;
            if (conflicts.Any() || gaps.Any())
                return KnowledgeConfidence.Low;
            var average = values.Average();
            return average >= 2.5 ? KnowledgeConfidence.High
                : average >= 1.5 ? KnowledgeConfidence.Medium
                : KnowledgeConfidence.Low;
        }
    }
}
