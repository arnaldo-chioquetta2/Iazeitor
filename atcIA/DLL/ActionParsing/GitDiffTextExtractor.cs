using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GptBolDll
{
    public sealed class GitDiffTextExtractionResult
    {
        public bool JsonWrapperCandidateDetected { get; set; }
        public bool HasWrapperJson { get; set; }
        public bool ExtractedFromJsonWrapper { get; set; }
        public bool JsonWrapperIncompleteRecovered { get; set; }
        public bool PrefixDiscardedBeforeDiff { get; set; }
        public bool UsedFirstDiffFallback { get; set; }
        public bool DiffEscapedTextDetected { get; set; }
        public int FieldCount { get; set; }
        public int ExtractedBlockCount { get; set; }
        public List<string> JsonWrapperFieldsFound { get; } = new List<string>();
        public string EffectiveText { get; set; }
    }

    public static class GitDiffTextExtractor
    {
        public static GitDiffTextExtractionResult Extract(string rawResponse)
        {
            var result = new GitDiffTextExtractionResult
            {
                EffectiveText = string.Empty
            };

            if (string.IsNullOrWhiteSpace(rawResponse))
                return result;

            string text = rawResponse.Trim();
            string markdownStripped = StripMarkdownFence(text);
            if (!string.Equals(markdownStripped, text, StringComparison.Ordinal))
                text = markdownStripped.Trim();

            bool wrapperCandidate = LooksLikeJsonWrapperCandidate(text);
            if (wrapperCandidate)
            {
                result.JsonWrapperCandidateDetected = true;
                if (TryExtractGitDiffFromJsonWrapper(text, result, out string wrapperText))
                {
                    result.EffectiveText = wrapperText;
                    return result;
                }
            }

            JToken token;
            if (!TryParseJsonToken(text, out token) && !string.Equals(markdownStripped, text, StringComparison.Ordinal))
            {
                if (TryParseJsonToken(markdownStripped, out token))
                    text = markdownStripped;
            }

            if (token == null)
            {
                if (TryRecoverLooseDiffStrings(text, result, out _))
                    return result;

                if (LooksLikeGitDiff(text))
                {
                    result.UsedFirstDiffFallback = true;
                    result.EffectiveText = NormalizeEscapedGitDiffText(IsolateFromFirstDiff(text, result), result);
                    return result;
                }

                result.EffectiveText = text;
                return result;
            }

            result.HasWrapperJson = true;
            result.JsonWrapperCandidateDetected = true;

            var segments = new List<string>();
            int fields = 0;
            bool prefixDiscarded = false;
            ExtractDiffSegments(token, segments, ref fields, ref prefixDiscarded);
            result.PrefixDiscardedBeforeDiff = prefixDiscarded;

            result.FieldCount = fields;
            if (segments.Count > 0)
            {
                result.ExtractedFromJsonWrapper = true;
                result.EffectiveText = string.Join(Environment.NewLine + Environment.NewLine, segments.Where(s => !string.IsNullOrWhiteSpace(s)));
                return result;
            }

            if (TryRecoverLooseDiffStrings(text, result, out _))
                return result;

            if (LooksLikeGitDiff(text))
            {
                result.UsedFirstDiffFallback = true;
                result.EffectiveText = NormalizeEscapedGitDiffText(IsolateFromFirstDiff(text, result), result);
                return result;
            }

            result.EffectiveText = string.Empty;
            return result;
        }

        private static bool TryExtractGitDiffFromJsonWrapper(string text, GitDiffTextExtractionResult result, out string effectiveText)
        {
            effectiveText = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ContainsJsonEscapes(text))
                result.DiffEscapedTextDetected = true;

            string[] fieldNames =
            {
                "operacoes_diff",
                "gitdiff",
                "git_diff",
                "unified_diff",
                "diff",
                "diffs",
                "patches"
            };

            foreach (string field in fieldNames)
            {
                if (text.IndexOf(field, StringComparison.OrdinalIgnoreCase) >= 0 && !result.JsonWrapperFieldsFound.Contains(field))
                    result.JsonWrapperFieldsFound.Add(field);
            }

            if (result.JsonWrapperFieldsFound.Count > 0)
                result.HasWrapperJson = true;

            JToken token;
            if (TryParseJsonToken(text, out token))
            {
                var segments = new List<string>();
                int fields = 0;
                bool prefixDiscarded = false;
                ExtractDiffSegments(token, segments, ref fields, ref prefixDiscarded);
                if (segments.Count > 0)
                {
                    effectiveText = string.Join(Environment.NewLine + Environment.NewLine, segments.Where(s => !string.IsNullOrWhiteSpace(s)));
                    result.ExtractedFromJsonWrapper = true;
                    result.JsonWrapperIncompleteRecovered = false;
                    result.FieldCount = fields;
                    result.PrefixDiscardedBeforeDiff = prefixDiscarded;
                    result.ExtractedBlockCount = segments.Count;
                    return true;
                }
            }

            if (TryRecoverLooseDiffStrings(text, result, out bool recoveredText))
            {
                result.JsonWrapperIncompleteRecovered = recoveredText;
                effectiveText = result.EffectiveText;
                return !string.IsNullOrWhiteSpace(effectiveText);
            }

            return false;
        }

        private static void ExtractDiffSegments(JToken token, List<string> segments, ref int fieldCount, ref bool prefixDiscarded)
        {
            if (token == null)
                return;

            if (token.Type == JTokenType.String)
            {
                string text = token.Value<string>();
                if (LooksLikeGitDiff(text))
                {
                    var segmentResult = new GitDiffTextExtractionResult();
                    segments.Add(IsolateFromFirstDiff(text, segmentResult));
                    if (segmentResult.PrefixDiscardedBeforeDiff)
                        prefixDiscarded = true;
                    fieldCount++;
                }
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token.Children())
                    ExtractDiffSegments(item, segments, ref fieldCount, ref prefixDiscarded);
                return;
            }

            JObject obj = token as JObject;
            if (obj == null)
                return;

            foreach (JProperty property in obj.Properties())
            {
                if (property == null)
                    continue;

                if (property.Value == null)
                    continue;

                if (property.Value.Type == JTokenType.String)
                {
                    string text = property.Value.Value<string>();
                    if (LooksLikeGitDiff(text))
                    {
                        var segmentResult = new GitDiffTextExtractionResult();
                        segments.Add(IsolateFromFirstDiff(text, segmentResult));
                        if (segmentResult.PrefixDiscardedBeforeDiff)
                            prefixDiscarded = true;
                        fieldCount++;
                    }
                    continue;
                }

                if (property.Value.Type == JTokenType.Array)
                {
                    var nestedSegments = new List<string>();
                    int nestedCount = 0;
                    bool nestedPrefixDiscarded = false;
                    ExtractDiffSegments(property.Value, nestedSegments, ref nestedCount, ref nestedPrefixDiscarded);
                    if (nestedSegments.Count > 0)
                    {
                        segments.AddRange(nestedSegments);
                        fieldCount += nestedCount;
                        if (nestedPrefixDiscarded)
                            prefixDiscarded = true;
                    }
                    continue;
                }

                if (property.Value.Type == JTokenType.Object)
                    ExtractDiffSegments(property.Value, segments, ref fieldCount, ref prefixDiscarded);
            }
        }

        private static bool TryRecoverLooseDiffStrings(string text, GitDiffTextExtractionResult result, out bool recovered)
        {
            recovered = false;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var segments = new List<string>();
            var fields = new List<string>();

            foreach (string fieldName in new[] { "operacoes_diff", "gitdiff", "git_diff", "unified_diff", "diff", "diffs", "patches" })
            {
                if (text.IndexOf(fieldName, StringComparison.OrdinalIgnoreCase) >= 0 && !fields.Contains(fieldName))
                    fields.Add(fieldName);
            }

            if (fields.Count > 0)
            {
                result.HasWrapperJson = true;
                result.JsonWrapperCandidateDetected = true;
                foreach (string field in fields)
                    if (!result.JsonWrapperFieldsFound.Contains(field))
                        result.JsonWrapperFieldsFound.Add(field);
            }

            bool sawEscapedText = false;
            foreach (Match match in Regex.Matches(text, "\"(?<value>(?:\\\\.|[^\\\"])*)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (!match.Success)
                    continue;

                string rawValue = match.Groups["value"].Value ?? string.Empty;
                string decoded = DecodeJsonEscapedString(rawValue);
                if (!sawEscapedText && !string.Equals(rawValue, decoded, StringComparison.Ordinal))
                    sawEscapedText = true;
                if (LooksLikeGitDiff(decoded))
                    segments.Add(IsolateFromFirstDiff(decoded, result));
            }

            if (segments.Count == 0)
                return false;

            result.ExtractedFromJsonWrapper = fields.Count > 0;
            result.JsonWrapperIncompleteRecovered = true;
            result.ExtractedBlockCount = segments.Count;
            result.DiffEscapedTextDetected = sawEscapedText ||
                                             text.IndexOf("\\n", StringComparison.Ordinal) >= 0 ||
                                             text.IndexOf("\\r", StringComparison.Ordinal) >= 0 ||
                                             text.IndexOf("\\t", StringComparison.Ordinal) >= 0 ||
                                             text.IndexOf("\\\"", StringComparison.Ordinal) >= 0 ||
                                             text.IndexOf("\\\\", StringComparison.Ordinal) >= 0;
            result.EffectiveText = string.Join(Environment.NewLine + Environment.NewLine, segments.Where(s => !string.IsNullOrWhiteSpace(s)));
            recovered = true;
            return true;
        }

        private static bool LooksLikeGitDiff(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int hits = 0;
            string[] signals =
            {
                "diff --git a/",
                "--- a/",
                "+++ b/",
                "@@"
            };

            foreach (string signal in signals)
            {
                if (text.IndexOf(signal, StringComparison.OrdinalIgnoreCase) >= 0)
                    hits++;
            }

            return hits >= 2;
        }

        private static bool LooksLikeJsonWrapperCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] fieldNames =
            {
                "operacoes_diff",
                "gitdiff",
                "git_diff",
                "unified_diff",
                "diff",
                "diffs",
                "patches"
            };

            if (text.StartsWith("{", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[", StringComparison.OrdinalIgnoreCase))
                return true;

            return fieldNames.Any(field => text.IndexOf(field, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string IsolateFromFirstDiff(string text, GitDiffTextExtractionResult result)
        {
            string normalized = NormalizeText(text);
            int firstDiff = normalized.IndexOf("diff --git", StringComparison.OrdinalIgnoreCase);
            if (firstDiff > 0)
            {
                if (result != null)
                    result.PrefixDiscardedBeforeDiff = true;
                return normalized.Substring(firstDiff).Trim();
            }

            return normalized;
        }

        private static string DecodeJsonEscapedString(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string decoded = text;
            decoded = decoded.Replace("\\r\\n", "\n");
            decoded = decoded.Replace("\\n", "\n");
            decoded = decoded.Replace("\\r", "\n");
            decoded = decoded.Replace("\\t", "\t");
            decoded = decoded.Replace("\\\"", "\"");
            decoded = decoded.Replace("\\\\", "\\");
            return decoded;
        }

        private static bool ContainsJsonEscapes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("\\n", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("\\r", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("\\t", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("\\\"", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("\\\\", StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeEscapedGitDiffText(string text, GitDiffTextExtractionResult result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text;
            bool hasEscapes = ContainsJsonEscapes(normalized);

            if (hasEscapes)
            {
                if (result != null)
                    result.DiffEscapedTextDetected = true;
                normalized = DecodeJsonEscapedString(normalized);
            }

            return NormalizeText(normalized);
        }

        private static string NormalizeText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static bool TryParseJsonToken(string text, out JToken token)
        {
            token = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                token = JToken.Parse(text.Trim());
                return token != null;
            }
            catch
            {
                token = null;
                return false;
            }
        }

        private static string StripMarkdownFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            int firstNl = trimmed.IndexOf('\n');
            int lastFence = trimmed.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
            if (firstNl < 0 || lastFence <= firstNl)
                return trimmed;

            return trimmed.Substring(firstNl + 1, lastFence - firstNl - 1).Trim();
        }
    }
}
