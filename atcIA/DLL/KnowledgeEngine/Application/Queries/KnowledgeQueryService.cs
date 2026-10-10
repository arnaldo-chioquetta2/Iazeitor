using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Catalog;
using Iazeitor.KnowledgeEngine.Contracts.Query;
using Iazeitor.KnowledgeEngine.Contracts.Shared;
using Iazeitor.KnowledgeEngine.Domain.Session;

namespace Iazeitor.KnowledgeEngine.Application.Queries
{
    public sealed class KnowledgeQueryService : IKnowledgeQueryService
    {
        private readonly KnowledgeSession session;
        private readonly IKnowledgePackCatalog persistedCatalog;

        public KnowledgeQueryService(IKnowledgeCatalog catalog)
            : this(catalog, null, null)
        {
        }

        public KnowledgeQueryService(
            IKnowledgeCatalog catalog,
            KnowledgeSession session)
            : this(catalog, session, null)
        {
        }

        public KnowledgeQueryService(
            IKnowledgeCatalog catalog,
            IKnowledgePackCatalog persistedCatalog)
            : this(catalog, null, persistedCatalog)
        {
        }

        private KnowledgeQueryService(
            IKnowledgeCatalog catalog,
            KnowledgeSession session,
            IKnowledgePackCatalog persistedCatalog)
        {
            Catalog = catalog;
            this.session = session;
            this.persistedCatalog = persistedCatalog;
        }

        public IKnowledgeCatalog Catalog { get; }

        public QueryResult Execute(
            QueryContract query,
            Iazeitor.KnowledgeEngine.Contracts.Public.SessionContract operationSession)
        {
            IReadOnlyList<KnowledgeIdentifier> persistedPackIds = FindPersistedPackIds(
                query,
                operationSession == null ? null : operationSession.KnowledgePackIds,
                operationSession == null ? null : operationSession.Identifier);
            if (persistedPackIds.Count > 0)
            {
                string queryId = query == null || query.QueryId == null ? null : query.QueryId.Value;
                return CreateCatalogResult(queryId, persistedPackIds);
            }

            return Execute(query);
        }

        public QueryResult Execute(QueryContract query)
        {
            if (query == null)
            {
                return CreateErrorResult(
                    null,
                    "INVALID_QUERY",
                    "A consulta de conhecimento não pode ser nula.");
            }

            string queryId = query.QueryId == null ? null : query.QueryId.Value;

            if (HasSufficientSessionContext(query))
            {
                return CreateSessionResult(queryId);
            }

            IReadOnlyList<KnowledgeIdentifier> persistedPackIds = FindPersistedPackIds(query);
            if (persistedPackIds.Count > 0)
            {
                return CreateCatalogResult(queryId, persistedPackIds);
            }

            IReadOnlyList<KnowledgeIdentifier> packIds = FindCatalogPackIds(query);
            if (packIds.Count > 0)
            {
                return CreateCatalogResult(queryId, packIds);
            }

            if (Catalog == null)
            {
                return CreateErrorResult(
                    queryId,
                    "CATALOG_UNAVAILABLE",
                    "O catálogo de conhecimento não está disponível.");
            }

            return CreateErrorResult(
                queryId,
                "KNOWLEDGE_NOT_FOUND",
                "Nenhum conhecimento compatível foi encontrado.");
        }

        private IReadOnlyList<KnowledgeIdentifier> FindPersistedPackIds(QueryContract query)
        {
            if (persistedCatalog == null || session == null || session.KnowledgePackIds == null)
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            return FindPersistedPackIds(query, session.KnowledgePackIds, session.Identifier);
        }

        private IReadOnlyList<KnowledgeIdentifier> FindPersistedPackIds(
            QueryContract query,
            IReadOnlyList<KnowledgeIdentifier> sessionPackIds,
            KnowledgeIdentifier sessionId)
        {
            if (persistedCatalog == null || sessionPackIds == null)
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            if (query != null && query.SessionId != null && !HasSameIdentifier(sessionId, query.SessionId))
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            var packIds = new List<KnowledgeIdentifier>();
            foreach (KnowledgeIdentifier packId in sessionPackIds)
            {
                if (packId == null || persistedCatalog.Find(new KnowledgePackIdentityContract { PackId = packId }) == null)
                {
                    continue;
                }

                if (!packIds.Any(item => HasSameIdentifier(item, packId)))
                {
                    packIds.Add(packId);
                }
            }

            return packIds.AsReadOnly();
        }

