using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Acquisition;
using Iazeitor.KnowledgeEngine.Contracts.Gateway;
using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Public;
using Iazeitor.KnowledgeEngine.Contracts.Quality;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Reading;
using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Contracts.Updating;

namespace Iazeitor.KnowledgeEngine.Application.Gateway
{
    public sealed class KnowledgeGateway : IKnowledgeGateway
    {
        private readonly IKnowledgeQueryService queryService;
        private readonly IKnowledgeRouter router;
        private readonly IKnowledgePlanner planner;
        private readonly IKnowledgeAcquisitionService acquisitionService;
        private readonly IKnowledgeQualityService qualityService;
        private readonly IKnowledgeUpdateService updateService;
        private readonly IKnowledgePackReader packReader;

        public KnowledgeGateway(
            IKnowledgeQueryService queryService,
            IKnowledgeRouter router,
            IKnowledgePlanner planner,
            IKnowledgeAcquisitionService acquisitionService,
            IKnowledgeQualityService qualityService,
            IKnowledgeUpdateService updateService,
            IKnowledgePackReader packReader)
        {
            this.queryService = queryService;
            this.router = router;
            this.planner = planner;
            this.acquisitionService = acquisitionService;
            this.qualityService = qualityService;
            this.updateService = updateService;
            this.packReader = packReader;
        }

