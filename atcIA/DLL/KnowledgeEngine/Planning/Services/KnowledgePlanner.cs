using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Planning;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Planning.Services
{
    public sealed class KnowledgePlanner : IKnowledgePlanner
    {
        public KnowledgePlanner(IKnowledgeRouter router, IKnowledgeQueryService queryService)
        {
            Router = router;
            QueryService = queryService;
        }

        public IKnowledgeRouter Router { get; }
        public IKnowledgeQueryService QueryService { get; }

        public InvestigationResult CreatePlan(PlannerContract contract)
        {
            if (contract == null)
                return InvalidResult();

            var decision = contract.RouterDecision;
            if (decision == null && Router != null)
            {
                decision = Router.Route(new RouterContract
                {
                    Demand = contract.Demand,
                    MinimumConfidence = contract.Confidence,
                    Status = contract.Status
                });
            }

            QueryResult query = null;
            if (QueryService != null)
            {
                query = QueryService.Execute(new QueryContract
                {
                    Demand = contract.Demand,
                    SessionId = contract.SessionId,
                    MinimumConfidence = contract.Confidence
                });
            }

            var packs = Copy(decision == null ? null : decision.KnowledgePackIds);
            var domain = PrimaryDomain(contract, decision);
            var steps = new List<InvestigationStep>
            {
                new InvestigationStep
                {
                    StepId = "route",
                    Objective = "Confirmar a decisão de conhecimento.",
                    PrimaryDomain = domain,
                    KnowledgePackIds = packs,
                    Priority = 1,
                    DependsOnStepIds = new List<string>(),
                    Status = decision == null ? KnowledgeStatus.Invalid : KnowledgeStatus.Completed,
                    Limitation = decision == null ? "A decisão do Router não está disponível." : null
                }
            };

            var project = (decision != null && decision.RequiresProjectConsultation)
                || (query != null && query.RequiresProjectConsultation);
            var conflict = decision != null && decision.HasConflicts;
            var noKnowledge = decision == null || packs.Count == 0 || decision.RecommendsNewKnowledgePack;
            var limitations = new List<string>();
            var replan = conflict;

            if (conflict)
            {
                const string text = "Existem conflitos entre as fontes de conhecimento selecionadas.";
                limitations.Add(text);
                steps.Add(Step("conflict", "Resolver conflitos de conhecimento.", 2, domain, packs,
                    KnowledgeStatus.Blocked, false, true, text));
            }

            if (project)
            {
                var text = query == null || String.IsNullOrWhiteSpace(query.Limitation)
                    ? "A confirmação no projeto atual é necessária."
                    : query.Limitation;
                limitations.Add(text);
                steps.Add(Step("project", "Confirmar o conhecimento no projeto atual.", 3, domain, packs,
                    KnowledgeStatus.Pending, true, true, text));
                replan = true;
            }

            if (noKnowledge)
            {
                const string text = "Não há Knowledge Pack suficiente para concluir o plano.";
                limitations.Add(text);
                steps.Add(Step("gap", "Registrar a ausência de conhecimento disponível.", 4, domain, packs,
                    KnowledgeStatus.Blocked, false, true, text));
                replan = true;
            }

            var status = decision == null || decision.Status == KnowledgeStatus.Invalid
                ? KnowledgeStatus.Invalid
                : conflict ? KnowledgeStatus.Blocked
                : (project || noKnowledge) ? KnowledgeStatus.Partial
                : KnowledgeStatus.Active;
            var id = contract.Demand == null || String.IsNullOrWhiteSpace(contract.Demand.RequestId)
                ? "investigation-plan" : contract.Demand.RequestId;
            var plan = new InvestigationPlan
            {
                PlanId = id,
                Demand = contract.Demand,
                RouterDecision = decision,
                PrimaryDomain = domain,
                SessionId = contract.SessionId,
                KnowledgePackIds = packs,
                Steps = steps,
                Status = status,
                RequiresProjectConsultation = project,
                RequiresReplanning = replan,
                Limitations = limitations
            };

            var stepIds = new List<string>();
            foreach (var step in steps)
                stepIds.Add(step.StepId);
            return new InvestigationResult
            {
                InvestigationId = id,
                Demand = contract.Demand,
                Status = status,
                Summary = Summary(status, project, noKnowledge, steps.Count),
                Confidence = Confidence(contract, decision, query),
                Plan = plan,
                StepIds = stepIds,
                DiscoveryIds = new List<string>(),
                KnowledgeGapIds = new List<string>(),
                Sources = query != null && query.Sources != null ? query.Sources
                    : decision != null && decision.Sources != null ? decision.Sources
                    : new List<KnowledgeSource>(),
                Error = query == null ? null : query.Error
            };
        }

        private static InvestigationStep Step(string id, string objective, int priority, string domain,
            List<string> packs, KnowledgeStatus status, bool project, bool replan, string limitation)
        {
            return new InvestigationStep
            {
                StepId = id,
                Objective = objective,
                PrimaryDomain = domain,
                KnowledgePackIds = packs,
                Priority = priority,
                DependsOnStepIds = new List<string> { "route" },
                Status = status,
                RequiresProjectConsultation = project,
                RequiresReplanning = replan,
                Limitation = limitation
            };
        }

        private static InvestigationResult InvalidResult()
        {
            var plan = new InvestigationPlan
            {
                PlanId = "investigation-plan",
                Status = KnowledgeStatus.Invalid,
                Steps = new List<InvestigationStep>(),
                KnowledgePackIds = new List<string>(),
                Limitations = new List<string>()
            };
            return new InvestigationResult
            {
                InvestigationId = plan.PlanId,
                Status = KnowledgeStatus.Invalid,
                Summary = "O contrato do Planner não foi informado.",
                Confidence = KnowledgeConfidence.Unknown,
                Plan = plan,
                StepIds = new List<string>(),
                DiscoveryIds = new List<string>(),
                KnowledgeGapIds = new List<string>(),
                Sources = new List<KnowledgeSource>(),
                Error = new KnowledgeError { Code = "INVALID_PLANNER_CONTRACT", Message = "O contrato do Planner não foi informado." }
            };
        }

        private static string PrimaryDomain(PlannerContract contract, RouterDecision decision)
        {
            if (decision != null && !String.IsNullOrWhiteSpace(decision.PrimaryDomain))
                return decision.PrimaryDomain;
            return contract.Demand == null ? null : contract.Demand.Domain;
        }

        private static KnowledgeConfidence Confidence(PlannerContract contract, RouterDecision decision, QueryResult query)
        {
            if (query != null && query.Confidence != KnowledgeConfidence.Unknown)
                return query.Confidence;
            if (decision != null && decision.Confidence != KnowledgeConfidence.Unknown)
                return decision.Confidence;
            return contract.Confidence;
        }

        private static List<string> Copy(IReadOnlyList<string> values)
        {
            return values == null ? new List<string>() : new List<string>(values);
        }

        private static string Summary(KnowledgeStatus status, bool project, bool noKnowledge, int count)
        {
            if (status == KnowledgeStatus.Invalid) return "O plano de investigação é inválido.";
            if (status == KnowledgeStatus.Blocked) return "O plano de investigação está bloqueado por conflitos.";
            if (noKnowledge) return "O plano foi criado parcialmente por ausência de conhecimento suficiente.";
            if (project) return "O plano foi criado e requer confirmação no projeto atual.";
            return "Plano de investigação criado com " + count + " etapas.";
        }
    }
}
