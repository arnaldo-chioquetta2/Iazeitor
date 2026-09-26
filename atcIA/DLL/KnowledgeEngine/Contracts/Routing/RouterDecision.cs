using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Routing
{
    public sealed class RouterDecision
    {
        public KnowledgeStatus Status { get; set; }

        public string PrimaryDomain { get; set; }

        public IReadOnlyList<string> AuxiliaryDomains { get; set; }

        public IReadOnlyList<string> KnowledgePackIds { get; set; }

        public KnowledgeConfidence Confidence { get; set; }

        public bool RequiresProjectConsultation { get; set; }

        public bool RecommendsNewKnowledgePack { get; set; }

        public bool HasConflicts { get; set; }

        public IReadOnlyList<KnowledgeSource> Sources { get; set; }

        public IReadOnlyList<KnowledgeError> Errors { get; set; }
    }
}