        public KnowledgeGatewayResult Execute(KnowledgeDemand demand, SessionContract session, QueryContract query)
        {
            var result = new KnowledgeGatewayResult
            {
                OperationId = query == null || query.QueryId == null ? demand == null ? null : demand.RequestId : query.QueryId.Value,
                Session = session,
                Status = "Failed",
                Confidence = KnowledgeConfidence.Unknown,
                PipelineStage = "Received"
            };

            var queryResult = queryService.Execute(
                query ?? new QueryContract { Demand = demand },
                session);
            result.QueryResult = queryResult;
            result.Confidence = queryResult == null ? KnowledgeConfidence.Unknown : queryResult.Confidence;
            result.RequiresAdditionalQuery = queryResult != null && queryResult.RequiresProjectConsultation;
            result.Limitations = queryResult == null ? null : queryResult.Limitation;
            result.PipelineStage = "Query";

            if (queryResult != null && queryResult.Status == KnowledgeStatus.Completed && !queryResult.RequiresProjectConsultation)
            {
                result.Status = "Success";
                result.Summary = "A consulta foi concluída com conhecimento suficiente.";
                return result;
            }

            var routerDecision = router.Route(new RouterContract
            {
                Demand = demand,
                Scope = query == null ? null : query.Scope,
                MinimumConfidence = query == null ? KnowledgeConfidence.Unknown : query.MinimumConfidence,
                Status = KnowledgeStatus.Unknown
            });
            result.RouterDecision = routerDecision;
            result.Confidence = routerDecision == null ? result.Confidence : routerDecision.Confidence;
            result.HasConflicts = routerDecision != null && routerDecision.HasConflicts;
            result.RequiresAdditionalQuery = result.RequiresAdditionalQuery || routerDecision != null && routerDecision.RequiresProjectConsultation;
            result.PipelineStage = "Routing";

            if (routerDecision == null || routerDecision.Status == KnowledgeStatus.Blocked || routerDecision.Status == KnowledgeStatus.Invalid)
            {
                result.Status = routerDecision != null && routerDecision.Status == KnowledgeStatus.Blocked ? "Blocked" : "Failed";
                result.Summary = "O roteamento não permitiu a continuidade da investigação.";
                return result;
            }

            var planResult = planner.CreatePlan(new PlannerContract
            {
                Demand = demand,
                RouterDecision = routerDecision,
                SessionId = session == null ? null : session.Identifier,
                Confidence = result.Confidence,
                Status = routerDecision.Status
            });
            result.InvestigationResult = planResult;
            result.InvestigationPlan = planResult == null ? null : planResult.Plan;
            result.RequiresReplanning = planResult != null && planResult.Plan != null && planResult.Plan.RequiresReplanning;
            result.HasKnowledgeGaps = planResult != null && planResult.KnowledgeGapIds != null && planResult.KnowledgeGapIds.Count > 0;
            result.PipelineStage = "Planning";

            if (planResult == null || planResult.Plan == null || planResult.Plan.Status == KnowledgeStatus.Invalid)
            {
                result.Status = "Failed";
                result.Summary = "O planejamento não produziu um plano válido.";
                return result;
            }

            if (planResult.Plan.RequiresProjectConsultation || planResult.Plan.RequiresReplanning || planResult.Plan.Status == KnowledgeStatus.Partial)
            {
                result.Status = "Partial";
                result.RequiresAdditionalQuery = result.RequiresAdditionalQuery || planResult.Plan.RequiresProjectConsultation;
                result.Summary = "O plano requer execução ou decisão externa antes da aquisição.";
                return result;
            }

            var acquisitionResult = acquisitionService.Execute(
                new AcquisitionContract { Plan = planResult.Plan, SessionId = planResult.Plan.SessionId },
                session);
            result.InvestigationResult = acquisitionResult;
            result.Session = session;
            result.RequiresAdditionalQuery = acquisitionResult != null && acquisitionResult.RequiresAdditionalQuery;
            result.RequiresReplanning = acquisitionResult != null && acquisitionResult.RequiresReplanning;
            result.HasKnowledgeGaps = acquisitionResult != null && acquisitionResult.KnowledgeGapIds != null && acquisitionResult.KnowledgeGapIds.Count > 0;
            result.HasConflicts = result.HasConflicts || acquisitionResult != null && acquisitionResult.ConflictIds != null && acquisitionResult.ConflictIds.Count > 0;
            result.PipelineStage = "Acquisition";

            if (acquisitionResult == null || acquisitionResult.Interrupted || result.RequiresAdditionalQuery || result.RequiresReplanning)
            {
                result.Status = "Partial";
                result.Summary = "A aquisição foi preservada parcialmente.";
                return result;
            }

            var assessment = qualityService.Assess(session);
            result.QualityAssessment = assessment;
            result.Confidence = assessment == null ? result.Confidence : assessment.ConsolidatedConfidence;
            result.HasKnowledgeGaps = assessment != null && assessment.KnowledgeGapCount > 0;
            result.HasConflicts = result.HasConflicts || assessment != null && assessment.ConflictCount > 0;
            result.Limitations = assessment == null || assessment.Limitations == null ? result.Limitations : string.Join("; ", assessment.Limitations);
            result.PipelineStage = "Quality";

            if (assessment == null || !string.Equals(assessment.Recommendation, "Use", System.StringComparison.OrdinalIgnoreCase))
            {
                result.Status = assessment != null && assessment.Recommendation == "Reject" ? "Blocked" : "Partial";
                result.Summary = "A avaliação não autorizou a atualização.";
                return result;
            }

            IReadOnlyList<PackContract> existingPacks = new List<PackContract>().AsReadOnly();
            if (session != null && session.KnowledgePackIds != null && packReader != null)
            {
                try
                {
                    KnowledgePackReadResult readResult = packReader.ReadWithStatus(session.KnowledgePackIds);
                    existingPacks = readResult == null || readResult.Packs == null
                        ? existingPacks
                        : readResult.Packs;

                    // Pack legado reconhecido nao pode desaparecer silenciosamente da Session:
                    // sem reidratacao completa, nenhum UPDATE (total ou parcial) e executado e
                    // nunca caimos em CREATE sobre um PackId ja ocupado.
                    if (readResult != null && readResult.HasLegacyUnsupportedPacks)
                    {
                        result.Status = "Blocked";
                        result.PipelineStage = "Update";
                        result.Summary = "Existem Knowledge Packs legados nao suportados; a atualizacao foi impedida.";
                        result.Limitations = "Um ou mais Packs solicitados estao em formato legado e nao podem ser reidratados sem inventar dados.";
                        return result;
                    }

                    if (readResult == null ||
                        (readResult.HasUnresolvedPacks && existingPacks.Count == 0))
                    {
                        result.Status = "Blocked";
                        result.PipelineStage = "Update";
                        result.Summary = "NÃ£o foi possÃ­vel resolver os Knowledge Packs solicitados; a criaÃ§Ã£o foi impedida.";
                        result.Limitations = "A leitura dos Packs solicitados falhou ou produziu dados incompletos.";
                        return result;
                    }
                }
                catch (System.Exception)
                {
                    result.Status = "Blocked";
                    result.PipelineStage = "Update";
                    result.Summary = "A leitura dos Knowledge Packs solicitados falhou; a criaÃ§Ã£o foi impedida.";
                    result.Limitations = "Falha tÃ©cnica ao resolver os Packs da Session.";
                    return result;
                }
            }
            else if (session != null && session.KnowledgePackIds != null && packReader == null &&
                session.KnowledgePackIds.Count > 0)
            {
                result.Status = "Blocked";
                result.PipelineStage = "Update";
                result.Summary = "O leitor de Knowledge Packs nÃ£o estÃ¡ disponÃ­vel; a criaÃ§Ã£o foi impedida.";
                result.Limitations = "Leitor de Packs indisponÃ­vel para IDs solicitados.";
                return result;
            }

            var updateResult = updateService.Update(assessment, session, existingPacks);
            result.KnowledgeUpdateResult = updateResult;
            result.PipelineStage = "Update";
            result.Status = updateResult != null && updateResult.Status == KnowledgeStatus.Completed ? "Success" : "Failed";
            result.Summary = result.Status == "Success" ? "O pipeline do Knowledge Engine foi concluído." : "A atualização do conhecimento falhou.";
            if (result.Status == "Success" && updateResult != null && updateResult.AssociationPersisted == false)
            {
                result.Status = "Partial";
                result.Summary = "O Knowledge Pack foi persistido, mas a associacao persistente ao projeto falhou.";
                result.Limitations = updateResult.AssociationFailureReason;
            }

            return result;
        }
    }
}
