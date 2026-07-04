using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class GitDiffParseResult
    {
        public bool IsGitDiff { get; set; }
        public bool IsValid { get; set; }
        public List<GitDiffFileChange> Files { get; } = new List<GitDiffFileChange>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public int TotalHunks { get; set; }
        public bool HadEmptyHunks { get; set; }
        public bool IgnoredTrailingEmptyHunks { get; set; }
        public bool HasEmptyHunkInMiddle { get; set; }
        public bool ContainsOnlyEmptyHunks { get; set; }
    }

    public sealed class GitDiffFileChange
    {
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public string EffectivePath { get; set; }
        public List<GitDiffHunk> Hunks { get; } = new List<GitDiffHunk>();
        public bool IsNewFile { get; set; }
        public bool IsDeletedFile { get; set; }
        public bool IsRename { get; set; }
    }

    public sealed class GitDiffHunk
    {
        public string Header { get; set; }
        public int? OldStart { get; set; }
        public int? OldCount { get; set; }
        public int? NewStart { get; set; }
        public int? NewCount { get; set; }
        public List<GitDiffLine> Lines { get; } = new List<GitDiffLine>();
    }

    public sealed class GitDiffLine
    {
        public GitDiffLineKind Kind { get; set; }
        public string Text { get; set; }
    }

    public enum GitDiffLineKind
    {
        Context,
        Added,
        Removed,
        Meta
    }
}
