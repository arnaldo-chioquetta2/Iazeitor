using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Infrastructure.Persistence
{
    internal sealed class KnowledgePackFileStore
    {
        private const string ManifestFileName = "manifest.json";
        private const string HistoryFileName = "history.json";
        private const string VersionsDirectoryName = "versions";
        private const string TransactionsDirectoryName = ".transactions";
        private const string JournalFileName = "journal.json";
        private const string IncomingDirectoryName = "incoming";
        private const string PreviousDirectoryName = "previous";

        private readonly string rootDirectory;
        private readonly KnowledgePackStorageLock storageLock;

        public KnowledgePackFileStore(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("O diretório raiz deve ser informado.", nameof(rootDirectory));
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
            this.storageLock = new KnowledgePackStorageLock(this.rootDirectory);
        }

        public string RootDirectory
        {
            get { return rootDirectory; }
        }

        internal KnowledgePackStorageLock StorageLock
        {
            get { return storageLock; }
        }

        public string GetPackDirectory(KnowledgeIdentifier packId)
        {
            ValidateIdentifier(packId, nameof(packId));
            return GetContainedPath(SafeSegment(packId.Value, nameof(packId)));
        }

        public string GetVersionsDirectory(KnowledgeIdentifier packId)
        {
            return Path.Combine(GetPackDirectory(packId), VersionsDirectoryName);
        }

        public string GetManifestPath(KnowledgeIdentifier packId)
        {
            return Path.Combine(GetPackDirectory(packId), ManifestFileName);
        }

        public string GetHistoryPath(KnowledgeIdentifier packId)
        {
            return Path.Combine(GetPackDirectory(packId), HistoryFileName);
        }

        public string GetVersionPath(KnowledgeIdentifier packId, KnowledgeIdentifier versionId)
        {
            ValidateIdentifier(packId, nameof(packId));
            ValidateIdentifier(versionId, nameof(versionId));
            return GetContainedPath(
                SafeSegment(packId.Value, nameof(packId)),
                VersionsDirectoryName,
                SafeSegment(versionId.Value, nameof(versionId)) + ".json");
        }

        internal string GetTransactionsDirectory(KnowledgeIdentifier packId)
        {
            ValidateIdentifier(packId, nameof(packId));
            return Path.Combine(GetPackDirectory(packId), TransactionsDirectoryName);
        }

        internal string GetTransactionDirectory(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId)
        {
            ValidateIdentifier(packId, nameof(packId));
            ValidateIdentifier(transactionId, nameof(transactionId));
            return Path.Combine(GetTransactionsDirectory(packId), SafeSegment(transactionId.Value, nameof(transactionId)));
        }

        internal string GetJournalPath(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId)
        {
            return Path.Combine(GetTransactionDirectory(packId, transactionId), JournalFileName);
        }

        internal string GetIncomingDirectory(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            string stagingDirectoryName = IncomingDirectoryName)
        {
            return Path.Combine(GetTransactionDirectory(packId, transactionId), SafeSegment(stagingDirectoryName, nameof(stagingDirectoryName)));
        }

        internal string GetPreviousDirectory(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId)
        {
            return Path.Combine(GetTransactionDirectory(packId, transactionId), PreviousDirectoryName);
        }

        internal string GetIncomingManifestPath(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            string stagingDirectoryName = IncomingDirectoryName)
        {
            return Path.Combine(GetIncomingDirectory(packId, transactionId, stagingDirectoryName), ManifestFileName);
        }

        internal string GetIncomingHistoryPath(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            string stagingDirectoryName = IncomingDirectoryName)
        {
            return Path.Combine(GetIncomingDirectory(packId, transactionId, stagingDirectoryName), HistoryFileName);
        }

        internal string GetIncomingVersionPath(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgeIdentifier versionId,
            string stagingDirectoryName = IncomingDirectoryName)
        {
            ValidateIdentifier(versionId, nameof(versionId));
            return Path.Combine(
                GetIncomingDirectory(packId, transactionId, stagingDirectoryName),
                VersionsDirectoryName,
                SafeSegment(versionId.Value, nameof(versionId)) + ".json");
        }

        internal string GetPreviousManifestPath(KnowledgeIdentifier packId, KnowledgeIdentifier transactionId)
        {
            return Path.Combine(GetPreviousDirectory(packId, transactionId), ManifestFileName);
        }

        internal string GetPreviousHistoryPath(KnowledgeIdentifier packId, KnowledgeIdentifier transactionId)
        {
            return Path.Combine(GetPreviousDirectory(packId, transactionId), HistoryFileName);
        }

        internal string GetPreviousVersionPath(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            KnowledgeIdentifier versionId)
        {
            ValidateIdentifier(versionId, nameof(versionId));
            return Path.Combine(
                GetPreviousDirectory(packId, transactionId),
                VersionsDirectoryName,
                SafeSegment(versionId.Value, nameof(versionId)) + ".json");
        }

        internal void EnsureTransactionStructure(
            KnowledgeIdentifier packId,
            KnowledgeIdentifier transactionId,
            string stagingDirectoryName = IncomingDirectoryName)
        {
            Directory.CreateDirectory(GetIncomingDirectory(packId, transactionId, stagingDirectoryName));
            Directory.CreateDirectory(Path.Combine(GetIncomingDirectory(packId, transactionId, stagingDirectoryName), VersionsDirectoryName));
            Directory.CreateDirectory(GetPreviousDirectory(packId, transactionId));
            Directory.CreateDirectory(Path.Combine(GetPreviousDirectory(packId, transactionId), VersionsDirectoryName));
        }

        internal bool TransactionExists(KnowledgeIdentifier packId, KnowledgeIdentifier transactionId)
        {
            return Directory.Exists(GetTransactionDirectory(packId, transactionId));
        }

        internal bool JournalExists(KnowledgeIdentifier packId, KnowledgeIdentifier transactionId)
        {
            return File.Exists(GetJournalPath(packId, transactionId));
        }

        internal IReadOnlyList<KnowledgeIdentifier> ListTransactionIds(KnowledgeIdentifier packId)
        {
            string transactionsDirectory = GetTransactionsDirectory(packId);
            if (!Directory.Exists(transactionsDirectory))
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            var result = new List<KnowledgeIdentifier>();
            foreach (string directory in Directory.GetDirectories(transactionsDirectory))
            {
                string name = Path.GetFileName(directory);
                try
                {
                    if (!String.IsNullOrWhiteSpace(name))
                    {
                        result.Add(new KnowledgeIdentifier(SafeSegment(name, "transactionId")));
                    }
                }
                catch (ArgumentException)
                {
                    // Uma transação inválida não impede a enumeração das demais.
                }
            }

            return result
                .GroupBy(identifier => identifier.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList()
                .AsReadOnly();
        }

        internal string ReadTransactionText(string filePath)
        {
            return ReadText(filePath);
        }

        internal void WriteTransactionTextAtomically(string filePath, string content)
        {
            WriteTextAtomically(filePath, content);
        }

        internal void DeleteTransaction(KnowledgeIdentifier packId, KnowledgeIdentifier transactionId)
        {
            string transactionDirectory = GetTransactionDirectory(packId, transactionId);
            if (Directory.Exists(transactionDirectory))
            {
                Directory.Delete(transactionDirectory, true);
            }
        }

        public void EnsurePackStructure(KnowledgeIdentifier packId)
        {
            Directory.CreateDirectory(GetVersionsDirectory(packId));
        }

        internal bool AnyVersionFileExists(KnowledgeIdentifier packId)
        {
            string versionsDirectory = GetVersionsDirectory(packId);
            return Directory.Exists(versionsDirectory) &&
                Directory.GetFiles(versionsDirectory, "*.json").Length > 0;
        }

        /// <summary>
        /// Enumera apenas os arquivos versions/*.json, derivando o VersionId do nome do
        /// arquivo. Nao desserializa, nao cria diretorios e nao altera nada. Subdiretorios,
        /// extensoes diferentes de .json e nomes inseguros (regra IAZ:25) sao ignorados.
        /// </summary>
        internal IReadOnlyList<KnowledgeIdentifier> ListVersionIds(KnowledgeIdentifier packId)
        {
            ValidateIdentifier(packId, nameof(packId));

            string versionsDirectory = GetVersionsDirectory(packId);
            if (!Directory.Exists(versionsDirectory))
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            var result = new List<KnowledgeIdentifier>();
            foreach (string file in Directory.GetFiles(versionsDirectory))
            {
                if (!String.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    if (!String.IsNullOrWhiteSpace(name))
                    {
                        result.Add(new KnowledgeIdentifier(SafeSegment(name, "versionId")));
                    }
                }
                catch (ArgumentException)
                {
                    // Um arquivo invalido nao impede a enumeracao dos demais.
                }
            }

            return result
                .GroupBy(identifier => identifier.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList()
                .AsReadOnly();
        }

        public bool DirectoryExists(string directoryPath)
        {
            return Directory.Exists(directoryPath);
        }

        public bool FileExists(string filePath)
        {
            return File.Exists(filePath);
        }

        public IReadOnlyList<KnowledgeIdentifier> ListPackIds()
        {
            if (!Directory.Exists(rootDirectory))
            {
                return new List<KnowledgeIdentifier>().AsReadOnly();
            }

            var result = new List<KnowledgeIdentifier>();
            foreach (string directory in Directory.GetDirectories(rootDirectory))
            {
                string directoryName = Path.GetFileName(directory);
                if (String.IsNullOrWhiteSpace(directoryName) ||
                    !File.Exists(Path.Combine(directory, ManifestFileName)))
                {
                    continue;
                }

                try
                {
                    SafeSegment(directoryName, "packId");
                    result.Add(new KnowledgeIdentifier(directoryName));
                }
                catch (ArgumentException)
                {
                    // Uma entrada física inválida não impede a enumeração das demais.
                }
            }

            return result
                .GroupBy(identifier => identifier.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList()
                .AsReadOnly();
        }

        public string ReadText(string filePath)
        {
            return File.ReadAllText(filePath);
        }

        public void WriteTextAtomically(string filePath, string content)
        {
            // Sufixo temporario compacto: identifica o arquivo em construcao sem alongar
            // o caminho. Com os PackIds project-scoped da IAZ:36 o caminho de staging
            // ficou longo o bastante para o sufixo completo (32 hex) estourar MAX_PATH
            // em raizes profundas, deixando o Pack preso em Unresolved.
            string temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
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

        private static void ValidateIdentifier(KnowledgeIdentifier identifier, string parameterName)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            SafeSegment(identifier.Value, parameterName);
        }

        /// <summary>
        /// Reutiliza exatamente a regra de segmento seguro da IAZ:25 para validar
        /// identificadores sem lancar excecao. Usado por outros componentes de
        /// persistencia (ex.: binding de projeto) que compartilham a mesma raiz.
        /// </summary>
        internal static bool IsSafeSegment(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                SafeSegment(value, "segment");
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string SafeSegment(string value, string parameterName)
        {
            if (String.IsNullOrWhiteSpace(value) ||
                value == "." ||
                value == ".." ||
                Path.IsPathRooted(value) ||
                value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                value.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("O segmento de caminho transacional é inválido.", parameterName);
            }

            return value;
        }

        private string GetContainedPath(params string[] segments)
        {
            string path = rootDirectory;
            foreach (string segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            string fullPath = Path.GetFullPath(path);
            string normalizedRoot = rootDirectory.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? rootDirectory
                : rootDirectory + Path.DirectorySeparatorChar;
            if (!String.Equals(fullPath, rootDirectory, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("O caminho físico excede o diretório raiz.", nameof(segments));
            }

            return fullPath;
        }
    }
}
