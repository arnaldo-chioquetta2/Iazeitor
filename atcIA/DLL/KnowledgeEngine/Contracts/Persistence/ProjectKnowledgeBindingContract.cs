using System;
using System.Collections.Generic;
using Iazeitor.KnowledgeEngine.Contracts.Shared;

namespace Iazeitor.KnowledgeEngine.Contracts.Persistence
{
    /// <summary>
    /// Associacao persistente entre a identidade estavel de um projeto e os
    /// Knowledge Packs que pertencem a ele.
    ///
    /// Nao e um manifest de Pack: nao guarda Identity/Content/versao. Tambem nao
    /// persiste a KnowledgeSession (Demand, Discoveries, Evidences). A chave
    /// primaria e o ProjectId estavel e a associacao e uma LISTA, pois o modelo
    /// admite mais de um Pack por projeto.
    /// </summary>
    public sealed class ProjectKnowledgeBindingContract
    {
        /// <summary>Versao do formato DESTE arquivo de binding (nao dos Packs).</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }

        public string ProjectId { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public IReadOnlyList<KnowledgeIdentifier> PackIds { get; set; }

        /// <summary>
        /// Normaliza a identidade do projeto para a forma canonica usada como chave
        /// do binding. Somente a identidade estavel persistida do projeto (Guid) e
        /// aceita: nome, path, hash de codigo e heuristica nunca sao usados.
        /// </summary>
        public static bool TryNormalizeProjectId(string projectId, out string normalized)
        {
            normalized = null;
            if (String.IsNullOrWhiteSpace(projectId))
            {
                return false;
            }

            Guid parsed;
            if (!Guid.TryParse(projectId.Trim(), out parsed))
            {
                return false;
            }

            normalized = parsed.ToString("D").ToLowerInvariant();
            return true;
        }
    }
}