        private bool HasSufficientSessionContext(QueryContract query)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.Context))
            {
                return false;
            }

            if (query.SessionId != null && !HasSameIdentifier(session.Identifier, query.SessionId))
            {
                return false;
            }

            return query.MinimumConfidence == KnowledgeConfidence.Unknown ||
                session.AccumulatedConfidence >= query.MinimumConfidence;
        }

        private QueryResult CreateSessionResult(string queryId)
        {
            return new QueryResult
            {
                QueryId = queryId,
                Status = KnowledgeStatus.Completed,
                Answer = session.Context,
                Confidence = session.AccumulatedConfidence,
                Sources = CreateSessionSources(),
                EvidenceIds = new List<string>().AsReadOnly(),
                ConflictIds = new List<string>().AsReadOnly(),
                RequiresProjectConsultation = false,
                IsReusable = true,
                Limitation = null,
                Error = null
            };
        }

        private IReadOnlyList<KnowledgeIdentifier> FindCatalogPackIds(QueryContract query)
        {
            if (Catalog == null)
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            if (query.Scope != null)
            {
                return Catalog.FindPackIdsByScope(query.Scope);
            }

            if (query.Demand != null && !string.IsNullOrWhiteSpace(query.Demand.Domain))
            {
                return Catalog.FindPackIdsByDomain(query.Demand.Domain);
            }

            if (session != null && session.KnowledgePackIds != null)
            {
                return session.KnowledgePackIds
                    .Where(packId => packId != null && Catalog.ContainsPack(packId))
                    .ToList()
                    .AsReadOnly();
            }

            return new List<KnowledgeIdentifier>().AsReadOnly();
        }

        private QueryResult CreateCatalogResult(
            string queryId,
            IReadOnlyList<KnowledgeIdentifier> packIds)
        {
            return new QueryResult
            {
                QueryId = queryId,
                Status = KnowledgeStatus.Partial,
                Answer = null,
                Confidence = KnowledgeConfidence.Unknown,
                Sources = CreatePackSources(packIds),
                EvidenceIds = new List<string>().AsReadOnly(),
                ConflictIds = new List<string>().AsReadOnly(),
                RequiresProjectConsultation = true,
                IsReusable = false,
                Limitation = "Foram localizados Knowledge Packs compatíveis, mas a confirmação do conteúdo ainda é necessária.",
                Error = null
            };
        }

        private QueryResult CreateErrorResult(
            string queryId,
            string code,
            string message)
        {
            return new QueryResult
            {
                QueryId = queryId,
                Status = KnowledgeStatus.Unknown,
                Answer = null,
                Confidence = KnowledgeConfidence.Unknown,
                Sources = new List<KnowledgeSource>().AsReadOnly(),
                EvidenceIds = new List<string>().AsReadOnly(),
                ConflictIds = new List<string>().AsReadOnly(),
                RequiresProjectConsultation = true,
                IsReusable = false,
                Limitation = message,
                Error = new KnowledgeError
                {
                    Code = code,
                    Message = message,
                    Status = KnowledgeStatus.Unknown,
                    Source = null,
                    Details = null
                }
            };
        }

        private IReadOnlyList<KnowledgeSource> CreateSessionSources()
        {
            return new List<KnowledgeSource>
            {
                new KnowledgeSource
                {
                    Kind = "KnowledgeSession",
                    Identifier = session.Identifier == null ? null : session.Identifier.Value,
                    Location = "KnowledgeSession",
                    Description = "Contexto temporário da KnowledgeSession."
                }
            }.AsReadOnly();
        }

        private IReadOnlyList<KnowledgeSource> CreatePackSources(
            IReadOnlyList<KnowledgeIdentifier> packIds)
        {
            return packIds
                .Where(packId => packId != null)
                .Select(packId => new KnowledgeSource
                {
                    Kind = "KnowledgePack",
                    Identifier = packId.Value,
                    Location = "KnowledgeCatalog",
                    Description = "Knowledge Pack localizado pelo KnowledgeCatalog."
                })
                .ToList()
                .AsReadOnly();
        }

        private static bool HasSameIdentifier(
            KnowledgeIdentifier first,
            KnowledgeIdentifier second)
        {
            return first != null &&
                second != null &&
                string.Equals(first.Value, second.Value, StringComparison.Ordinal);
        }
    }
}
