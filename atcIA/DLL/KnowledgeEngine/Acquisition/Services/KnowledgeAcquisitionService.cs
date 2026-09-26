using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Acquisition;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Compatibility.Mappers;
using Iazeitor.KnowledgeEngine.Domain.Discovery;
using Iazeitor.KnowledgeEngine.Domain.Conflicts;
using Iazeitor.KnowledgeEngine.Domain.Gaps;
using Iazeitor.KnowledgeEngine.Domain.Session;

namespace Iazeitor.KnowledgeEngine.Acquisition.Services
{
    public sealed class KnowledgeAcquisitionService : IKnowledgeAcquisitionService
    {
        public KnowledgeAcquisitionService(
            IKnowledgePlanner planner,
            IKnowledgeQueryService queryService)
        {
            Planner = planner;
            QueryService = queryService;
        }

        public IKnowledgePlanner Planner { get; }

        public IKnowledgeQueryService QueryService { get; }

        public InvestigationResult Execute(AcquisitionContract contract, SessionContract session)
        {
            var domainSession = SessionMapper.ToDomain(session);
            var result = Execute(contract, domainSession);
            SessionMapper.CopyToContract(domainSession, session);
            return result;
        }

        internal InvestigationResult Execute(AcquisitionContract contract, KnowledgeSession session)
        {
            if (contract == null || contract.Plan == null || session == null || contract.Plan.Steps == null)
                return null;

            var discoveries = session.Discoveries == null
                ? new List<KnowledgeDiscovery>()
                : session.Discoveries.ToList();
            var evidences = session.Evidences == null
                ? new List<KnowledgeEvidence>()
                : session.Evidences.ToList();
            var relations = session.Relations == null
                ? new List<KnowledgeRelation>()
                : session.Relations.ToList();
            var conflicts = session.Conflicts == null
                ? new List<KnowledgeConflict>()
                : session.Conflicts.ToList();
            var gaps = session.KnowledgeGaps == null
                ? new List<KnowledgeGap>()
                : session.KnowledgeGaps.ToList();
            var limitations = new List<string>();
            var stepIds = new List<string>();
            var discoveryIds = new List<string>();
            var evidenceIds = new List<string>();
            var relationIds = new List<string>();
            var conflictIds = new List<string>();
            var gapIds = new List<string>();
            var interrupted = false;
            var additionalQuery = false;
            var replan = contract.Plan.RequiresReplanning;

            foreach (var step in contract.Plan.Steps.OrderBy(item => item.Priority))
            {
                if (step == null)
                    continue;
                stepIds.Add(step.StepId);
                if (step.Status == KnowledgeStatus.Blocked || step.Status == KnowledgeStatus.Invalid ||
                    step.Status == KnowledgeStatus.Cancelled)
                {
                    interrupted = true;
                    replan = true;
                    if (!String.IsNullOrWhiteSpace(step.Limitation))
                        limitations.Add(step.Limitation);
                    continue;
                }

                if (step.RequiresProjectConsultation)
                {
                    interrupted = true;
                    additionalQuery = true;
                    replan = true;
                    limitations.Add(String.IsNullOrWhiteSpace(step.Limitation)
                        ? "A etapa requer consulta adicional ao projeto."
                        : step.Limitation);
                }

                var identifier = new KnowledgeIdentifier(step.StepId + "-discovery");
                var source = new KnowledgeReference("InvestigationStep", identifier, step.StepId);
                var now = DateTime.UtcNow;
                var scope = new KnowledgeScope(
                    String.IsNullOrWhiteSpace(step.PrimaryDomain) ? "unknown" : step.PrimaryDomain,
                    "InvestigationStep");
                var metadata = new KnowledgeMetadata(
                    identifier,
                    new KnowledgeVersion(1, 0, 0),
                    KnowledgeStatus.Active,
                    KnowledgeConfidence.Unknown,
                    source,
                    now,
                    null);

                if (!ContainsDiscovery(discoveries, identifier))
                    discoveries.Add(new KnowledgeDiscovery
                {
                    Identifier = identifier,
                    Content = step.Objective,
                    Classification = "observed",
                    Source = source,
                    Scope = scope,
                    Confidence = KnowledgeConfidence.Unknown,
                    Status = KnowledgeStatus.Active,
                    DiscoveredAtUtc = now,
                    RelatedReferences = new List<KnowledgeReference>().AsReadOnly(),
                    Metadata = metadata
                });
                var evidenceIdentifier = new KnowledgeIdentifier(step.StepId + "-evidence");
                if (!ContainsEvidence(evidences, evidenceIdentifier))
                    evidences.Add(new KnowledgeEvidence
                {
                    Identifier = evidenceIdentifier,
                    Source = source,
                    Type = "InvestigationStep",
                    Description = step.Objective,
                    Classification = "observed",
                    Confidence = KnowledgeConfidence.Unknown,
                    CollectedAtUtc = now,
                    RelatedReferences = new List<KnowledgeReference> { source }.AsReadOnly(),
                    Metadata = metadata
                });
                discoveryIds.Add(identifier.Value);
                evidenceIds.Add(evidenceIdentifier.Value);
                var relationIdentifier = new KnowledgeIdentifier(step.StepId + "-relation");
                if (!ContainsRelation(relations, relationIdentifier))
                    relations.Add(new KnowledgeRelation
                    {
                        Identifier = relationIdentifier,
                        FromIdentifier = identifier,
                        ToIdentifier = evidenceIdentifier,
                        Type = "supports",
                        Status = KnowledgeStatus.Active,
                        CreatedAtUtc = now,
                        References = new List<KnowledgeReference> { source }.AsReadOnly()
                    });
                relationIds.Add(relationIdentifier.Value);
            }

            if (contract.Plan.RouterDecision != null && contract.Plan.RouterDecision.HasConflicts)
            {
                var id = new KnowledgeIdentifier(contract.Plan.PlanId + "-conflict");
                if (!ContainsConflict(conflicts, id))
                    conflicts.Add(new KnowledgeConflict
                    {
                        Identifier = id,
                        Description = "Conflitos identificados pela decisão do Router.",
                        Status = KnowledgeStatus.Blocked,
                        Confidence = KnowledgeConfidence.Unknown,
                        IdentifiedAtUtc = DateTime.UtcNow,
                        RelatedReferences = new List<KnowledgeReference>().AsReadOnly()
                    });
                conflictIds.Add(id.Value);
                interrupted = true;
                replan = true;
            }

            foreach (var step in contract.Plan.Steps.Where(item => item != null && item.Status == KnowledgeStatus.Blocked))
            {
                var id = new KnowledgeIdentifier(step.StepId + "-gap");
                if (!ContainsGap(gaps, id))
                    gaps.Add(new KnowledgeGap
                    {
                        Identifier = id,
                        Description = String.IsNullOrWhiteSpace(step.Limitation)
                            ? step.Objective : step.Limitation,
                        Criticality = "high",
                        Status = KnowledgeStatus.Pending,
                        RelatedDomain = step.PrimaryDomain,
                        Scope = new KnowledgeScope(String.IsNullOrWhiteSpace(step.PrimaryDomain) ? "unknown" : step.PrimaryDomain, "InvestigationStep"),
                        Source = new KnowledgeReference("InvestigationStep", id, step.StepId),
                        IdentifiedAtUtc = DateTime.UtcNow,
                        RelatedReferences = new List<KnowledgeReference>().AsReadOnly(),
                        Metadata = null
                    });
                gapIds.Add(id.Value);
            }

            session.Discoveries = discoveries.AsReadOnly();
            session.Evidences = evidences.AsReadOnly();
            session.Relations = relations.AsReadOnly();
            session.Conflicts = conflicts.AsReadOnly();
            session.KnowledgeGaps = gaps.AsReadOnly();
            session.Status = interrupted ? KnowledgeStatus.Partial : KnowledgeStatus.Active;

            return new InvestigationResult
            {
                InvestigationId = contract.Plan.PlanId,
                Demand = contract.Plan.Demand,
                Status = interrupted ? KnowledgeStatus.Partial : KnowledgeStatus.Completed,
                Summary = interrupted ? "A aquisição foi preservada parcialmente e requer continuação."
                    : "A aquisição das etapas previstas foi concluída.",
                Confidence = KnowledgeConfidence.Unknown,
                Plan = contract.Plan,
                StepIds = stepIds,
                DiscoveryIds = discoveryIds,
                KnowledgeGapIds = gapIds,
                EvidenceIds = evidenceIds,
                RelationIds = relationIds,
                ConflictIds = conflictIds,
                Limitations = limitations,
                Interrupted = interrupted,
                RequiresAdditionalQuery = additionalQuery,
                RequiresReplanning = replan,
                Sources = new List<KnowledgeSource>().AsReadOnly(),
                Error = null
            };
        }

        private static bool ContainsDiscovery(List<KnowledgeDiscovery> items, KnowledgeIdentifier id)
        {
            return items.Any(item => item != null && item.Identifier != null && item.Identifier.Value == id.Value);
        }

        private static bool ContainsEvidence(List<KnowledgeEvidence> items, KnowledgeIdentifier id)
        {
            return items.Any(item => item != null && item.Identifier != null && item.Identifier.Value == id.Value);
        }

        private static bool ContainsRelation(List<KnowledgeRelation> items, KnowledgeIdentifier id)
        {
            return items.Any(item => item != null && item.Identifier != null && item.Identifier.Value == id.Value);
        }

        private static bool ContainsConflict(List<KnowledgeConflict> items, KnowledgeIdentifier id)
        {
            return items.Any(item => item != null && item.Identifier != null && item.Identifier.Value == id.Value);
        }

        private static bool ContainsGap(List<KnowledgeGap> items, KnowledgeIdentifier id)
        {
            return items.Any(item => item != null && item.Identifier != null && item.Identifier.Value == id.Value);
        }

    }
}
