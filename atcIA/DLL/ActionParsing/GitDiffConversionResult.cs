using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class GitDiffConversionResult
    {
        public bool Success { get; set; }
        public List<ConvertedGitDiffOperation> Operations { get; } = new List<ConvertedGitDiffOperation>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class ConvertedGitDiffOperation
    {
        public string FilePath { get; set; }
        public string SearchBlock { get; set; }
        public string ReplaceBlock { get; set; }
        public string ProtocolText { get; set; }
        public int HunkIndex { get; set; }
        public int RemovedLines { get; set; }
        public int AddedLines { get; set; }
        public int ContextLines { get; set; }
        public bool IsDeleteOnly { get; set; }
        public bool ExpandedFromDeleteOnlyContext { get; set; }
    }
}
