using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Query
{
    public sealed class QueryResult
    {
        public string QueryId { get; set; }

        public KnowledgeStatus Status { get; set; }

        public string Answer { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public IReadOnlyList<KnowledgeSource> Sources { get; set; }

        public IReadOnlyList<string> EvidenceIds { get; set; }

        public IReadOnlyList<string> ConflictIds { get; set; }

        public bool RequiresProjectConsultation { get; set; }

        public bool IsReusable { get; set; }

        public string Limitation { get; set; }

        public KnowledgeError Error { get; set; }
    }
}