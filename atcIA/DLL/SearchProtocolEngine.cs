using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GptBolDll
{
    /// <summary>
    /// GPTBol Search Protocol Engine (net481)
    ///
    /// Protocolo aceito:
    ///   ARQ=C:\caminho\arquivo.ext
    ///
    ///   SEARCH=texto
    ///   REPLACE=texto
    ///
    ///   SEARCH=texto
    ///   DELETE
    ///
    ///   SEARCH_BLOCK
    ///   linha 1
    ///   linha 2
    ///   END_SEARCH
    ///   REPLACE_BLOCK
    ///   linha nova 1
    ///   linha nova 2
    ///   END_REPLACE
    ///
    ///   SEARCH_BLOCK ... END_SEARCH
    ///   DELETE_BLOCK
    ///
    /// Regras:
    /// - A busca é incremental por arquivo (cursor avança).
    /// - SEARCH é "contém" (substring) em uma linha.
    /// - SEARCH_BLOCK é comparação por linhas (Trim() por padrão).
    /// </summary>
    public class SearchProtocolEngine
    {
        private readonly Action<string> _log;
        private readonly string _baseDirectory;

        public List<string> LastChangedFiles { get; } = new List<string>();

        public SearchProtocolEngine(Action<string> logger, string baseDirectory = null)
        {
            _log = logger ?? (_ => { });
            _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Environment.CurrentDirectory
                : baseDirectory;
        }

        public void Apply(string commandText, bool makeBackup = true)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                throw new ArgumentException("Comando vazio.");

            LastChangedFiles.Clear();
            PatchPlan patchPlan;
            try
            {
                var lines = SplitLines(commandText);
                patchPlan = BuildPatchPlan(lines);
            }
            catch (Exception ex)
            {
                var invalidFormat = ClassifyInvalidFormat(ex.Message);
                LogPatchClassification(invalidFormat, 0);
                throw new Exception(BuildPatchClassificationExceptionMessage(invalidFormat, ex.Message), ex);
            }

            PatchValidationResult validationResult = ValidatePatchPlan(patchPlan);
            LogPatchValidationResult(validationResult, patchPlan.Operations.Count);
            if (!validationResult.IsValid)
            {
                PatchFailureClassification validationClassification = ClassifyValidationFailure(validationResult);
                LogPatchClassification(validationClassification, patchPlan.Operations.Count);
                throw new Exception(BuildPatchClassificationExceptionMessage(validationClassification, BuildPatchValidationFailureMessage(validationResult)));
            }

            _log("[PATCH-PREFLIGHT] Validação iniciada. Operações: " + patchPlan.Operations.Count);

            var preflightBuffers = new Dictionary<string, FileBuffer>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ExecutePatchPlan(patchPlan, preflightBuffers, preflight: true);
            }
            catch (Exception ex)
            {
                PatchFailureClassification preflightClassification = ClassifyPreflightFailure(ex.Message, patchPlan);
                LogPatchClassification(preflightClassification, patchPlan.Operations.Count);
                throw new Exception(BuildPatchClassificationExceptionMessage(preflightClassification, ex.Message), ex);
            }
            _log("[PATCH-PREFLIGHT] Todas as âncoras foram encontradas. Aplicando alterações.");

            var fileBuffers = new Dictionary<string, FileBuffer>(StringComparer.OrdinalIgnoreCase);
            ExecutePatchPlan(patchPlan, fileBuffers, preflight: false);
            SaveChangedFiles(fileBuffers, makeBackup);
        }

        private static bool FileLinesEqual(string path, List<string> lines)
        {
            try
            {
                var existing = File.ReadAllLines(path);
                var next = lines ?? new List<string>();

                if (existing.Length != next.Count)
                    return false;

                for (int i = 0; i < existing.Length; i++)
                {
                    if (!string.Equals(existing[i], next[i] ?? string.Empty, StringComparison.Ordinal))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private PatchPlan BuildPatchPlan(List<string> lines)
        {
            string currentFile = null;
            int i = 0;
            var operations = new List<PatchOperation>();

            while (i < lines.Count)
            {
                var raw = lines[i];
                var line = raw.TrimEnd();

                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                if (IsIgnorableNoiseLine(line))
                {
                    _log($"[SKIP] Linha de ruído ignorada: {line}");
                    i++;
                    continue;
                }

                if (StartsWithCmd(line, "ARQ="))
                {
                    currentFile = ResolvePath(line.Substring(4).Trim());
                    if (string.IsNullOrWhiteSpace(currentFile))
                        throw new Exception("ARQ= sem caminho.");

                    _log($"[ARQ] Arquivo atual: {currentFile}");
                    i++;
                    continue;
                }

                if (currentFile == null)
                    throw new Exception("Faltou ARQ= antes das operações.");

                if (StartsWithCmd(line, "REPLACE="))
                {
                    var replace = ReadWholeFileReplacement(lines, ref i, line.Substring("REPLACE=".Length));
                    operations.Add(PatchOperation.ForWholeFileReplace(currentFile, replace, "REPLACE", line.Substring("REPLACE=".Length)));
                    continue;
                }

                if (StartsWithCmd(line.TrimStart(), "REPLACE_BLOCK="))
                {
                    var replaceBlock = ReadInlineReplaceBlock(lines, ref i, line.TrimStart().Substring("REPLACE_BLOCK=".Length));
                    i++;
                    operations.Add(PatchOperation.ForWholeFileReplace(currentFile, string.Join(Environment.NewLine, replaceBlock), "REPLACE_BLOCK", replaceBlock.FirstOrDefault() ?? string.Empty));
                    continue;
                }

                if (IsReplaceBlockMarker(line))
                {
                    i++;
                    var replaceBlock = ReadBlock(lines, ref i, "END_REPLACE", allowEndOfInput: true);
                    operations.Add(PatchOperation.ForWholeFileReplace(currentFile, string.Join(Environment.NewLine, replaceBlock), "REPLACE_BLOCK", replaceBlock.FirstOrDefault() ?? string.Empty));
                    continue;
                }

                if (StartsWithCmd(line, "SEARCH="))
                {
                    var searchLines = new List<string> { line.Substring("SEARCH=".Length) ?? string.Empty };
                    i++;

                    while (i < lines.Count && !IsSearchActionMarker(lines[i]))
                    {
                        searchLines.Add(lines[i]);
                        i++;
                    }

                    var search = string.Join(Environment.NewLine, searchLines);
                    bool searchMultiline = searchLines.Count > 1;

                    while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;

                    if (i >= lines.Count)
                    {
                        if (TryBuildEmbeddedReplaceAfterSearch(currentFile, searchLines, out PatchOperation embeddedOperation))
                        {
                            operations.Add(embeddedOperation);
                            continue;
                        }

                        throw new Exception("SEARCH= sem ação (REPLACE=, REPLACE_BLOCK ou DELETE).");
                    }

                    var action = lines[i].TrimEnd();

                    if (StartsWithCmd(action, "REPLACE="))
                    {
                        var replace = ReadWholeFileReplacement(lines, ref i, action.Substring("REPLACE=".Length));
                        operations.Add(searchMultiline
                            ? PatchOperation.ForBlockReplace(currentFile, searchLines, SplitLines(replace), "SEARCH_BLOCK", FirstRelevantLine(searchLines))
                            : PatchOperation.ForSearchReplace(currentFile, search, replace, "SEARCH", FirstRelevantLine(searchLines)));
                        continue;
                    }

                    if (IsReplaceBlockMarker(action))
                    {
                        i++;
                        var replaceBlock = ReadBlock(lines, ref i, "END_REPLACE");
                        operations.Add(searchMultiline
                            ? PatchOperation.ForBlockReplace(currentFile, searchLines, replaceBlock, "SEARCH_BLOCK", FirstRelevantLine(searchLines))
                            : PatchOperation.ForSearchReplace(currentFile, search, string.Join(Environment.NewLine, replaceBlock), "SEARCH", FirstRelevantLine(searchLines)));
                        continue;
                    }

                    if (StartsWithCmd(action.TrimStart(), "REPLACE_BLOCK="))
                    {
                        var replaceBlock = ReadInlineReplaceBlock(lines, ref i, action.TrimStart().Substring("REPLACE_BLOCK=".Length));
                        i++;
                        operations.Add(searchMultiline
                            ? PatchOperation.ForBlockReplace(currentFile, searchLines, replaceBlock, "SEARCH_BLOCK", FirstRelevantLine(searchLines))
                            : PatchOperation.ForSearchReplace(currentFile, search, string.Join(Environment.NewLine, replaceBlock), "SEARCH", FirstRelevantLine(searchLines)));
                        continue;
                    }

                    if (EqualsCmd(action, "DELETE"))
                    {
                        operations.Add(searchMultiline
                            ? PatchOperation.ForBlockDelete(currentFile, searchLines, "SEARCH_BLOCK", FirstRelevantLine(searchLines))
                            : PatchOperation.ForSearchDelete(currentFile, search, "SEARCH", FirstRelevantLine(searchLines)));
                        i++;
                        continue;
                    }

                    throw new Exception($"Ação inválida após SEARCH=. Esperado REPLACE=..., REPLACE_BLOCK ou DELETE. Encontrado: {action}");
                }

                if (StartsWithCmd(line, "SEARCH_BLOCK="))
                {
                    var searchBlock = ReadInlineSearchBlock(lines, ref i, line.Substring("SEARCH_BLOCK=".Length));
                    _log("[WARN] SEARCH_BLOCK= recebido. Interpretando como SEARCH_BLOCK.");
                    i++;

                    while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;

                    if (i >= lines.Count)
                        throw new Exception("SEARCH_BLOCK= sem ação (REPLACE_BLOCK ou DELETE_BLOCK).");

                    var action = lines[i].TrimEnd();

                    if (IsReplaceBlockMarker(action))
                    {
                        i++;
                        var replaceBlock = ReadBlock(lines, ref i, "END_REPLACE");
                        operations.Add(PatchOperation.ForBlockReplace(currentFile, searchBlock, replaceBlock, "SEARCH_BLOCK", FirstRelevantLine(searchBlock)));
                        continue;
                    }

                    if (StartsWithCmd(action.TrimStart(), "REPLACE_BLOCK="))
                    {
                        var replaceBlock = ReadInlineReplaceBlock(lines, ref i, action.TrimStart().Substring("REPLACE_BLOCK=".Length));
                        i++;
                        operations.Add(PatchOperation.ForBlockReplace(currentFile, searchBlock, replaceBlock, "SEARCH_BLOCK", FirstRelevantLine(searchBlock)));
                        continue;
                    }

                    if (EqualsCmd(action, "DELETE_BLOCK") || EqualsCmd(action, "DELETE"))
                    {
                        operations.Add(PatchOperation.ForBlockDelete(currentFile, searchBlock, "SEARCH_BLOCK", FirstRelevantLine(searchBlock)));
                        i++;
                        continue;
                    }

                    throw new Exception($"Ação inválida após SEARCH_BLOCK=. Esperado REPLACE_BLOCK, DELETE_BLOCK ou DELETE. Encontrado: {action}");
                }

                if (EqualsCmd(line, "SEARCH_BLOCK"))
                {
                    i++;
                    var searchBlock = ReadSearchBlock(lines, ref i);

                    while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;

                    if (i >= lines.Count)
                        throw new Exception("SEARCH_BLOCK sem ação (REPLACE_BLOCK ou DELETE_BLOCK).");

                    var action = lines[i].TrimEnd();

                    if (IsReplaceBlockMarker(action))
                    {
                        i++;
                        var replaceBlock = ReadBlock(lines, ref i, "END_REPLACE");
                        operations.Add(PatchOperation.ForBlockReplace(currentFile, searchBlock, replaceBlock, "SEARCH_BLOCK", FirstRelevantLine(searchBlock)));
                        continue;
                    }

                    if (EqualsCmd(action, "DELETE_BLOCK") || EqualsCmd(action, "DELETE"))
                    {
                        operations.Add(PatchOperation.ForBlockDelete(currentFile, searchBlock, "SEARCH_BLOCK", FirstRelevantLine(searchBlock)));
                        i++;
                        continue;
                    }

                    throw new Exception($"Ação inválida após SEARCH_BLOCK. Esperado REPLACE_BLOCK, DELETE_BLOCK ou DELETE. Encontrado: {action}");
                }

                throw new Exception($"Linha inesperada no protocolo: '{line}' (linha {i + 1})");
            }

            return new PatchPlan(operations);
        }

        private void ExecutePatchPlan(PatchPlan patchPlan, Dictionary<string, FileBuffer> fileBuffers, bool preflight)
        {
            if (patchPlan == null)
                throw new ArgumentNullException(nameof(patchPlan));

            for (int index = 0; index < patchPlan.Operations.Count; index++)
            {
                PatchOperation operation = patchPlan.Operations[index];
                EnsureLoaded(fileBuffers, operation.FilePath, operation.CreatesFileIfMissing);
                FileBuffer fileBuffer = fileBuffers[operation.FilePath];
                int operationIndex = index + 1;

                ExecutePatchOperation(preflight, operationIndex, patchPlan.Operations.Count, fileBuffer.Path, operation.OperationType, operation.SearchPreview, () =>
                {
                    ApplyPatchOperation(fileBuffer, operation);
                });
            }
        }

        private PatchValidationResult ValidatePatchPlan(PatchPlan patchPlan)
        {
            var result = new PatchValidationResult();
            if (patchPlan == null || patchPlan.Operations == null || patchPlan.Operations.Count == 0)
                return result;

            var duplicateMap = new Dictionary<string, PatchValidationIssue>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < patchPlan.Operations.Count; index++)
            {
                PatchOperation operation = patchPlan.Operations[index];
                int operationIndex = index + 1;

                ValidateDuplicateAnchor(result, duplicateMap, operation, operationIndex);
                ValidateEmptyAnchor(result, operation, operationIndex);
                ValidateEmptyReplace(result, operation, operationIndex);
                ValidateWeakBlockAnchor(result, operation, operationIndex);
                ValidateFragmentedReplace(result, operation, operationIndex);
                ValidateWrongLanguagePython(result, operation, operationIndex);
            }

            return result;
        }

        private void ValidateDuplicateAnchor(PatchValidationResult result, Dictionary<string, PatchValidationIssue> duplicateMap, PatchOperation operation, int operationIndex)
        {
            string anchorKey = operation.GetDuplicateAnchorKey();
            if (string.IsNullOrWhiteSpace(anchorKey))
                return;

            if (duplicateMap.ContainsKey(anchorKey))
            {
                result.Issues.Add(new PatchValidationIssue(
                    PatchValidationSeverity.Error,
                    "DUPLICATE_ANCHOR",
                    "[PATCH-VALIDATOR] Âncora duplicada no mesmo arquivo. Consolide em um único patch atômico.",
                    operation.FilePath,
                    operationIndex,
                    operation.SearchPreview));
                return;
            }

            duplicateMap[anchorKey] = new PatchValidationIssue(
                PatchValidationSeverity.Info,
                "DUPLICATE_ANCHOR",
                string.Empty,
                operation.FilePath,
                operationIndex,
                operation.SearchPreview);
        }

        private static void ValidateEmptyAnchor(PatchValidationResult result, PatchOperation operation, int operationIndex)
        {
            bool missingAnchor =
                (operation.Kind == PatchOperationKind.SearchReplace || operation.Kind == PatchOperationKind.SearchDelete) &&
                string.IsNullOrWhiteSpace(operation.SearchText);

            missingAnchor = missingAnchor ||
                ((operation.Kind == PatchOperationKind.BlockReplace || operation.Kind == PatchOperationKind.BlockDelete) &&
                 (operation.SearchBlock == null || operation.SearchBlock.Count == 0 || operation.SearchBlock.All(string.IsNullOrWhiteSpace)));

            if (!missingAnchor)
                return;

            result.Issues.Add(new PatchValidationIssue(
                PatchValidationSeverity.Error,
                "EMPTY_ANCHOR",
                "[PATCH-VALIDATOR] Operação sem âncora de busca.",
                operation.FilePath,
                operationIndex,
                operation.SearchPreview));
        }

        private static void ValidateEmptyReplace(PatchValidationResult result, PatchOperation operation, int operationIndex)
        {
            bool emptyReplace =
                operation.Kind == PatchOperationKind.SearchReplace &&
                string.IsNullOrWhiteSpace(operation.ReplaceText);

            emptyReplace = emptyReplace ||
                (operation.Kind == PatchOperationKind.BlockReplace &&
                 (operation.ReplaceBlock == null || operation.ReplaceBlock.Count == 0 || operation.ReplaceBlock.All(string.IsNullOrWhiteSpace)));

            if (!emptyReplace)
                return;

            result.Issues.Add(new PatchValidationIssue(
                PatchValidationSeverity.Error,
                "EMPTY_REPLACE",
                "[PATCH-VALIDATOR] REPLACE vazio em operação que não é DELETE.",
                operation.FilePath,
                operationIndex,
                operation.SearchPreview));
        }

        private static void ValidateWeakBlockAnchor(PatchValidationResult result, PatchOperation operation, int operationIndex)
        {
            if (operation.Kind != PatchOperationKind.BlockReplace && operation.Kind != PatchOperationKind.BlockDelete)
                return;

            if (operation.SearchBlock == null || operation.SearchBlock.Count != 1)
                return;

            string line = operation.SearchBlock[0] ?? string.Empty;
            string useful = Regex.Replace(line, @"\s+", string.Empty);
            if (useful.Length >= 20)
                return;

            result.Issues.Add(new PatchValidationIssue(
                PatchValidationSeverity.Warning,
                "WEAK_BLOCK_ANCHOR",
                "[PATCH-VALIDATOR] SEARCH_BLOCK muito curto; risco de âncora frágil.",
                operation.FilePath,
                operationIndex,
                operation.SearchPreview));
        }

        private static void ValidateFragmentedReplace(PatchValidationResult result, PatchOperation operation, int operationIndex)
        {
            if (operation.Kind != PatchOperationKind.BlockReplace || operation.ReplaceBlock == null || operation.ReplaceBlock.Count == 0)
                return;

            var meaningfulLines = operation.ReplaceBlock
                .Where(blockLine => !string.IsNullOrWhiteSpace(blockLine))
                .Select(blockLine => blockLine.Trim())
                .ToList();

            if (meaningfulLines.Count != 1)
                return;

            string line = meaningfulLines[0];
            bool fragmented =
                string.Equals(line, "return {", StringComparison.Ordinal) ||
                string.Equals(line, "\"Success\": False,", StringComparison.Ordinal) ||
                string.Equals(line, "\"Success\": false,", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @"^\s*if\b.*:\s*$", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"^\s*if\b.*\{\s*$", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"[:{]\s*$", RegexOptions.None);

            if (!fragmented)
                return;

            result.Issues.Add(new PatchValidationIssue(
                PatchValidationSeverity.Error,
                "FRAGMENTED_REPLACE",
                "[PATCH-VALIDATOR] REPLACE_BLOCK parece fragmento incompleto. Use patch atômico com bloco completo.",
                operation.FilePath,
                operationIndex,
                operation.SearchPreview));
        }

        private static void ValidateWrongLanguagePython(PatchValidationResult result, PatchOperation operation, int operationIndex)
        {
            if (!string.Equals(Path.GetExtension(operation.FilePath), ".py", StringComparison.OrdinalIgnoreCase))
                return;

            int braceDepth = 0;
            foreach (string line in operation.GetValidationLines())
            {
                string trimmed = (line ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                bool closesPythonDict = string.Equals(trimmed, "}", StringComparison.Ordinal) && braceDepth > 0;
                bool wrong =
                    (string.Equals(trimmed, "}", StringComparison.Ordinal) && !closesPythonDict) ||
                    Regex.IsMatch(trimmed, @"\bif\s*\(.*\)\s*\{", RegexOptions.IgnoreCase) ||
                    trimmed.IndexOf("function ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("var ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("let ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("const ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("=>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("<?php", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("public function", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("private function", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!wrong)
                {
                    braceDepth += CountCharOutsideSimpleStrings(trimmed, '{');
                    braceDepth -= CountCharOutsideSimpleStrings(trimmed, '}');
                    if (braceDepth < 0)
                        braceDepth = 0;
                    continue;
                }

                result.Issues.Add(new PatchValidationIssue(
                    PatchValidationSeverity.Error,
                    "WRONG_LANGUAGE_PY",
                    "[PATCH-VALIDATOR] Patch contém sintaxe incompatível com Python.",
                    operation.FilePath,
                operationIndex,
                trimmed));
                return;
            }
        }

        private void LogPatchValidationResult(PatchValidationResult validationResult, int totalOperations)
        {
            _log("[PATCH-VALIDATOR] Validação iniciada. Operações: " + totalOperations);

            foreach (PatchValidationIssue issue in validationResult.Issues.Where(i => i.Severity == PatchValidationSeverity.Warning))
            {
                _log("[PATCH-VALIDATOR] Warning " + issue.Code + " op " + issue.OperationIndex + "/" + totalOperations + ": " + issue.Message + " Preview=" + MaskSensitivePreview(issue.Preview));
            }

            foreach (PatchValidationIssue issue in validationResult.Issues.Where(i => i.Severity == PatchValidationSeverity.Error))
            {
                _log("[PATCH-VALIDATOR] Erro " + issue.Code + " op " + issue.OperationIndex + "/" + totalOperations + ": " + issue.Message + " Preview=" + MaskSensitivePreview(issue.Preview));
            }

            if (validationResult.IsValid)
            {
                _log("[PATCH-VALIDATOR] Validação concluída sem erros.");
                return;
            }

            _log("[PATCH-VALIDATOR] Validação falhou. Nenhum arquivo foi alterado.");
        }

        private PatchFailureClassification ClassifyValidationFailure(PatchValidationResult validationResult)
        {
            PatchValidationIssue issue = validationResult?.Issues?.FirstOrDefault(i => i.Severity == PatchValidationSeverity.Error)
                ?? validationResult?.Issues?.FirstOrDefault();

            if (issue == null)
                return ClassifyUnknown("Falha de validação sem issue detalhada.");

            switch ((issue.Code ?? string.Empty).ToUpperInvariant())
            {
                case "DUPLICATE_ANCHOR":
                    return new PatchFailureClassification(PatchFailureKind.DuplicateAnchor, "DUPLICATE_ANCHOR", "Âncora duplicada",
                        "A IA reutilizou a mesma âncora no mesmo arquivo. Consolide a alteração em um único patch atômico.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Não reutilize SEARCH ou SEARCH_BLOCK. Gere um único SEARCH_BLOCK literal maior e um único REPLACE_BLOCK completo.",
                        true, false);
                case "FRAGMENTED_REPLACE":
                    return new PatchFailureClassification(PatchFailureKind.FragmentedPatch, "FRAGMENTED_REPLACE", "Patch fragmentado",
                        "O REPLACE parece um fragmento incompleto de código.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Gere um bloco completo e sintaticamente válido. Não envie linhas soltas como return {, if sem corpo ou campos isolados.",
                        true, false);
                case "WRONG_LANGUAGE_PY":
                    return new PatchFailureClassification(PatchFailureKind.WrongLanguage, "WRONG_LANGUAGE_PY", "Sintaxe incompatível com Python",
                        "O patch contém sintaxe forte de outra linguagem em arquivo .py.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Use apenas sintaxe Python real vista no arquivo. Não invente chaves, Provider com maiúscula ou estruturas C#/JS/PHP.",
                        true, false);
                case "EMPTY_ANCHOR":
                    return new PatchFailureClassification(PatchFailureKind.EmptyAnchor, "EMPTY_ANCHOR", "Operação sem âncora",
                        "A operação não possui SEARCH ou SEARCH_BLOCK válido.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Forneça uma âncora literal existente no arquivo permitido.",
                        true, false);
                case "EMPTY_REPLACE":
                    return new PatchFailureClassification(PatchFailureKind.EmptyReplace, "EMPTY_REPLACE", "REPLACE vazio",
                        "A operação possui REPLACE vazio sem ser uma exclusão explícita.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Use DELETE explícito para remoção ou forneça REPLACE completo.",
                        true, false);
                case "WEAK_BLOCK_ANCHOR":
                    return new PatchFailureClassification(PatchFailureKind.WeakAnchor, "WEAK_BLOCK_ANCHOR", "Âncora frágil",
                        "SEARCH_BLOCK curto demais; pode encontrar trecho errado.",
                        issue.FilePath, issue.OperationIndex, issue.Preview,
                        "Use bloco literal maior e mais específico.",
                        false, false);
                default:
                    return ClassifyUnknown(issue.Message, issue.FilePath, issue.OperationIndex, issue.Preview);
            }
        }

        private PatchFailureClassification ClassifyPreflightFailure(string message, PatchPlan patchPlan)
        {
            string text = message ?? string.Empty;
            int operationIndex = ExtractOperationIndexFromPreflight(text);
            string filePath = ExtractPreflightField(text, "[PATCH-PREFLIGHT] Arquivo:");
            string preview = ExtractPreflightField(text, "[PATCH-PREFLIGHT] Primeiras linhas buscadas:");

            if (text.IndexOf("[IDEMPOTENTE]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new PatchFailureClassification(PatchFailureKind.Idempotent, "IDEMPOTENT", "Patch já aplicado",
                    "SEARCH não foi encontrado porque a alteração já parece estar aplicada.",
                    filePath, operationIndex, preview,
                    "Não reprocessar como erro.",
                    false, false);
            }

            if (text.IndexOf("SEARCH não encontrado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("SEARCH_BLOCK não encontrado", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new PatchFailureClassification(PatchFailureKind.InventedAnchor, "INVENTED_ANCHOR", "Âncora inventada",
                    "A âncora informada pela IA não existe literalmente no arquivo permitido.",
                    filePath, operationIndex, preview,
                    "Use somente texto copiado literalmente do arquivo. Não use nomes inferidos, blocos aproximados ou estrutura inventada.",
                    true, false);
            }

            return new PatchFailureClassification(PatchFailureKind.PreflightFailed, "PREFLIGHT_FAILED", "Preflight falhou",
                "O patch falhou na validação prévia antes da aplicação.",
                filePath, operationIndex, preview,
                "Revise a âncora e a estrutura do patch antes de reenviar.",
                true, false);
        }

        private static PatchFailureClassification ClassifyInvalidFormat(string message)
        {
            return new PatchFailureClassification(PatchFailureKind.InvalidFormat, "INVALID_FORMAT", "Formato de patch inválido",
                "A resposta da IA contém sinais de ação, mas não foi possível montar uma operação executável.",
                string.Empty, 0, string.Empty,
                "Responder usando somente o protocolo suportado: ARQ + SEARCH/REPLACE ou ARQ + SEARCH_BLOCK/REPLACE_BLOCK.",
                true, false);
        }

        private static PatchFailureClassification ClassifyUnknown(string message, string filePath = "", int operationIndex = 0, string preview = "")
        {
            return new PatchFailureClassification(PatchFailureKind.Unknown, "UNKNOWN", "Falha não classificada",
                message ?? "Falha não classificada. Manter erro original.",
                filePath, operationIndex, preview,
                string.Empty,
                false, false);
        }

        private void LogPatchClassification(PatchFailureClassification classification, int totalOperations)
        {
            if (classification == null)
                return;

            _log("[PATCH-CLASSIFIER] Tipo: " + classification.Kind);
            _log("[PATCH-CLASSIFIER] Codigo: " + classification.Code);
            _log("[PATCH-CLASSIFIER] Titulo: " + classification.Title);
            _log("[PATCH-CLASSIFIER] Arquivo: " + MaskSensitivePreview(classification.FilePath));
            _log("[PATCH-CLASSIFIER] Operacao: " + classification.OperationIndex + "/" + totalOperations);
            _log("[PATCH-CLASSIFIER] ShouldRetry: " + classification.ShouldRetry.ToString().ToLowerInvariant());
            _log("[PATCH-CLASSIFIER] Nenhum arquivo foi alterado: " + (!classification.WasFileModified).ToString().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(classification.HintForRetry))
                _log("[PATCH-CLASSIFIER] Dica retry: " + MaskSensitivePreview(classification.HintForRetry));
            if (classification.Kind == PatchFailureKind.Unknown)
                _log("[PATCH-CLASSIFIER] Falha não classificada. Manter erro original.");
        }

        private static string BuildPatchClassificationExceptionMessage(PatchFailureClassification classification, string originalDetail)
        {
            if (classification == null)
                return originalDetail ?? string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("[PATCH-CLASSIFIER] Tipo: " + classification.Kind);
            sb.AppendLine("[PATCH-CLASSIFIER] Codigo: " + classification.Code);
            sb.AppendLine("[PATCH-CLASSIFIER] Titulo: " + classification.Title);
            sb.AppendLine("[PATCH-CLASSIFIER] Arquivo: " + MaskSensitivePreview(classification.FilePath));
            sb.AppendLine("[PATCH-CLASSIFIER] Operacao: " + classification.OperationIndex);
            sb.AppendLine("[PATCH-CLASSIFIER] ShouldRetry: " + classification.ShouldRetry.ToString().ToLowerInvariant());
            sb.AppendLine("[PATCH-CLASSIFIER] Nenhum arquivo foi alterado: " + (!classification.WasFileModified).ToString().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(classification.SearchPreview))
                sb.AppendLine("[PATCH-CLASSIFIER] Preview: " + MaskSensitivePreview(classification.SearchPreview));
            if (!string.IsNullOrWhiteSpace(classification.HintForRetry))
                sb.AppendLine("[PATCH-CLASSIFIER] Dica retry: " + MaskSensitivePreview(classification.HintForRetry));
            sb.AppendLine("[PATCH-CLASSIFIER] " + classification.Title + ".");
            sb.AppendLine(classification.Message);
            sb.AppendLine("Nenhum arquivo foi alterado.");
            if (!string.IsNullOrWhiteSpace(classification.HintForRetry))
                sb.AppendLine("Dica: " + classification.HintForRetry);
            if (!string.IsNullOrWhiteSpace(originalDetail))
                sb.Append(originalDetail);
            return sb.ToString().TrimEnd();
        }

        private static int ExtractOperationIndexFromPreflight(string text)
        {
            Match match = Regex.Match(text ?? string.Empty, @"\[PATCH-PREFLIGHT\]\s*Opera(?:ç|c)ão:\s*(\d+)\/\d+", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int op))
                return op;
            return 0;
        }

        private static string ExtractPreflightField(string text, string prefix)
        {
            foreach (string line in SplitLines(text ?? string.Empty))
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
            }

            return string.Empty;
        }

        private static string BuildPatchValidationFailureMessage(PatchValidationResult validationResult)
        {
            var errors = validationResult.Issues
                .Where(i => i.Severity == PatchValidationSeverity.Error)
                .Take(5)
                .Select(i => i.Code + " op " + i.OperationIndex + ": " + i.Message)
                .ToList();

            return "[PATCH-VALIDATOR] Validação falhou. Nenhum arquivo foi alterado. " + string.Join(" | ", errors);
        }

        private static string MaskSensitivePreview(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string result = text;
            result = Regex.Replace(result, @"sk-[A-Za-z0-9_\-]{8,}", "[REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.\=]{8,}", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(?i)(OpenAiApiKey|ApiKey|Authorization)\\s*[:=]\\s*([^\\r\\n;,\\\"']+)", "$1: [REDACTED]", RegexOptions.IgnoreCase);
            return result;
        }

        private void SaveChangedFiles(Dictionary<string, FileBuffer> fileBuffers, bool makeBackup)
        {
            foreach (var kv in fileBuffers)
            {
                var fb = kv.Value;
                if (!fb.Changed)
                {
                    _log($"[SKIP] Sem alterações: {fb.Path}");
                    continue;
                }

                if (makeBackup && File.Exists(fb.Path))
                    MakeBackup(fb.Path);

                Directory.CreateDirectory(Path.GetDirectoryName(fb.Path) ?? _baseDirectory);

                if (string.Equals(Path.GetExtension(fb.Path), ".cs", StringComparison.OrdinalIgnoreCase))
                    ValidateCSharpStructureBeforeSave(fb);

                if (File.Exists(fb.Path) && FileLinesEqual(fb.Path, fb.Lines))
                {
                    _log($"[SKIP] Sem alteracao real: {fb.Path}");
                    continue;
                }

                File.WriteAllLines(fb.Path, fb.Lines);
                _log($"[OK] Salvo: {fb.Path}");
                LastChangedFiles.Add(fb.Path);

                if (string.Equals(Path.GetExtension(fb.Path), ".csproj", StringComparison.OrdinalIgnoreCase))
                    RemoveDuplicateCompileIncludes(fb.Path);

                if (string.Equals(Path.GetExtension(fb.Path), ".cs", StringComparison.OrdinalIgnoreCase))
                    EnsureProjectIncludesNewSource(fb.Path, makeBackup);
            }
        }

        private void ExecutePatchOperation(bool preflight, int operationIndex, int totalOperations, string filePath, string operationType, string searchPreview, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (preflight)
            {
                throw new Exception(BuildPatchPreflightFailureMessage(operationIndex, totalOperations, filePath, operationType, searchPreview, ex.Message), ex);
            }
        }

        private void ApplyPatchOperation(FileBuffer fileBuffer, PatchOperation operation)
        {
            switch (operation.Kind)
            {
                case PatchOperationKind.WholeFileReplace:
                    ApplyWholeFileReplace(fileBuffer, operation.ReplaceText ?? string.Empty);
                    return;
                case PatchOperationKind.SearchReplace:
                    ApplySearchReplace(fileBuffer, operation.SearchText ?? string.Empty, operation.ReplaceText ?? string.Empty);
                    return;
                case PatchOperationKind.SearchDelete:
                    ApplySearchDeleteLine(fileBuffer, operation.SearchText ?? string.Empty);
                    return;
                case PatchOperationKind.BlockReplace:
                    ApplyBlockReplace(fileBuffer, operation.SearchBlock ?? new List<string>(), operation.ReplaceBlock ?? new List<string>());
                    return;
                case PatchOperationKind.BlockDelete:
                    ApplyBlockDelete(fileBuffer, operation.SearchBlock ?? new List<string>());
                    return;
                default:
                    throw new Exception("Tipo de PatchOperation desconhecido: " + operation.Kind);
            }
        }

        private bool TryBuildEmbeddedReplaceAfterSearch(string currentFile, List<string> searchLines, out PatchOperation operation)
        {
            operation = null;
            if (searchLines == null || searchLines.Count < 2)
                return false;

            int replaceIndex = -1;
            for (int j = 1; j < searchLines.Count; j++)
            {
                string trimmed = (searchLines[j] ?? string.Empty).TrimStart();
                if (StartsWithCmd(trimmed, "REPLACE="))
                {
                    replaceIndex = j;
                    break;
                }
            }

            if (replaceIndex <= 0)
                return false;

            var realSearch = searchLines.Take(replaceIndex).ToList();
            var replaceLines = new List<string>();
            string firstReplace = (searchLines[replaceIndex] ?? string.Empty).TrimStart();
            replaceLines.Add(firstReplace.Substring("REPLACE=".Length));
            replaceLines.AddRange(searchLines.Skip(replaceIndex + 1));

            _log("[WARN] REPLACE= foi recebido dentro de SEARCH= multilinha. Separando automaticamente como substituicao de bloco.");
            operation = PatchOperation.ForBlockReplace(currentFile, realSearch, replaceLines, "SEARCH_BLOCK", FirstRelevantLine(realSearch));
            return true;
        }

        private static string FirstRelevantLine(IEnumerable<string> lines)
        {
            if (lines == null)
                return string.Empty;

            return lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        }

        private static string BuildPatchPreflightFailureMessage(int operationIndex, int totalOperations, string filePath, string operationType, string searchPreview, string detail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[PATCH-PREFLIGHT] Falha na validação. Nenhum arquivo foi alterado.");
            sb.AppendLine("[PATCH-PREFLIGHT] Operação: " + operationIndex + "/" + totalOperations);
            sb.AppendLine("[PATCH-PREFLIGHT] Arquivo: " + filePath);
            sb.AppendLine("[PATCH-PREFLIGHT] Tipo: " + operationType);
            sb.AppendLine("[PATCH-PREFLIGHT] Primeiras linhas buscadas: " + (searchPreview ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(detail))
                sb.Append("[PATCH-PREFLIGHT] Detalhe: " + detail);
            return sb.ToString().TrimEnd();
        }

        private static void ValidateCSharpStructureBeforeSave(FileBuffer fb)
        {
            if (fb == null || fb.Lines == null)
                return;

            ValidateNoMethodInsertedBetweenSignatureAndBody(fb);

            int depth = 0;

            for (int i = 0; i < fb.Lines.Count; i++)
            {
                string line = fb.Lines[i] ?? string.Empty;
                string trimmed = line.Trim();

                if (depth <= 0 && LooksLikeRootLevelMethod(trimmed))
                {
                    throw new Exception(
                        "Estrutura C# invalida: metodo aparenta estar fora de uma classe na linha " +
                        (i + 1) +
                        ". Provavelmente a IA inseriu codigo depois do ultimo '}'.");
                }

                depth += CountCharOutsideSimpleStrings(line, '{');
                depth -= CountCharOutsideSimpleStrings(line, '}');

                if (depth < 0)
                    depth = 0;
            }
        }

        private static void ValidateNoMethodInsertedBetweenSignatureAndBody(FileBuffer fb)
        {
            for (int i = 0; i < fb.Lines.Count; i++)
            {
                string atual = (fb.Lines[i] ?? string.Empty).Trim();

                if (!LooksLikeMethodSignatureWithoutBody(atual))
                    continue;

                int nextIndex = FindNextRelevantLine(fb.Lines, i + 1);
                if (nextIndex < 0)
                    continue;

                string proxima = (fb.Lines[nextIndex] ?? string.Empty).Trim();

                if (LooksLikeMethodSignatureWithoutBody(proxima) || LooksLikeMethodSignatureWithBodyStart(proxima))
                {
                    throw new Exception(
                        "Estrutura C# invalida: metodo inserido entre a assinatura e o corpo de outro metodo. " +
                        "Arquivo: " + fb.Path +
                        ". Linha aproximada: " + (i + 1) +
                        ". Assinatura original: " + atual +
                        ". Linha inserida: " + proxima);
                }
            }
        }

        private static int FindNextRelevantLine(List<string> lines, int start)
        {
            for (int i = start; i < lines.Count; i++)
            {
                string trimmed = (lines[i] ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                return i;
            }

            return -1;
        }

        private static bool LooksLikeMethodSignatureWithoutBody(string trimmed)
        {
            if (!LooksLikeCSharpMethodSignature(trimmed))
                return false;

            if (trimmed.EndsWith("{", StringComparison.Ordinal))
                return false;

            if (trimmed.EndsWith(";", StringComparison.Ordinal))
                return false;

            if (trimmed.Contains("=>"))
                return false;

            return true;
        }

        private static bool LooksLikeMethodSignatureWithBodyStart(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (!trimmed.EndsWith("{", StringComparison.Ordinal))
                return false;

            string withoutBrace = trimmed.Substring(0, trimmed.Length - 1).Trim();
            return LooksLikeCSharpMethodSignature(withoutBrace);
        }

        private static bool LooksLikeCSharpMethodSignature(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (!trimmed.Contains("(") || !trimmed.Contains(")"))
                return false;

            if (trimmed.Contains("="))
                return false;

            if (trimmed.StartsWith("if ", StringComparison.Ordinal) ||
                trimmed.StartsWith("if(", StringComparison.Ordinal) ||
                trimmed.StartsWith("for ", StringComparison.Ordinal) ||
                trimmed.StartsWith("for(", StringComparison.Ordinal) ||
                trimmed.StartsWith("foreach ", StringComparison.Ordinal) ||
                trimmed.StartsWith("foreach(", StringComparison.Ordinal) ||
                trimmed.StartsWith("while ", StringComparison.Ordinal) ||
                trimmed.StartsWith("while(", StringComparison.Ordinal) ||
                trimmed.StartsWith("switch ", StringComparison.Ordinal) ||
                trimmed.StartsWith("switch(", StringComparison.Ordinal) ||
                trimmed.StartsWith("catch ", StringComparison.Ordinal) ||
                trimmed.StartsWith("catch(", StringComparison.Ordinal) ||
                trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                trimmed.StartsWith("using(", StringComparison.Ordinal) ||
                trimmed.StartsWith("lock ", StringComparison.Ordinal) ||
                trimmed.StartsWith("lock(", StringComparison.Ordinal))
            {
                return false;
            }

            return trimmed.StartsWith("private ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("public ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("protected ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("static ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("async ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("partial ", StringComparison.Ordinal);
        }

        private static bool LooksLikeRootLevelMethod(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                trimmed.StartsWith("namespace ", StringComparison.Ordinal) ||
                trimmed.StartsWith("public class ", StringComparison.Ordinal) ||
                trimmed.StartsWith("public partial class ", StringComparison.Ordinal) ||
                trimmed.StartsWith("internal class ", StringComparison.Ordinal) ||
                trimmed.StartsWith("class ", StringComparison.Ordinal) ||
                trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return false;
            }

            bool startsLikeMember =
                trimmed.StartsWith("public ", StringComparison.Ordinal) ||
                trimmed.StartsWith("private ", StringComparison.Ordinal) ||
                trimmed.StartsWith("protected ", StringComparison.Ordinal) ||
                trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
                trimmed.StartsWith("static ", StringComparison.Ordinal) ||
                trimmed.StartsWith("async ", StringComparison.Ordinal) ||
                trimmed.StartsWith("void ", StringComparison.Ordinal);

            if (!startsLikeMember)
                return false;

            if (!trimmed.Contains("(") || !trimmed.Contains(")"))
                return false;

            return !trimmed.Contains(" class ") &&
                   !trimmed.Contains(" interface ") &&
                   !trimmed.Contains(" struct ") &&
                   !trimmed.Contains(" enum ") &&
                   !trimmed.Contains(" delegate ");
        }

        private static int CountCharOutsideSimpleStrings(string line, char target)
        {
            if (string.IsNullOrEmpty(line))
                return 0;

            int count = 0;
            bool inString = false;
            bool inChar = false;
            bool escaped = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if ((inString || inChar) && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (!inChar && c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString && c == '\'')
                {
                    inChar = !inChar;
                    continue;
                }

                if (!inString && !inChar && c == target)
                    count++;
            }

            return count;
        }

        // ------------------ Implementações de operações ------------------

        private void ApplySearchReplace(FileBuffer fb, string search, string replace)
        {
            int idx = FindLineContaining(fb, search);
            if (idx < 0)
            {
                if (ReplacementAlreadyApplied(fb, replace))
                {
                    LogPatchClassification(
                        new PatchFailureClassification(
                            PatchFailureKind.Idempotent,
                            "IDEMPOTENT",
                            "Patch já aplicado",
                            "SEARCH não foi encontrado porque a alteração já parece estar aplicada.",
                            fb.Path,
                            0,
                            search,
                            "Não reprocessar como erro.",
                            false,
                            false),
                        0);
                    _log($"[IDEMPOTENTE] SEARCH não encontrado, mas REPLACE já está aplicado.");
                    fb.Cursor = Math.Min(fb.Cursor, fb.Lines.Count);
                    return;
                }

                throw new Exception($"SEARCH não encontrado (a partir do cursor): '{search}'");
            }

            var before = fb.Lines[idx];
            var replacementLines = SplitLines(replace ?? string.Empty);
            if (replacementLines.Count <= 1)
            {
                fb.Lines[idx] = before.Replace(search, replace ?? string.Empty);
                fb.Cursor = idx + 1;
            }
            else
            {
                replacementLines[0] = before.Replace(search, replacementLines[0]);
                fb.Lines.RemoveAt(idx);
                fb.Lines.InsertRange(idx, replacementLines);
                fb.Cursor = idx + replacementLines.Count;
            }

            fb.Changed = true;

            _log($"[REPLACE] Linha {idx + 1}: '{search}' => '{replace}'");
        }

        private static bool ReplacementAlreadyApplied(FileBuffer fb, string replace)
        {
            if (fb == null || fb.Lines == null)
                return false;

            string target = replace ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string normalizedTarget = NormalizeTextForIdempotence(target);
            if (string.IsNullOrWhiteSpace(normalizedTarget))
                return false;

            foreach (string line in fb.Lines)
            {
                string normalizedLine = NormalizeTextForIdempotence(line ?? string.Empty);
                if (normalizedLine.IndexOf(normalizedTarget, StringComparison.Ordinal) >= 0)
                    return true;
            }

            string joined = NormalizeTextForIdempotence(string.Join(Environment.NewLine, fb.Lines ?? new List<string>()));
            return joined.IndexOf(normalizedTarget, StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeTextForIdempotence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();
        }

        private void ApplyWholeFileReplace(FileBuffer fb, string replace)
        {
            fb.Lines.Clear();
            fb.Lines.AddRange(SplitLines(replace));
            fb.Changed = true;
            fb.Cursor = fb.Lines.Count;
            _log($"[REPLACE_FILE] Conteudo completo atualizado: {fb.Path}");
        }

        private void ApplySearchDeleteLine(FileBuffer fb, string search)
        {
            int idx = FindLineContaining(fb, search);
            if (idx < 0)
                throw new Exception($"SEARCH não encontrado para DELETE (a partir do cursor): '{search}'");

            _log($"[DELETE] Removendo linha {idx + 1} (contém '{search}')");
            fb.Lines.RemoveAt(idx);
            fb.Changed = true;

            fb.Cursor = Math.Max(0, idx); // incremental (fica na posição do delete)
        }

        private void ApplyBlockReplace(FileBuffer fb, List<string> searchBlock, List<string> replaceBlock)
        {
            var effectiveSearchBlock = NormalizeSearchBlockEdges(searchBlock);
            var start = FindBlock(fb, effectiveSearchBlock);
            int removeCount = effectiveSearchBlock.Count;
            if (start < 0 && IsCSharpFile(fb))
            {
                var member = FindCSharpMemberBlock(fb, effectiveSearchBlock);
                if (member.Start >= 0)
                {
                    start = member.Start;
                    removeCount = member.Count;
                    _log("[WARN] SEARCH_BLOCK nao encontrado literalmente; substituindo bloco C# localizado pela assinatura.");
                }
            }

            if (start < 0)
                throw new Exception(BuildBlockNotFoundMessage(fb, effectiveSearchBlock, "SEARCH_BLOCK não encontrado (a partir do cursor)."));

            _log($"[BLOCK REPLACE] Linhas {start + 1}..{start + removeCount}");

            fb.Lines.RemoveRange(start, removeCount);
            fb.Lines.InsertRange(start, replaceBlock);

            fb.Changed = true;
            fb.Cursor = start + replaceBlock.Count; // incremental
        }

        private void ApplyBlockDelete(FileBuffer fb, List<string> searchBlock)
        {
            var effectiveSearchBlock = NormalizeSearchBlockEdges(searchBlock);
            var start = FindBlock(fb, effectiveSearchBlock);
            if (start < 0)
                throw new Exception(BuildBlockNotFoundMessage(fb, effectiveSearchBlock, "SEARCH_BLOCK não encontrado para DELETE_BLOCK (a partir do cursor)."));

            _log($"[DELETE_BLOCK] Removendo linhas {start + 1}..{start + effectiveSearchBlock.Count}");
            fb.Lines.RemoveRange(start, effectiveSearchBlock.Count);

            fb.Changed = true;
            fb.Cursor = Math.Max(0, start);
        }

        private bool TryApplyEmbeddedReplaceAfterSearch(FileBuffer fb, List<string> searchLines)
        {
            if (fb == null || searchLines == null || searchLines.Count < 2)
                return false;

            int replaceIndex = -1;
            for (int j = 1; j < searchLines.Count; j++)
            {
                string trimmed = (searchLines[j] ?? string.Empty).TrimStart();
                if (StartsWithCmd(trimmed, "REPLACE="))
                {
                    replaceIndex = j;
                    break;
                }
            }

            if (replaceIndex <= 0)
                return false;

            var realSearch = searchLines.Take(replaceIndex).ToList();
            var replaceLines = new List<string>();
            string firstReplace = (searchLines[replaceIndex] ?? string.Empty).TrimStart();
            replaceLines.Add(firstReplace.Substring("REPLACE=".Length));
            replaceLines.AddRange(searchLines.Skip(replaceIndex + 1));

            _log("[WARN] REPLACE= foi recebido dentro de SEARCH= multilinha. Separando automaticamente como substituicao de bloco.");
            ApplyBlockReplace(fb, realSearch, replaceLines);
            return true;
        }

        // ------------------ Busca incremental ------------------

        private int FindLineContaining(FileBuffer fb, string search)
        {
            int idx = FindLineContainingExact(fb, search, fb.Cursor, fb.Lines.Count);
            if (idx >= 0)
                return idx;

            int tolerantIndex = FindLineContainingTolerant(fb, search, fb.Cursor, fb.Lines.Count);
            if (tolerantIndex >= 0)
            {
                _log("[WARN] SEARCH encontrado por comparacao tolerante.");
                return tolerantIndex;
            }

            if (fb.Cursor > 0)
            {
                idx = FindLineContainingExact(fb, search, 0, fb.Cursor);
                if (idx >= 0)
                {
                    _log($"[WARN] SEARCH nao encontrado a partir do cursor {fb.Cursor + 1}; encontrado antes do cursor na linha {idx + 1}.");
                    return idx;
                }

                tolerantIndex = FindLineContainingTolerant(fb, search, 0, fb.Cursor);
                if (tolerantIndex >= 0)
                {
                    _log($"[WARN] SEARCH encontrado por comparacao tolerante antes do cursor, linha {tolerantIndex + 1}.");
                    return tolerantIndex;
                }
            }

            return -1;
        }

        private static List<string> NormalizeSearchBlockEdges(List<string> searchBlock)
        {
            if (searchBlock == null || searchBlock.Count == 0)
                return new List<string>();

            int first = 0;
            int last = searchBlock.Count - 1;

            while (first <= last && string.IsNullOrWhiteSpace(searchBlock[first]))
                first++;

            while (last >= first && string.IsNullOrWhiteSpace(searchBlock[last]))
                last--;

            if (first > last)
                return new List<string>();

            if (first == 0 && last == searchBlock.Count - 1)
                return searchBlock;

            return searchBlock.Skip(first).Take(last - first + 1).ToList();
        }

        private static int FindLineContainingExact(FileBuffer fb, string search, int start, int endExclusive)
        {
            if (fb == null || fb.Lines == null || search == null)
                return -1;

            int begin = Math.Max(0, start);
            int end = Math.Min(endExclusive, fb.Lines.Count);
            for (int i = begin; i < end; i++)
            {
                if ((fb.Lines[i] ?? string.Empty).Contains(search))
                    return i;
            }

            return -1;
        }

        private int FindLineContainingTolerant(FileBuffer fb, string search, int start, int endExclusive)
        {
            if (fb == null || string.IsNullOrWhiteSpace(search))
                return -1;

            string normalizedSearch = NormalizeForSimilarity(search);
            string normalizedCSharpSearch = NormalizeCSharpSignatureForSearch(search);

            int begin = Math.Max(0, start);
            int end = Math.Min(endExclusive, fb.Lines.Count);
            for (int i = begin; i < end; i++)
            {
                string line = fb.Lines[i] ?? string.Empty;
                if (!string.IsNullOrEmpty(normalizedSearch) &&
                    NormalizeForSimilarity(line).Contains(normalizedSearch))
                    return i;

                if (!string.IsNullOrEmpty(normalizedCSharpSearch) &&
                    NormalizeCSharpSignatureForSearch(line).Contains(normalizedCSharpSearch))
                    return i;
            }

            return -1;
        }

        private int FindBlock(FileBuffer fb, List<string> block)
        {
            if (block.Count == 0) return -1;

            // comparação por Trim() para tolerar indentação
            var blockTrim = block.Select(x => (x ?? "").Trim()).ToList();

            int exactStart = FindBlockExact(fb, blockTrim, fb.Cursor, fb.Lines.Count);
            if (exactStart >= 0)
                return exactStart;

            var fuzzyStart = FindBlockTolerant(fb, blockTrim, fb.Cursor, fb.Lines.Count);
            if (fuzzyStart >= 0)
            {
                _log("[WARN] SEARCH_BLOCK encontrado por comparacao tolerante. Revise o resultado se o bloco gerado era muito grande.");
                return fuzzyStart;
            }

            if (fb.Cursor > 0)
            {
                exactStart = FindBlockExact(fb, blockTrim, 0, fb.Cursor);
                if (exactStart >= 0)
                {
                    _log($"[WARN] SEARCH_BLOCK nao encontrado a partir do cursor {fb.Cursor + 1}; encontrado antes do cursor na linha {exactStart + 1}.");
                    return exactStart;
                }

                fuzzyStart = FindBlockTolerant(fb, blockTrim, 0, fb.Cursor);
                if (fuzzyStart >= 0)
                {
                    _log($"[WARN] SEARCH_BLOCK encontrado por comparacao tolerante antes do cursor, linha {fuzzyStart + 1}. Revise o resultado se o bloco gerado era muito grande.");
                    return fuzzyStart;
                }
            }

            return -1;
        }

        private static int FindBlockExact(FileBuffer fb, List<string> blockTrim, int start, int endExclusive)
        {
            if (fb == null || fb.Lines == null || blockTrim == null || blockTrim.Count == 0)
                return -1;

            int begin = Math.Max(0, start);
            int lastStart = Math.Min(endExclusive, fb.Lines.Count) - blockTrim.Count;
            for (int i = begin; i <= lastStart; i++)
            {
                bool ok = true;
                for (int j = 0; j < blockTrim.Count; j++)
                {
                    var a = (fb.Lines[i + j] ?? "").Trim();
                    var b = blockTrim[j];
                    if (!string.Equals(a, b, StringComparison.Ordinal))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return i;
            }

            return -1;
        }

        private static bool IsCSharpFile(FileBuffer fb)
        {
            return fb != null &&
                   !string.IsNullOrWhiteSpace(fb.Path) &&
                   string.Equals(Path.GetExtension(fb.Path), ".cs", StringComparison.OrdinalIgnoreCase);
        }

        private (int Start, int Count) FindCSharpMemberBlock(FileBuffer fb, List<string> searchBlock)
        {
            string signature = GetLikelyCSharpSignature(searchBlock);
            if (string.IsNullOrWhiteSpace(signature))
                return (-1, 0);

            string normalizedSignature = NormalizeCSharpSignatureForSearch(signature);
            if (normalizedSignature.Length < 8)
                return (-1, 0);

            for (int i = fb.Cursor; i < fb.Lines.Count; i++)
            {
                string line = (fb.Lines[i] ?? string.Empty).Trim();
                if (NormalizeCSharpSignatureForSearch(line) != normalizedSignature)
                    continue;

                int openBraceLine = FindOpeningBraceLine(fb, i);
                if (openBraceLine < 0)
                    return (-1, 0);

                int endLine = FindMatchingBraceLine(fb, openBraceLine);
                if (endLine < 0)
                    return (-1, 0);

                return (i, endLine - i + 1);
            }

            return (-1, 0);
        }

        private static string GetLikelyCSharpSignature(List<string> searchBlock)
        {
            if (searchBlock == null)
                return string.Empty;

            foreach (string raw in searchBlock)
            {
                string line = (raw ?? string.Empty).Trim();
                if (line.Length == 0 || line == "{" || line == "}")
                    continue;

                if (line.Contains("(") && line.Contains(")") && !line.EndsWith(";", StringComparison.Ordinal))
                    return line;
            }

            return string.Empty;
        }

        private static int FindOpeningBraceLine(FileBuffer fb, int signatureLine)
        {
            for (int i = signatureLine; i < fb.Lines.Count && i <= signatureLine + 5; i++)
            {
                if ((fb.Lines[i] ?? string.Empty).Contains("{"))
                    return i;
            }

            return -1;
        }

        private static int FindMatchingBraceLine(FileBuffer fb, int openBraceLine)
        {
            int depth = 0;
            bool foundOpen = false;

            for (int i = openBraceLine; i < fb.Lines.Count; i++)
            {
                string line = fb.Lines[i] ?? string.Empty;
                for (int j = 0; j < line.Length; j++)
                {
                    char c = line[j];
                    if (c == '{')
                    {
                        depth++;
                        foundOpen = true;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (foundOpen && depth == 0)
                            return i;
                    }
                }
            }

            return -1;
        }

        private int FindBlockTolerant(FileBuffer fb, List<string> blockTrim, int start, int endExclusive)
        {
            if (fb == null || blockTrim == null || blockTrim.Count == 0)
                return -1;

            int minExactMatches = blockTrim.Count == 1
                ? 0
                : Math.Max(1, blockTrim.Count - Math.Max(1, blockTrim.Count / 10));

            int begin = Math.Max(0, start);
            int lastStart = Math.Min(endExclusive, fb.Lines.Count) - blockTrim.Count;
            for (int i = begin; i <= lastStart; i++)
            {
                int exactMatches = 0;
                bool ok = true;

                for (int j = 0; j < blockTrim.Count; j++)
                {
                    var actual = (fb.Lines[i + j] ?? "").Trim();
                    var expected = blockTrim[j];

                    if (string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        exactMatches++;
                        continue;
                    }

                    if (!AreLinesSimilar(actual, expected))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok && exactMatches >= minExactMatches)
                    return i;
            }

            return -1;
        }

        private static bool AreLinesSimilar(string actual, string expected)
        {
            var a = NormalizeForSimilarity(actual);
            var b = NormalizeForSimilarity(expected);

            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;

            if (a.StartsWith("//", StringComparison.Ordinal) &&
                string.Equals(a.Substring(2), b, StringComparison.Ordinal))
                return true;

            if (a.Length < 12 || b.Length < 12)
                return false;

            int maxLen = Math.Max(a.Length, b.Length);
            int distance = LevenshteinDistance(a, b, maxDistance: Math.Max(2, maxLen / 10));
            if (distance < 0)
                return false;

            return distance <= Math.Max(2, maxLen / 10);
        }

        private static string NormalizeForSimilarity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = RepairMojibakeRepeated(text);
            text = RemoveDiacritics(text);

            var chars = text
                .Where(c => !char.IsWhiteSpace(c))
                .ToArray();

            return new string(chars);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string normalized = text.Normalize(NormalizationForm.FormD);

            var chars = normalized
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray();

            return new string(chars).Normalize(NormalizationForm.FormC);
        }

        private static string RepairMojibakeRepeated(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string current = text;

            for (int i = 0; i < 3; i++)
            {
                string repaired = TryRepairMojibakeOnce(current);
                if (string.Equals(repaired, current, StringComparison.Ordinal))
                    break;

                current = repaired;
            }

            return current;
        }

        private static string TryRepairMojibakeOnce(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (!LooksLikeMojibake(text))
                return text;

            try
            {
                var bytes = Encoding.GetEncoding(1252).GetBytes(text);
                var repaired = Encoding.UTF8.GetString(bytes);

                return MojibakeScore(repaired) < MojibakeScore(text)
                    ? repaired
                    : text;
            }
            catch
            {
                return text;
            }
        }

        private static bool LooksLikeMojibake(string text)
        {
            return text.IndexOf("Ã", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("Â", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("�", StringComparison.Ordinal) >= 0;
        }

        private static int MojibakeScore(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int score = 0;
            foreach (char c in text)
            {
                if (c == 'Ã' || c == 'Â' || c == '�')
                    score++;
            }

            return score;
        }

        private static string NormalizeCSharpSignatureForSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = NormalizeForSimilarity(text);
            normalized = normalized.Replace("privateasync", "private");
            normalized = normalized.Replace("publicasync", "public");
            normalized = normalized.Replace("protectedasync", "protected");
            normalized = normalized.Replace("internalasync", "internal");
            normalized = normalized.Replace("staticasync", "static");
            return normalized;
        }

        private static int LevenshteinDistance(string a, string b, int maxDistance)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;

            if (Math.Abs(a.Length - b.Length) > maxDistance)
                return -1;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];

                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);

                    if (current[j] < rowMin)
                        rowMin = current[j];
                }

                if (rowMin > maxDistance)
                    return -1;

                var temp = previous;
                previous = current;
                current = temp;
            }

            return previous[b.Length];
        }

        private static string BuildBlockNotFoundMessage(FileBuffer fb, List<string> searchBlock, string message)
        {
            var preview = searchBlock == null || searchBlock.Count == 0
                ? "(bloco vazio)"
                : string.Join(" | ", searchBlock.Take(3).Select(x => (x ?? "").Trim()));

            string details = BuildSimilarBlockDiagnostics(fb, searchBlock);
            return message + " Arquivo: " + (fb == null ? "" : fb.Path) + ". Primeiras linhas buscadas: " + preview + details;
        }

        private static string BuildSimilarBlockDiagnostics(FileBuffer fb, List<string> searchBlock)
        {
            if (fb == null || fb.Lines == null || searchBlock == null || searchBlock.Count == 0)
                return string.Empty;

            var anchors = searchBlock
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length >= 12)
                .Take(4)
                .ToList();

            if (anchors.Count == 0)
                return string.Empty;

            var candidates = new List<int>();
            foreach (string anchor in anchors)
            {
                int found = FindSimilarLineIndex(fb, anchor);
                if (found >= 0 && !candidates.Contains(found))
                    candidates.Add(found);
            }

            if (candidates.Count == 0)
            {
                string csharpMembers = BuildCSharpMemberDiagnostics(fb);
                return string.IsNullOrWhiteSpace(csharpMembers) ? string.Empty : csharpMembers;
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Trechos parecidos encontrados no arquivo atual:");

            foreach (int candidate in candidates.Take(3))
            {
                int start = Math.Max(0, candidate - 2);
                int end = Math.Min(fb.Lines.Count - 1, candidate + Math.Max(5, Math.Min(12, searchBlock.Count + 2)));
                sb.AppendLine("Linha aproximada " + (candidate + 1) + ":");
                for (int i = start; i <= end; i++)
                    sb.AppendLine((i + 1) + ": " + (fb.Lines[i] ?? string.Empty));
            }

            string members = BuildCSharpMemberDiagnostics(fb);
            if (!string.IsNullOrWhiteSpace(members))
                sb.Append(members);

            return sb.ToString();
        }

        private static int FindSimilarLineIndex(FileBuffer fb, string anchor)
        {
            string normalizedAnchor = NormalizeForLooseSearch(anchor);
            if (normalizedAnchor.Length == 0)
                return -1;

            for (int i = fb.Cursor; i < fb.Lines.Count; i++)
            {
                string normalizedLine = NormalizeForLooseSearch(fb.Lines[i]);
                if (LineMatchesLooseSearch(normalizedLine, normalizedAnchor))
                    return i;
            }

            for (int i = 0; i < fb.Cursor && i < fb.Lines.Count; i++)
            {
                string normalizedLine = NormalizeForLooseSearch(fb.Lines[i]);
                if (LineMatchesLooseSearch(normalizedLine, normalizedAnchor))
                    return i;
            }

            return -1;
        }

        private static bool LineMatchesLooseSearch(string normalizedLine, string normalizedAnchor)
        {
            if (string.IsNullOrWhiteSpace(normalizedLine) || string.IsNullOrWhiteSpace(normalizedAnchor))
                return false;

            if (normalizedLine.Contains(normalizedAnchor))
                return true;

            if (normalizedAnchor.Contains(normalizedLine) && normalizedLine.Length >= Math.Max(18, normalizedAnchor.Length / 2))
                return true;

            return AreLinesSimilar(normalizedLine, normalizedAnchor);
        }

        private static string BuildCSharpMemberDiagnostics(FileBuffer fb)
        {
            if (!IsCSharpFile(fb) || fb.Lines == null || fb.Lines.Count == 0)
                return string.Empty;

            var members = new List<string>();
            var regex = new Regex(@"^\s*(public|private|protected|internal)\s+(static\s+)?(async\s+)?[\w<>\[\],\s]+\s+(?<name>\w+)\s*\([^;]*\)\s*$");

            for (int i = 0; i < fb.Lines.Count; i++)
            {
                string line = fb.Lines[i] ?? string.Empty;
                if (!regex.IsMatch(line))
                    continue;

                string trimmed = line.Trim();
                if (trimmed.IndexOf("SetVolume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Tocar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    members.Add((i + 1) + ": " + trimmed);
                }
            }

            if (members.Count == 0)
            {
                for (int i = 0; i < fb.Lines.Count && members.Count < 12; i++)
                {
                    string line = fb.Lines[i] ?? string.Empty;
                    if (regex.IsMatch(line))
                        members.Add((i + 1) + ": " + line.Trim());
                }
            }

            if (members.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Membros C# encontrados no arquivo atual para referencia:");
            foreach (string member in members.Take(20))
                sb.AppendLine(member);
            sb.AppendLine("Observacao: se o metodo buscado nao existe, a IA deve inserir um novo metodo proximo a um membro real existente, nao substituir um SEARCH_BLOCK inexistente.");
            return sb.ToString();
        }

        private static string NormalizeForLooseSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Normalize(NormalizationForm.FormD);
            var chars = normalized
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Where(c => !char.IsWhiteSpace(c))
                .Select(char.ToUpperInvariant)
                .ToArray();

            return new string(chars).Normalize(NormalizationForm.FormC);
        }

        // ------------------ IO / Parsing helpers ------------------

        private void EnsureLoaded(Dictionary<string, FileBuffer> buffers, string path, bool createIfMissing)
        {
            if (buffers.ContainsKey(path))
                return;

            string resolvedPath = path;

            if (!File.Exists(resolvedPath))
            {
                if (!createIfMissing)
                {
                    if (!TryResolveExistingFileBySuffix(path, out resolvedPath, out string resolveError))
                        throw new FileNotFoundException(BuildMissingFileMessage(path, resolveError), path);

                    _log("[PATH] ARQ ajustado automaticamente: " + path + " -> " + MakeRelativePath(_baseDirectory, resolvedPath));
                }
            }

            if (!File.Exists(resolvedPath))
            {
                if (!createIfMissing)
                    throw new FileNotFoundException(BuildMissingFileMessage(path), path);

                buffers[path] = new FileBuffer(path, new List<string>());
                buffers[path].Changed = true;
                buffers[path].CreatedNewFile = true;
                _log($"[CREATE] Novo arquivo: {path}");
                return;
            }

            var content = File.ReadAllLines(resolvedPath).ToList();
            buffers[path] = new FileBuffer(resolvedPath, content);
            if (!string.Equals(resolvedPath, path, StringComparison.OrdinalIgnoreCase))
                buffers[resolvedPath] = buffers[path];
            _log($"[LOAD] {resolvedPath} ({content.Count} linhas)");
        }

        private string BuildMissingFileMessage(string path)
        {
            return BuildMissingFileMessage(path, null);
        }

        private string BuildMissingFileMessage(string path, string resolveError)
        {
            var sb = new StringBuilder();
            sb.Append("Arquivo nao encontrado para SEARCH/DELETE. A IA tentou alterar um arquivo que nao existe no projeto atual: ");
            sb.Append(path);

            if (!string.IsNullOrWhiteSpace(resolveError))
            {
                sb.Append(". ");
                sb.Append(resolveError);
            }

            string candidates = BuildMissingFileCandidates(path);
            if (!string.IsNullOrWhiteSpace(candidates))
                sb.Append(candidates);

            return sb.ToString();
        }

        private string BuildMissingFileCandidates(string missingPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_baseDirectory) || !Directory.Exists(_baseDirectory))
                    return string.Empty;

                string missingName = Path.GetFileName(missingPath) ?? string.Empty;
                string missingBase = Path.GetFileNameWithoutExtension(missingName) ?? string.Empty;
                string missingExt = Path.GetExtension(missingName) ?? string.Empty;

                var files = Directory.EnumerateFiles(_baseDirectory, "*" + missingExt, SearchOption.AllDirectories)
                    .Where(IsProjectSourceCandidate)
                    .Take(2000)
                    .ToList();

                var exactNameMatches = files
                    .Where(f => string.Equals(Path.GetFileName(f), missingName, StringComparison.OrdinalIgnoreCase))
                    .Select(ToRelativePath)
                    .Take(20)
                    .ToList();

                var scored = files
                    .Select(f => new
                    {
                        Path = f,
                        Relative = ToRelativePath(f),
                        Score = ScoreMissingFileCandidate(f, missingBase, missingExt)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Relative)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(25)
                    .ToList();

                if (exactNameMatches.Count == 0 && scored.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("Arquivos existentes candidatos no projeto:");

                foreach (string relative in exactNameMatches)
                    sb.AppendLine("- " + relative);

                foreach (string relative in scored)
                {
                    if (!exactNameMatches.Contains(relative, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine("- " + relative);
                }

                sb.AppendLine("Observacao: nao crie arquivo novo para SEARCH/DELETE. Use um dos arquivos existentes acima, ou selecione outro arquivo real do contexto.");
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool TryResolveExistingFileBySuffix(string path, out string resolvedPath, out string errorMessage)
        {
            resolvedPath = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalizedSuffix = NormalizeRelativePath(path);
            if (string.IsNullOrWhiteSpace(normalizedSuffix))
                return false;

            string directPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(_baseDirectory, path);

            if (File.Exists(directPath))
            {
                resolvedPath = Path.GetFullPath(directPath);
                return true;
            }

            string projectRoot = _baseDirectory;
            var candidates = new List<string>();

            foreach (string file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
            {
                if (!IsCandidateFileWithinProject(file, projectRoot))
                    continue;

                string relative = NormalizeRelativePath(MakeRelativePath(projectRoot, file));
                if (relative.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.GetFullPath(file));
            }

            candidates = candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 1)
            {
                resolvedPath = candidates[0];
                return true;
            }

            if (candidates.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append("Foram encontrados multiplos candidatos com o mesmo final:");
                foreach (string candidate in candidates.Take(10))
                    sb.AppendLine().Append("- ").Append(MakeRelativePath(projectRoot, candidate));
                sb.AppendLine();
                sb.Append("Use o caminho completo relativo ao projeto.");
                errorMessage = sb.ToString();
            }

            return false;
        }

        private static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static bool IsCandidateFileWithinProject(string file, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(projectRoot))
                return false;

            if (!IsPathUnderRoot(file, projectRoot))
                return false;

            string relative = MakeRelativePath(projectRoot, file);
            if (string.IsNullOrWhiteSpace(relative))
                return false;

            string[] ignored = { @"bin\", @"obj\", @".git\", @".vs\", @"packages\" };
            foreach (string folder in ignored)
            {
                if (relative.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return true;
        }

        private static bool IsPathUnderRoot(string file, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(projectRoot))
                return false;

            string fullFile = Path.GetFullPath(file);
            string fullRoot = Path.GetFullPath(projectRoot);

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullRoot += Path.DirectorySeparatorChar;

            return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProjectSourceCandidate(string path)
        {
            string relative = ToRelativePath(path).Replace('\\', '/');
            return !relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                   !relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) &&
                   !relative.Contains("/bin/") &&
                   !relative.Contains("/obj/") &&
                   !relative.Contains("/packages/") &&
                   !relative.Contains("/node_modules/") &&
                   !relative.Contains("/vendor/");
        }

        private int ScoreMissingFileCandidate(string path, string missingBase, string missingExt)
        {
            int score = 0;
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            string relative = ToRelativePath(path).Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(missingBase))
            {
                if (name.IndexOf(missingBase, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    missingBase.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;

                foreach (string token in SplitNameTokens(missingBase))
                {
                    if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        relative.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 8;
                }
            }

            if (string.Equals(missingExt, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (LooksLikeWinFormsForm(path))
                    score += 30;

                if (relative.IndexOf("Form", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    relative.IndexOf("Views/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    relative.IndexOf("UI/", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            }

            return score;
        }

        private static IEnumerable<string> SplitNameTokens(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                yield break;

            foreach (Match match in Regex.Matches(name, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
            {
                string token = match.Value;
                if (token.Length >= 3)
                    yield return token;
            }
        }

        private static bool LooksLikeWinFormsForm(string path)
        {
            try
            {
                foreach (string line in File.ReadLines(path).Take(120))
                {
                    if (line.IndexOf(": Form", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf(": System.Windows.Forms.Form", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private string ToRelativePath(string path)
        {
            try
            {
                Uri root = new Uri(AppendDirectorySeparator(Path.GetFullPath(_baseDirectory)));
                Uri file = new Uri(Path.GetFullPath(path));
                return Uri.UnescapeDataString(root.MakeRelativeUri(file).ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        private void MakeBackup(string path)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileName(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backup = Path.Combine(dir, $"{name}.bak.{stamp}");

            File.Copy(path, backup, true);
            _log($"[BAK] Backup criado: {backup}");
        }

        private string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            string combined = Path.GetFullPath(Path.Combine(_baseDirectory, normalized));
            string withoutRepeatedRoot = RemoveRepeatedRootDirectory(normalized);

            if (!string.Equals(withoutRepeatedRoot, normalized, StringComparison.OrdinalIgnoreCase))
            {
                string alternative = Path.GetFullPath(Path.Combine(_baseDirectory, withoutRepeatedRoot));
                if (File.Exists(alternative) || !File.Exists(combined))
                {
                    _log("[WARN] ARQ continha a pasta raiz repetida. Usando: " + alternative);
                    return alternative;
                }
            }

            return combined;
        }

        private string RemoveRepeatedRootDirectory(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(_baseDirectory))
                return relativePath;

            string rootName = Path.GetFileName(_baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rootName))
                return relativePath;

            string prefix = rootName + Path.DirectorySeparatorChar;
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return relativePath.Substring(prefix.Length);

            return relativePath;
        }

        private static List<string> SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        }

        private static bool StartsWithCmd(string line, string cmd)
        {
            return line != null && line.StartsWith(cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualsCmd(string line, string cmd)
        {
            return string.Equals(line?.Trim(), cmd, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReplaceBlockMarker(string line)
        {
            var trimmed = (line ?? "").Trim();
            if (EqualsCmd(trimmed, "REPLACE_BLOCK"))
                return true;

            if (!StartsWithCmd(trimmed, "REPLACE_BLOCK="))
                return false;

            var value = trimmed.Substring("REPLACE_BLOCK=".Length).Trim();
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIgnorableNoiseLine(string line)
        {
            var trimmed = (line ?? "").TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
                return true;

            return trimmed.StartsWith("#", StringComparison.Ordinal) ||
                   trimmed.StartsWith("//", StringComparison.Ordinal) ||
                   trimmed.StartsWith("///", StringComparison.Ordinal) ||
                   trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                   trimmed.StartsWith("*/", StringComparison.Ordinal) ||
                   trimmed.StartsWith(";", StringComparison.Ordinal) ||
                   trimmed.StartsWith("-", StringComparison.Ordinal) ||
                   trimmed.StartsWith("*", StringComparison.Ordinal) ||
                   trimmed.StartsWith("```", StringComparison.Ordinal) ||
                   trimmed.StartsWith(">", StringComparison.Ordinal);
        }

        private List<string> ReadBlock(List<string> lines, ref int index, string endMarker, bool allowEndOfInput = false)
        {
            var block = new List<string>();

            while (index < lines.Count)
            {
                var cur = lines[index].TrimEnd();
                if (IsEndMarker(cur.Trim(), endMarker))
                {
                    index++; // consome o marcador
                    return block;
                }

                block.Add(lines[index]); // preserva exatamente como veio (com espaços)
                index++;
            }

            if (allowEndOfInput)
            {
                _log($"[WARN] Bloco sem '{endMarker}'. Usando fim do protocolo como fechamento implicito.");
                return block;
            }

            throw new Exception($"Bloco não terminou. Faltou '{endMarker}'.");
        }

        private List<string> ReadSearchBlock(List<string> lines, ref int index)
        {
            var block = new List<string>();

            while (index < lines.Count)
            {
                var cur = lines[index].TrimEnd();
                var trimmed = cur.Trim();

                if (IsEndMarker(trimmed, "END_SEARCH"))
                {
                    index++;
                    return block;
                }

                if (EqualsCmd(trimmed, "REPLACE_BLOCK") || EqualsCmd(trimmed, "DELETE_BLOCK"))
                {
                    _log("[WARN] SEARCH_BLOCK sem END_SEARCH. Usando marcador de acao como fechamento implicito.");
                    return block;
                }

                block.Add(lines[index]);
                index++;
            }

            throw new Exception("Bloco não terminou. Faltou 'END_SEARCH'.");
        }

        private List<string> ReadInlineSearchBlock(List<string> lines, ref int index, string firstLineContent)
        {
            var block = new List<string> { firstLineContent ?? string.Empty };

            while (index + 1 < lines.Count)
            {
                index++;
                var cur = lines[index].TrimEnd();
                var trimmed = cur.Trim();

                if (IsEndMarker(trimmed, "END_SEARCH"))
                    return block;

                if (EqualsCmd(trimmed, "REPLACE_BLOCK") ||
                    StartsWithCmd(trimmed, "REPLACE_BLOCK=") ||
                    EqualsCmd(trimmed, "DELETE_BLOCK"))
                {
                    index--;
                    _log("[WARN] SEARCH_BLOCK= sem END_SEARCH. Usando marcador de acao como fechamento implicito.");
                    return block;
                }

                block.Add(lines[index]);
            }

            return block;
        }

        private List<string> ReadInlineReplaceBlock(List<string> lines, ref int index, string firstLineContent)
        {
            var block = new List<string> { firstLineContent ?? string.Empty };

            while (index + 1 < lines.Count)
            {
                index++;
                var cur = lines[index].TrimEnd();
                if (IsEndMarker(cur.Trim(), "END_REPLACE"))
                    return block;

                block.Add(lines[index]);
            }

            _log("[WARN] REPLACE_BLOCK= sem END_REPLACE. Usando fim do protocolo como fechamento implicito.");
            return block;
        }

        private static string ReadWholeFileReplacement(List<string> lines, ref int index, string firstLineContent)
        {
            var contentLines = new List<string> { firstLineContent ?? string.Empty };

            int next = index + 1;
            while (next < lines.Count)
            {
                var candidate = lines[next];
                if (IsCommandBoundary(candidate))
                    break;

                contentLines.Add(candidate);
                next++;
            }

            index = next;
            return string.Join(Environment.NewLine, contentLines);
        }

        private static bool IsCommandBoundary(string line)
        {
            if (line == null)
                return false;

            var trimmed = line.TrimStart();
            return StartsWithCmd(trimmed, "ARQ=") ||
                   StartsWithCmd(trimmed, "SEARCH=") ||
                   StartsWithCmd(trimmed, "SEARCH_BLOCK=") ||
                   StartsWithCmd(trimmed, "REPLACE=") ||
                   EqualsCmd(trimmed, "DELETE") ||
                   EqualsCmd(trimmed, "SEARCH_BLOCK") ||
                   StartsWithCmd(trimmed, "SEARCH_BLOCK=") ||
                   EqualsCmd(trimmed, "REPLACE_BLOCK") ||
                   StartsWithCmd(trimmed, "REPLACE_BLOCK=") ||
                   EqualsCmd(trimmed, "DELETE_BLOCK") ||
                   IsEndMarker(trimmed, "END_SEARCH") ||
                   IsEndMarker(trimmed, "END_REPLACE");
        }

        private static bool IsSearchActionMarker(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.TrimStart();
            return StartsWithCmd(trimmed, "REPLACE=") ||
                   StartsWithCmd(trimmed, "REPLACE_BLOCK=") ||
                   IsReplaceBlockMarker(trimmed) ||
                   EqualsCmd(trimmed, "DELETE");
        }

        private static bool IsEndMarker(string line, string expected)
        {
            if (EqualsCmd(line, expected))
                return true;

            if (EqualsCmd(expected, "END_SEARCH"))
                return EqualsCmd(line, "END_SEARCH_BLOCK");

            if (EqualsCmd(expected, "END_REPLACE"))
                return EqualsCmd(line, "END_REPLACE_BLOCK");

            return false;
        }

        private void EnsureProjectIncludesNewSource(string sourcePath, bool makeBackup)
        {
            var csprojPath = FindProjectFileForSource(sourcePath);
            if (string.IsNullOrWhiteSpace(csprojPath))
            {
                _log($"[CSPROJ] Nenhum .csproj encontrado para incluir {sourcePath}");
                return;
            }

            if (!File.Exists(csprojPath))
            {
                _log($"[CSPROJ] .csproj nao encontrado: {csprojPath}");
                return;
            }

            var projectText = File.ReadAllText(csprojPath);
            if (IsSdkStyleProject(projectText))
            {
                _log($"[CSPROJ] Projeto SDK-style detectado, sem ajuste automatico: {csprojPath}");
                return;
            }

            var csprojDir = Path.GetDirectoryName(csprojPath) ?? _baseDirectory;
            var relativeSource = MakeRelativePath(csprojDir, sourcePath);
            if (string.IsNullOrWhiteSpace(relativeSource))
            {
                _log($"[CSPROJ] Nao foi possivel calcular caminho relativo para {sourcePath}");
                return;
            }

            var normalizedRelative = relativeSource.Replace('/', '\\');
            if (ProjectAlreadyContainsCompile(projectText, normalizedRelative))
            {
                _log($"[CSPROJ] Ja referenciado: {normalizedRelative}");
                return;
            }

            var updatedText = InsertCompileInclude(projectText, normalizedRelative);
            if (string.IsNullOrWhiteSpace(updatedText) || string.Equals(updatedText, projectText, StringComparison.Ordinal))
            {
                _log($"[CSPROJ] Nenhuma alteracao aplicada em {csprojPath}");
                return;
            }

            if (makeBackup)
                MakeBackup(csprojPath);

            File.WriteAllText(csprojPath, updatedText);
            _log($"[CSPROJ] Atualizado: {csprojPath} (+ {normalizedRelative})");
        }

        private string FindProjectFileForSource(string sourcePath)
        {
            try
            {
                var currentDir = Path.GetDirectoryName(sourcePath);
                var baseDir = Path.GetFullPath(_baseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                while (!string.IsNullOrWhiteSpace(currentDir))
                {
                    var normalizedCurrent = Path.GetFullPath(currentDir)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    var localCsproj = Directory.GetFiles(currentDir, "*.csproj", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(localCsproj))
                        return localCsproj;

                    if (string.Equals(normalizedCurrent, baseDir, StringComparison.OrdinalIgnoreCase))
                        break;

                    var parent = Directory.GetParent(currentDir);
                    if (parent == null)
                        break;

                    currentDir = parent.FullName;
                }

                return Directory.GetFiles(_baseDirectory, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSdkStyleProject(string projectText)
        {
            if (string.IsNullOrWhiteSpace(projectText))
                return false;

            return projectText.IndexOf("<Project Sdk=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ProjectAlreadyContainsCompile(string projectText, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(projectText) || string.IsNullOrWhiteSpace(relativeSource))
                return false;

            var normalizedProject = projectText.Replace('/', '\\');
            var needle = $"<Compile Include=\"{relativeSource}\"";
            return normalizedProject.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string InsertCompileInclude(string projectText, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(projectText) || string.IsNullOrWhiteSpace(relativeSource))
                return projectText;

            var insert = $"  <ItemGroup>{Environment.NewLine}    <Compile Include=\"{relativeSource}\" />{Environment.NewLine}  </ItemGroup>{Environment.NewLine}";
            var marker = "</Project>";
            var index = projectText.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return projectText;

            return projectText.Insert(index, insert);
        }

        private void RemoveDuplicateCompileIncludes(string csprojPath)
        {
            if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
                return;

            var lines = File.ReadAllLines(csprojPath).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = 0;

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var include = ExtractCompileInclude(lines[i]);
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                var normalized = include.Replace('/', '\\').Trim();
                if (seen.Add(normalized))
                    continue;

                lines.RemoveAt(i);
                removed++;
            }

            if (removed <= 0)
                return;

            File.WriteAllLines(csprojPath, lines);
            _log($"[CSPROJ] Compile Include duplicado removido: {removed}");
        }

        private static string ExtractCompileInclude(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var compileIndex = line.IndexOf("<Compile", StringComparison.OrdinalIgnoreCase);
            if (compileIndex < 0)
                return null;

            var includeIndex = line.IndexOf("Include=", compileIndex, StringComparison.OrdinalIgnoreCase);
            if (includeIndex < 0)
                return null;

            var quoteIndex = includeIndex + "Include=".Length;
            while (quoteIndex < line.Length && char.IsWhiteSpace(line[quoteIndex]))
                quoteIndex++;

            if (quoteIndex >= line.Length)
                return null;

            var quote = line[quoteIndex];
            if (quote != '"' && quote != '\'')
                return null;

            var start = quoteIndex + 1;
            var end = line.IndexOf(quote, start);
            if (end <= start)
                return null;

            return line.Substring(start, end - start);
        }

        private static string MakeRelativePath(string fromPath, string toPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fromPath) || string.IsNullOrWhiteSpace(toPath))
                    return null;

                var fromFull = Path.GetFullPath(fromPath);
                var toFull = Path.GetFullPath(toPath);

                if (!fromFull.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                    !fromFull.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    fromFull += Path.DirectorySeparatorChar;
                }

                var fromUri = new Uri(fromFull, UriKind.Absolute);
                var toUri = new Uri(toFull, UriKind.Absolute);
                var relativeUri = fromUri.MakeRelativeUri(toUri);
                return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }

        private sealed class FileBuffer
        {
            public string Path { get; }
            public List<string> Lines { get; }
            public int Cursor { get; set; } = 0;
            public bool Changed { get; set; } = false;
            public bool CreatedNewFile { get; set; } = false;

            public FileBuffer(string path, List<string> lines)
            {
                Path = path;
                Lines = lines;
            }
        }

        private enum PatchOperationKind
        {
            WholeFileReplace,
            SearchReplace,
            SearchDelete,
            BlockReplace,
            BlockDelete
        }

        private sealed class PatchPlan
        {
            public IReadOnlyList<PatchOperation> Operations { get; }

            public PatchPlan(IReadOnlyList<PatchOperation> operations)
            {
                Operations = operations ?? new List<PatchOperation>();
            }
        }

        private sealed class PatchOperation
        {
            public PatchOperationKind Kind { get; private set; }
            public string FilePath { get; private set; }
            public string OperationType { get; private set; }
            public string SearchPreview { get; private set; }
            public string SearchText { get; private set; }
            public string ReplaceText { get; private set; }
            public List<string> SearchBlock { get; private set; }
            public List<string> ReplaceBlock { get; private set; }
            public bool CreatesFileIfMissing { get; private set; }

            public static PatchOperation ForWholeFileReplace(string filePath, string replaceText, string operationType, string searchPreview)
            {
                return new PatchOperation
                {
                    Kind = PatchOperationKind.WholeFileReplace,
                    FilePath = filePath,
                    ReplaceText = replaceText,
                    OperationType = operationType,
                    SearchPreview = searchPreview,
                    CreatesFileIfMissing = true
                };
            }

            public static PatchOperation ForSearchReplace(string filePath, string searchText, string replaceText, string operationType, string searchPreview)
            {
                return new PatchOperation
                {
                    Kind = PatchOperationKind.SearchReplace,
                    FilePath = filePath,
                    SearchText = searchText,
                    ReplaceText = replaceText,
                    OperationType = operationType,
                    SearchPreview = searchPreview
                };
            }

            public static PatchOperation ForSearchDelete(string filePath, string searchText, string operationType, string searchPreview)
            {
                return new PatchOperation
                {
                    Kind = PatchOperationKind.SearchDelete,
                    FilePath = filePath,
                    SearchText = searchText,
                    OperationType = operationType,
                    SearchPreview = searchPreview
                };
            }

            public static PatchOperation ForBlockReplace(string filePath, List<string> searchBlock, List<string> replaceBlock, string operationType, string searchPreview)
            {
                return new PatchOperation
                {
                    Kind = PatchOperationKind.BlockReplace,
                    FilePath = filePath,
                    SearchBlock = searchBlock ?? new List<string>(),
                    ReplaceBlock = replaceBlock ?? new List<string>(),
                    OperationType = operationType,
                    SearchPreview = searchPreview
                };
            }

            public static PatchOperation ForBlockDelete(string filePath, List<string> searchBlock, string operationType, string searchPreview)
            {
                return new PatchOperation
                {
                    Kind = PatchOperationKind.BlockDelete,
                    FilePath = filePath,
                    SearchBlock = searchBlock ?? new List<string>(),
                    OperationType = operationType,
                    SearchPreview = searchPreview
                };
            }

            public string GetDuplicateAnchorKey()
            {
                if (Kind == PatchOperationKind.SearchReplace || Kind == PatchOperationKind.SearchDelete)
                {
                    string anchor = NormalizeAnchor(SearchText);
                    return string.IsNullOrWhiteSpace(anchor) ? null : (FilePath ?? string.Empty) + "::SEARCH::" + anchor;
                }

                if (Kind == PatchOperationKind.BlockReplace || Kind == PatchOperationKind.BlockDelete)
                {
                    string anchor = NormalizeAnchor(string.Join("\n", SearchBlock ?? new List<string>()));
                    return string.IsNullOrWhiteSpace(anchor) ? null : (FilePath ?? string.Empty) + "::SEARCH_BLOCK::" + anchor;
                }

                return null;
            }

            public IEnumerable<string> GetValidationLines()
            {
                if (!string.IsNullOrWhiteSpace(SearchText))
                    yield return SearchText;

                if (!string.IsNullOrWhiteSpace(ReplaceText))
                    yield return ReplaceText;

                if (SearchBlock != null)
                {
                    foreach (string line in SearchBlock)
                        yield return line;
                }

                if (ReplaceBlock != null)
                {
                    foreach (string line in ReplaceBlock)
                        yield return line;
                }
            }

            private static string NormalizeAnchor(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return string.Empty;

                return text
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .Trim();
            }
        }

        private enum PatchValidationSeverity
        {
            Info,
            Warning,
            Error
        }

        private sealed class PatchValidationIssue
        {
            public PatchValidationSeverity Severity { get; }
            public string Code { get; }
            public string Message { get; }
            public string FilePath { get; }
            public int OperationIndex { get; }
            public string Preview { get; }

            public PatchValidationIssue(PatchValidationSeverity severity, string code, string message, string filePath, int operationIndex, string preview)
            {
                Severity = severity;
                Code = code ?? string.Empty;
                Message = message ?? string.Empty;
                FilePath = filePath ?? string.Empty;
                OperationIndex = operationIndex;
                Preview = preview ?? string.Empty;
            }
        }

        private sealed class PatchValidationResult
        {
            public List<PatchValidationIssue> Issues { get; } = new List<PatchValidationIssue>();
            public bool IsValid => Issues.All(issue => issue.Severity != PatchValidationSeverity.Error);
        }

        private enum PatchFailureKind
        {
            None,
            InventedAnchor,
            DuplicateAnchor,
            FragmentedPatch,
            WrongLanguage,
            InvalidFormat,
            EmptyAnchor,
            EmptyReplace,
            WeakAnchor,
            Idempotent,
            PreflightFailed,
            Unknown
        }

        private sealed class PatchFailureClassification
        {
            public PatchFailureKind Kind { get; }
            public string Code { get; }
            public string Title { get; }
            public string Message { get; }
            public string FilePath { get; }
            public int OperationIndex { get; }
            public string SearchPreview { get; }
            public string HintForRetry { get; }
            public bool ShouldRetry { get; }
            public bool WasFileModified { get; }

            public PatchFailureClassification(
                PatchFailureKind kind,
                string code,
                string title,
                string message,
                string filePath,
                int operationIndex,
                string searchPreview,
                string hintForRetry,
                bool shouldRetry,
                bool wasFileModified)
            {
                Kind = kind;
                Code = code ?? string.Empty;
                Title = title ?? string.Empty;
                Message = message ?? string.Empty;
                FilePath = filePath ?? string.Empty;
                OperationIndex = operationIndex;
                SearchPreview = searchPreview ?? string.Empty;
                HintForRetry = hintForRetry ?? string.Empty;
                ShouldRetry = shouldRetry;
                WasFileModified = wasFileModified;
            }
        }
    }
}
