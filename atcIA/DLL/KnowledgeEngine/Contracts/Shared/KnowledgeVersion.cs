using System;

namespace Iazeitor.KnowledgeEngine.Contracts.Shared
{
    public sealed class KnowledgeVersion
    {
        public KnowledgeVersion(int major, int minor, int patch)
        {
            if (major < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(major));
            }

            if (minor < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minor));
            }

            if (patch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(patch));
            }

            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }

        public int Minor { get; }

        public int Patch { get; }
    }
}