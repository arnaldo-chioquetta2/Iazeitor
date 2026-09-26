using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Catalog;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Routing;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Routing.Services
{
    public sealed class KnowledgeRouter : IKnowledgeRouter
    {
        public KnowledgeRouter(
            IKnowledgeCatalog catalog,
            IKnowledgeQueryService queryService)
        {
            Catalog = catalog;
            QueryService = queryService;
        }

        public IKnowledgeCatalog Catalog { get; }

        public IKnowledgeQueryService QueryService { get; }

        public RouterDecision Route(RouterContract contract)
        {
            if (contract == null)
            {
                return CreateErrorDecision(
                    null,
                    "INVALID_ROUTER_CONTRACT",
                    "O contrato de roteamento não pode ser nulo.");
            }

            string primaryDomain = ResolvePrimaryDomain(contract);
            List<KnowledgeIdentifier> packIds = FindCandidatePackIds(contract, primaryDomain);
            QueryResult queryResult = QueryService == null
                ? null
                : QueryService.Execute(new QueryContract
                {
                    Demand = contract.Demand,
                    Scope = contract.Scope,
                    MinimumConfidence = contract.MinimumConfidence
                });

            bool hasConflicts = queryResult != null &&
                queryResult.ConflictIds != null &&
                queryResult.ConflictIds.Count > 0;

            KnowledgeStatus status = hasConflicts
                ? KnowledgeStatus.Blocked
                : packIds.Count > 0
                    ? KnowledgeStatus.Active
                    : KnowledgeStatus.Unknown;

            KnowledgeConfidence confidence = queryResult == null
                ? KnowledgeConfidence.Unknown
                : queryResult.Confidence;

            bool requiresProjectConsultation = queryResult != null
                ? queryResult.RequiresProjectConsultation
                : packIds.Count == 0;

            return new RouterDecision
            {
                Status = status,
                PrimaryDomain = primaryDomain,
                AuxiliaryDomains = new List<string>().AsReadOnly(),
                KnowledgePackIds = packIds
                    .Where(identifier => identifier != null)
                    .Select(identifier => identifier.Value)
                    .ToList()
                    .AsReadOnly(),
                Confidence = confidence,
                RequiresProjectConsultation = requiresProjectConsultation,
                RecommendsNewKnowledgePack = packIds.Count == 0,
                HasConflicts = hasConflicts,
                Sources = queryResult != null && queryResult.Sources != null && queryResult.Sources.Count > 0
                    ? queryResult.Sources
                    : CreatePackSources(packIds),
                Errors = CreateDecisionErrors(queryResult, packIds.Count == 0)
            };
        }

        private string ResolvePrimaryDomain(RouterContract contract)
        {
            if (contract.Demand != null && !string.IsNullOrWhiteSpace(contract.Demand.Domain))
            {
                return contract.Demand.Domain;
            }

            return contract.Scope == null ? null : contract.Scope.Domain;
        }

        private List<KnowledgeIdentifier> FindCandidatePackIds(
            RouterContract contract,
            string primaryDomain)
        {
            List<KnowledgeIdentifier> candidates = new List<KnowledgeIdentifier>();

            if (Catalog == null)
            {
                return candidates;
            }

            IReadOnlyList<KnowledgeIdentifier> located = contract.Scope != null
                ? Catalog.FindPackIdsByScope(contract.Scope)
                : !string.IsNullOrWhiteSpace(primaryDomain)
                    ? Catalog.FindPackIdsByDomain(primaryDomain)
                    : new List<KnowledgeIdentifier>().AsReadOnly();

            AddUnique(candidates, located);

            if (contract.PreferredKnowledgePackIds != null)
            {
                foreach (KnowledgeIdentifier preferredId in contract.PreferredKnowledgePackIds)
                {
                    if (preferredId == null || !Catalog.ContainsPack(preferredId))
                    {
                        continue;
                    }

                    if (contract.Scope != null &&
                        !ContainsIdentifier(Catalog.FindPackIdsByScope(contract.Scope), preferredId))
                    {
                        continue;
                    }

                    if (contract.Scope == null &&
                        !string.IsNullOrWhiteSpace(primaryDomain) &&
                        !ContainsIdentifier(Catalog.FindPackIdsByDomain(primaryDomain), preferredId))
                    {
                        continue;
                    }

                    AddUnique(candidates, new[] { preferredId });
                }
            }

            if (contract.Status != KnowledgeStatus.Unknown)
            {
                IReadOnlyList<KnowledgeIdentifier> statusIds = Catalog.FilterPackIdsByStatus(contract.Status);
                candidates = candidates
                    .Where(candidate => ContainsIdentifier(statusIds, candidate))
                    .ToList();
            }

            return candidates;
        }

        private static void AddUnique(
            List<KnowledgeIdentifier> target,
            IEnumerable<KnowledgeIdentifier> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (KnowledgeIdentifier identifier in source)
            {
                if (identifier != null && !ContainsIdentifier(target, identifier))
                {
                    target.Add(identifier);
                }
            }
        }

        private static bool ContainsIdentifier(
            IEnumerable<KnowledgeIdentifier> identifiers,
            KnowledgeIdentifier expected)
        {
            return expected != null && identifiers != null && identifiers.Any(identifier =>
                identifier != null &&
                string.Equals(identifier.Value, expected.Value, StringComparison.Ordinal));
        }

        private static IReadOnlyList<KnowledgeSource> CreatePackSources(
            IEnumerable<KnowledgeIdentifier> packIds)
        {
            return (packIds ?? new List<KnowledgeIdentifier>())
                .Where(identifier => identifier != null)
                .Select(identifier => new KnowledgeSource
                {
                    Kind = "KnowledgePack",
                    Identifier = identifier.Value,
                    Location = "KnowledgeCatalog",
                    Description = "Knowledge Pack selecionado pelo KnowledgeRouter."
                })
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<KnowledgeError> CreateDecisionErrors(
            QueryResult queryResult,
            bool noPacks)
        {
            if (queryResult != null && queryResult.Error != null)
            {
                return new List<KnowledgeError> { queryResult.Error }.AsReadOnly();
            }

            if (noPacks)
            {
                return new List<KnowledgeError>
                {
                    new KnowledgeError
                    {
                        Code = "KNOWLEDGE_BASE_NOT_FOUND",
                        Message = "Nenhum Knowledge Pack compatível foi localizado.",
                        Details = null,
                        Status = KnowledgeStatus.Unknown,
                        Source = null
                    }
                }.AsReadOnly();
            }

            return new List<KnowledgeError>().AsReadOnly();
        }

        private static RouterDecision CreateErrorDecision(
            string primaryDomain,
            string code,
            string message)
        {
            return new RouterDecision
            {
                Status = KnowledgeStatus.Invalid,
                PrimaryDomain = primaryDomain,
                AuxiliaryDomains = new List<string>().AsReadOnly(),
                KnowledgePackIds = new List<string>().AsReadOnly(),
                Confidence = KnowledgeConfidence.Unknown,
                RequiresProjectConsultation = true,
                RecommendsNewKnowledgePack = false,
                HasConflicts = false,
                Sources = new List<KnowledgeSource>().AsReadOnly(),
                Errors = new List<KnowledgeError>
                {
                    new KnowledgeError
                    {
                        Code = code,
                        Message = message,
                        Details = null,
                        Status = KnowledgeStatus.Invalid,
                        Source = null
                    }
                }.AsReadOnly()
            };
        }
    }
}