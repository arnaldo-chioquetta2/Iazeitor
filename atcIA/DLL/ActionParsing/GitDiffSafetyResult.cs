using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class GitDiffSafetyResult
    {
        public bool IsSafe { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public int FileCount { get; set; }
        public int HunkCount { get; set; }
    }
}
