using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Quality
{
    public sealed class QualityAssessment
    {
        public string SessionId { get; set; }
        public KnowledgeStatus Status { get; set; }
        public KnowledgeConfidence ConsolidatedConfidence { get; set; }
        public bool CoverageSufficient { get; set; }
        public int DiscoveryCount { get; set; }
        public int EvidenceCount { get; set; }
        public int RelationCount { get; set; }
        public int ConflictCount { get; set; }
        public int KnowledgeGapCount { get; set; }
        public string Recommendation { get; set; }
        public IReadOnlyList<string> Limitations { get; set; }
        public KnowledgeError Error { get; set; }
    }
}
