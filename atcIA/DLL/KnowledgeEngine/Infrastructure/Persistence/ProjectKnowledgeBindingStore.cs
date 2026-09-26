using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Iazeitor.KnowledgeEngine.Contracts.Persistence;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Persistence
{
    /// <summary>
    /// Store dedicado da associacao persistente ProjectId -> PackIds.
    ///
    /// Nao vive dentro de packs/&lt;packId&gt; porque a chave primaria e o ProjectId.
    /// Escrita atomica (temporario + replace), leitura tolerante e fail-closed, e
    /// exclusao fisica por lock file com handle aberto (mesmo conceito da IAZ:27).
    /// O arquivo residual de lock e um tombstone inofensivo.
    ///
    /// Nao acopla a UI, nao reutiliza o manifest como indice e nao executa recovery.
    /// </summary>
    internal sealed class ProjectKnowledgeBindingStore : IProjectKnowledgeBindingStore
    {
        private const string BindingFileExtension = ".json";
        private const string LocksDirectoryName = ".locks";
        private const string LockFileExtension = ".lock";
        private const string LockKeyPrefix = "|binding|";
        private const int RetryDelayMilliseconds = 50;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        private static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(5);

        private readonly string rootDirectory;
        private readonly KnowledgePackSerializer serializer;
        private readonly string locksDirectory;

        public ProjectKnowledgeBindingStore(string rootDirectory, KnowledgePackSerializer serializer)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("O diretorio raiz dos bindings deve ser informado.", nameof(rootDirectory));
            }

            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            this.rootDirectory = NormalizeDirectory(rootDirectory);
            this.serializer = serializer;
            this.locksDirectory = Path.Combine(this.rootDirectory, LocksDirectoryName);
        }

        public string RootDirectory
        {
            get { return rootDirectory; }
        }

        public ProjectKnowledgeBindingContract Load(string projectId)
        {
            string normalized;
            if (!ProjectKnowledgeBindingContract.TryNormalizeProjectId(projectId, out normalized))
            {
                return null;
            }

            return LoadCore(normalized);
        }

        public bool AddAssociation(string projectId, KnowledgeIdentifier packId)
        {
            string normalized;
            if (!ProjectKnowledgeBindingContract.TryNormalizeProjectId(projectId, out normalized))
            {
                return false;
            }

            if (packId == null || !KnowledgePackFileStore.IsSafeSegment(packId.Value))
            {
                return false;
            }

            using (AcquireBindingLock(normalized))
            {
                string path = GetBindingPath(normalized);
                bool fileExists = File.Exists(path);
                ProjectKnowledgeBindingContract current = LoadCore(normalized);

                // Arquivo existente porem invalido nao e sobrescrito: preserva a
                // evidencia e falha fechado em vez de gravar associacao incompleta.
                if (fileExists && current == null)
                {
                    return false;
                }

                var packIds = new List<KnowledgeIdentifier>();
                if (current != null && current.PackIds != null)
                {
                    foreach (KnowledgeIdentifier existing in current.PackIds)
                    {
                        if (existing != null && !Contains(packIds, existing.Value))
                        {
                            packIds.Add(existing);
                        }
                    }
                }

                if (Contains(packIds, packId.Value))
                {
                    return true;
                }

                packIds.Add(packId);

                var contract = new ProjectKnowledgeBindingContract
                {
                    SchemaVersion = ProjectKnowledgeBindingContract.CurrentSchemaVersion,
                    ProjectId = normalized,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    PackIds = packIds.AsReadOnly()
                };

                WriteTextAtomically(path, serializer.SerializeProjectKnowledgeBindingContract(contract));
                return true;
            }
        }

        private ProjectKnowledgeBindingContract LoadCore(string normalizedProjectId)
        {
            string path = GetBindingPath(normalizedProjectId);
            if (!File.Exists(path))
            {
                return null;
            }

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return null;
            }

            ProjectKnowledgeBindingContract contract;
            try
            {
                contract = serializer.DeserializeProjectKnowledgeBindingContract(content);
            }
            catch (Exception)
            {
                return null;
            }

            if (!IsValid(contract, normalizedProjectId))
            {
                return null;
            }

            return Canonicalize(contract, normalizedProjectId);
        }

        private static bool IsValid(ProjectKnowledgeBindingContract contract, string expectedProjectId)
        {
            if (contract == null)
            {
                return false;
            }

            if (contract.SchemaVersion != ProjectKnowledgeBindingContract.CurrentSchemaVersion)
            {
                return false;
            }

            string normalized;
            if (!ProjectKnowledgeBindingContract.TryNormalizeProjectId(contract.ProjectId, out normalized) ||
                !String.Equals(normalized, expectedProjectId, StringComparison.Ordinal))
            {
                return false;
            }

            if (contract.PackIds == null)
            {
                return true;
            }

            foreach (KnowledgeIdentifier packId in contract.PackIds)
            {
                if (packId == null || !KnowledgePackFileStore.IsSafeSegment(packId.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static ProjectKnowledgeBindingContract Canonicalize(
            ProjectKnowledgeBindingContract contract,
            string projectId)
        {
            var packIds = new List<KnowledgeIdentifier>();
            if (contract.PackIds != null)
            {
                foreach (KnowledgeIdentifier packId in contract.PackIds)
                {
                    if (!Contains(packIds, packId.Value))
                    {
                        packIds.Add(packId);
                    }
                }
            }

            return new ProjectKnowledgeBindingContract
            {
                SchemaVersion = contract.SchemaVersion,
                ProjectId = projectId,
                UpdatedAt = contract.UpdatedAt,
                PackIds = packIds.AsReadOnly()
            };
        }

        private static bool Contains(IEnumerable<KnowledgeIdentifier> packIds, string value)
        {
            return packIds.Any(item => String.Equals(item.Value, value, StringComparison.Ordinal));
        }

        private string GetBindingPath(string normalizedProjectId)
        {
            string fileName = normalizedProjectId + BindingFileExtension;
            string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, fileName));
            string normalizedRoot = rootDirectory.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? rootDirectory
                : rootDirectory + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("O caminho do binding excede o diretorio raiz.", nameof(normalizedProjectId));
            }

            return fullPath;
        }

        private IDisposable AcquireBindingLock(string normalizedProjectId)
        {
            string lockName = ComputeKeyHash(rootDirectory.ToUpperInvariant() + LockKeyPrefix + normalizedProjectId);
            string lockPath = Path.Combine(locksDirectory, lockName + LockFileExtension);

            Directory.CreateDirectory(locksDirectory);

            long timeoutMilliseconds = (long)Math.Ceiling(DefaultAcquisitionTimeout.TotalMilliseconds);
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
                    // Reutiliza o tipo publico de timeout de lock fisico do projeto:
                    // contencao e distinguivel de corrupcao.
                    throw new KnowledgePackStorageLockTimeoutException(
                        new KnowledgeIdentifier(normalizedProjectId),
                        DefaultAcquisitionTimeout);
                }

                Thread.Sleep(RetryDelayMilliseconds);
            }
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

        private static void WriteTextAtomically(string filePath, string content)
        {
            string temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, content);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Replace(temporaryPath, filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
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
