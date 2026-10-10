using System;
using System.Collections.Generic;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Gateway;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace GptBolDll
{
    internal static class KnowledgeGatewayResultToAdditionalContextConverter
    {
        internal static int MaxCharacters { get; set; } = 8000;
        internal static int MaxItemsPerCategory { get; set; } = 1;
        internal static Action<AdditionalContextBuildDiagnostics> DiagnosticSink { get; set; }

        internal static AdditionalContext Convert(KnowledgeGatewayResult result)
        {
            return Convert(result, null, null);
        }

        internal static AdditionalContext Convert(
            KnowledgeGatewayResult result,
            KnowledgeDemand demand)
        {
            return Convert(result, demand, null);
        }

        internal static AdditionalContext Convert(
            KnowledgeGatewayResult result,
            KnowledgeDemand demand,
            string originalProjectContext)
        {
            if (result == null || IsTerminalFailure(result.Status))
            {
                PublishDiagnostics(new ConversionMetrics());
                return null;
            }

            bool hasUsefulInformation =
                !string.IsNullOrWhiteSpace(result.Status) ||
                !string.IsNullOrWhiteSpace(result.PipelineStage) ||
                !string.IsNullOrWhiteSpace(result.Summary) ||
                !string.IsNullOrWhiteSpace(result.Limitations) ||
                result.RequiresAdditionalQuery ||
                result.RequiresReplanning ||
                result.HasConflicts ||
                result.HasKnowledgeGaps;

            if (!hasUsefulInformation)
            {
                PublishDiagnostics(new ConversionMetrics());
                return null;
            }

            var sections = new List<ContextSection>();
            var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var metrics = new ConversionMetrics();

            AddSection(sections, seenValues, itemCounts, metrics, "Status", result.Status, 6);
            if (result.Confidence != KnowledgeConfidence.Unknown)
                AddSection(sections, seenValues, itemCounts, metrics, "Confidence", result.Confidence.ToString(), 6);
            AddSection(sections, seenValues, itemCounts, metrics, "Pipeline", result.PipelineStage, 6);
            if (ShouldIncludeSummary(result.Summary, result.Limitations, demand, originalProjectContext))
                AddSection(sections, seenValues, itemCounts, metrics, "Summary", result.Summary, 4);
            else
            {
                metrics.OmittedSections++;
                if (IsAlreadyPresent(result.Summary, originalProjectContext))
                    metrics.DeduplicationRemovals++;
            }
            AddSection(sections, seenValues, itemCounts, metrics, "Limitations", result.Limitations, 3, true);
            if (result.HasConflicts)
                AddSection(sections, seenValues, itemCounts, metrics, "Conflicts", result.HasConflicts.ToString(), 1, true);
            if (result.HasKnowledgeGaps)
                AddSection(sections, seenValues, itemCounts, metrics, "Knowledge Gaps", result.HasKnowledgeGaps.ToString(), 2, true);
            if (result.RequiresAdditionalQuery)
                AddSection(sections, seenValues, itemCounts, metrics, "Additional Query", result.RequiresAdditionalQuery.ToString(), 5);
            if (result.RequiresReplanning)
                AddSection(sections, seenValues, itemCounts, metrics, "Replanning", result.RequiresReplanning.ToString(), 5);

            metrics.InitialSize = BuildContent(sections, false).Length;
            bool summarized = LimitSections(sections, metrics);
            metrics.TruncationOccurred = summarized;

            string content = BuildContent(sections, summarized);
            metrics.FinalSize = content.Length;
            metrics.EmittedSections = sections.Count;
            PublishDiagnostics(metrics);

            return new AdditionalContext
            {
                Content = content
            };
        }

        private static void PublishDiagnostics(ConversionMetrics metrics)
        {
            if (DiagnosticSink == null)
                return;

            var diagnostics = new AdditionalContextBuildDiagnostics
            {
                InitialSize = metrics.InitialSize,
                FinalSize = metrics.FinalSize,
                EmittedSections = metrics.EmittedSections,
                OmittedSections = metrics.OmittedSections,
                DeduplicationRemovals = metrics.DeduplicationRemovals,
                PriorityRemovals = metrics.PriorityRemovals,
                TruncationOccurred = metrics.TruncationOccurred
            };

            try
            {
                DiagnosticSink(diagnostics);
            }
            catch
            {
            }
        }

        private static bool ShouldIncludeSummary(
            string summary,
            string limitations,
            KnowledgeDemand demand,
            string originalProjectContext)
        {
            if (string.IsNullOrWhiteSpace(summary) || demand == null)
                return !IsAlreadyPresent(summary, originalProjectContext);

            if (IsAlreadyPresent(summary, originalProjectContext))
                return false;

            var terms = ExtractDemandTerms(demand);
            if (terms.Count == 0 || ContainsDemandTerm(summary, terms))
                return true;

            return !ContainsDemandTerm(limitations, terms);
        }

        private static bool IsAlreadyPresent(string value, string originalProjectContext)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(originalProjectContext))
                return false;

            string normalizedValue = NormalizeWhitespace(value);
            string normalizedContext = NormalizeWhitespace(originalProjectContext);
            return normalizedContext.IndexOf(normalizedValue, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(
                " ",
                value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static HashSet<string> ExtractDemandTerms(KnowledgeDemand demand)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDemandTerms(terms, demand.RequestId);
            AddDemandTerms(terms, demand.SessionId);
            AddDemandTerms(terms, demand.Domain);
            AddDemandTerms(terms, demand.Scope);
            AddDemandTerms(terms, demand.Objective);
            AddDemandTerms(terms, demand.Context);
            return terms;
        }

        private static void AddDemandTerms(ISet<string> terms, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (string term in value.Split(new[]
            {
                ' ', '\t', '\r', '\n', ',', '.', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']'
            }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (term.Length >= 3)
                    terms.Add(term);
            }
        }

        private static bool ContainsDemandTerm(string value, ISet<string> terms)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return terms.Any(term => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LimitSections(List<ContextSection> sections, ConversionMetrics metrics)
        {
            int maxCharacters = MaxCharacters > 0 ? MaxCharacters : 8000;
            bool summarized = false;

            while (BuildContent(sections, false).Length > maxCharacters)
            {
                ContextSection removable = sections
                    .Where(section => section.Priority > 3)
                    .OrderByDescending(section => section.Priority)
                    .ThenByDescending(section => sections.IndexOf(section))
                    .FirstOrDefault();

                if (removable == null)
                    break;

                sections.Remove(removable);
                metrics.PriorityRemovals++;
                metrics.OmittedSections++;
                summarized = true;
            }

            return summarized;
        }

        private static string BuildContent(IReadOnlyList<ContextSection> sections, bool summarized)
        {
            var contentSections = sections
                .Select(section => section.Name + ":" + Environment.NewLine + section.Value)
                .ToList();

            if (summarized)
                contentSections.Add("Resumo:" + Environment.NewLine + "Conteúdo resumido pelo limite configurado.");

            return string.Join(
                Environment.NewLine,
                new[]
                {
                    "========================================",
                    "KNOWLEDGE ENGINE",
                    string.Join(Environment.NewLine + Environment.NewLine, contentSections),
                    "========================================"
                });
        }

        private static bool IsTerminalFailure(string status)
        {
            return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddSection(
            ICollection<ContextSection> sections,
            ISet<string> seenValues,
            IDictionary<string, int> itemCounts,
            ConversionMetrics metrics,
            string name,
            string value,
            int priority,
            bool preserveDuplicate = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                metrics.OmittedSections++;
                return;
            }

            int maxItems = MaxItemsPerCategory > 0 ? MaxItemsPerCategory : 1;
            int currentCount;
            if (itemCounts.TryGetValue(name, out currentCount) && currentCount >= maxItems)
            {
                metrics.OmittedSections++;
                return;
            }

            string comparisonValue = value.Trim();
            if (!seenValues.Add(comparisonValue) && !preserveDuplicate)
            {
                metrics.OmittedSections++;
                metrics.DeduplicationRemovals++;
                return;
            }

            sections.Add(new ContextSection
            {
                Name = name,
                Value = value,
                Priority = priority
            });
            itemCounts[name] = currentCount + 1;
        }

        private sealed class ConversionMetrics
        {
            internal int InitialSize { get; set; }
            internal int FinalSize { get; set; }
            internal int EmittedSections { get; set; }
            internal int OmittedSections { get; set; }
            internal int DeduplicationRemovals { get; set; }
            internal int PriorityRemovals { get; set; }
            internal bool TruncationOccurred { get; set; }
        }

        private sealed class ContextSection
        {
            internal string Name { get; set; }
            internal string Value { get; set; }
            internal int Priority { get; set; }
        }
    }

    internal sealed class AdditionalContextBuildDiagnostics
    {
        internal int InitialSize { get; set; }
        internal int FinalSize { get; set; }
        internal int EmittedSections { get; set; }
        internal int OmittedSections { get; set; }
        internal int DeduplicationRemovals { get; set; }
        internal int PriorityRemovals { get; set; }
        internal bool TruncationOccurred { get; set; }
    }
}
