using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Persistence
{
    /// <summary>
    /// Exclusão física por Knowledge Pack baseada em lock file com handle aberto.
    /// A exclusão é determinada pelo FileStream aberto (FileShare.None), nunca
    /// pela existência do arquivo. O arquivo residual é um tombstone inofensivo.
    /// </summary>
    internal sealed class KnowledgePackStorageLock
    {
        private const string LocksDirectoryName = ".locks";
        private const string LockFileExtension = ".lock";
        private const int RetryDelayMilliseconds = 50;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        /// <summary>
        /// Default interno conservador: bounded wait, sem configuração externa nesta etapa.
        /// </summary>
        private static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(5);

        private readonly string storageIdentity;
        private readonly string locksDirectory;

        public KnowledgePackStorageLock(string packsRootDirectory)
        {
            if (String.IsNullOrWhiteSpace(packsRootDirectory))
            {
                throw new ArgumentException("O diretório raiz dos Packs deve ser informado.", nameof(packsRootDirectory));
            }

            string normalizedRoot = NormalizeDirectory(packsRootDirectory);
            this.storageIdentity = CreateStorageIdentity(normalizedRoot);

            string parentDirectory = Path.GetDirectoryName(normalizedRoot);
            string lockRoot = String.IsNullOrWhiteSpace(parentDirectory)
                ? Path.Combine(normalizedRoot, LocksDirectoryName)
                : Path.Combine(parentDirectory, LocksDirectoryName);

            this.locksDirectory = NormalizeDirectory(lockRoot);
        }

        internal string LocksDirectory
        {
            get { return locksDirectory; }
        }

        public IDisposable Acquire(KnowledgeIdentifier packId)
        {
            return Acquire(packId, DefaultAcquisitionTimeout);
        }

        public IDisposable Acquire(KnowledgeIdentifier packId, TimeSpan timeout)
        {
            if (packId == null)
            {
                throw new ArgumentNullException(nameof(packId));
            }

            if (String.IsNullOrWhiteSpace(packId.Value))
            {
                throw new ArgumentException("O identificador do Pack deve ser informado.", nameof(packId));
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "O timeout não pode ser negativo.");
            }

            string lockPath = GetLockPath(packId);
            Directory.CreateDirectory(locksDirectory);

            long timeoutMilliseconds = (long)Math.Ceiling(timeout.TotalMilliseconds);
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                try
                {
                    return new LockHandle(new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None));
                }
                catch (IOException exception)
                {
                    if (!IsContention(exception))
                    {
                        throw;
                    }
                }

                if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    throw new KnowledgePackStorageLockTimeoutException(packId, timeout);
                }

                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        internal string GetLockPath(KnowledgeIdentifier packId)
        {
            if (packId == null)
            {
                throw new ArgumentNullException(nameof(packId));
            }

            string fileName = ComputeKeyHash(storageIdentity + "|" + packId.Value) + LockFileExtension;
            string lockPath = Path.GetFullPath(Path.Combine(locksDirectory, fileName));

            string normalizedLocksDirectory = locksDirectory.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? locksDirectory
                : locksDirectory + Path.DirectorySeparatorChar;

            if (!lockPath.StartsWith(normalizedLocksDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("O caminho do lock excede o diretório esperado.", nameof(packId));
            }

            return lockPath;
        }

        private static bool IsContention(IOException exception)
        {
            int code = exception.HResult & 0xFFFF;
            return code == ErrorSharingViolation || code == ErrorLockViolation;
        }

        private static string ComputeKeyHash(string key)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string CreateStorageIdentity(string normalizedDirectory)
        {
            // Case-insensitive no Windows para que roots equivalentes compartilhem a mesma identidade.
            return normalizedDirectory.ToUpperInvariant();
        }

        private static string NormalizeDirectory(string directory)
        {
            string fullPath = Path.GetFullPath(directory);
            string root = Path.GetPathRoot(fullPath);
            if (!String.IsNullOrEmpty(root) && String.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed class LockHandle : IDisposable
        {
            private FileStream stream;

            public LockHandle(FileStream stream)
            {
                this.stream = stream;
            }

            public void Dispose()
            {
                FileStream current = stream;
                if (current == null)
                {
                    return;
                }

                stream = null;
                current.Dispose();
            }
        }
    }
}
