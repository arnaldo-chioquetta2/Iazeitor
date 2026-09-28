using System;
using System.Collections.Generic;
using System.Linq;

namespace GptBolDll
{
    public static class ProviderSelector
    {
        public static AiProviderConfig Select(List<AiProviderConfig> providers, string projetoProviderId, AiProviderConfig providerAtual)
        {
            if (providers == null || providers.Count == 0)
                return null;

            string projetoProviderIdEfetivo = (projetoProviderId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(projetoProviderIdEfetivo))
            {
                var porId = providers.FirstOrDefault(p => string.Equals(p.Id, projetoProviderIdEfetivo, StringComparison.OrdinalIgnoreCase));
                if (porId != null)
                    return porId;
            }

            if (providerAtual != null && !string.IsNullOrWhiteSpace(providerAtual.Id))
            {
                var porIdPersistido = providers.FirstOrDefault(p => string.Equals(p.Id, providerAtual.Id, StringComparison.OrdinalIgnoreCase));
                if (porIdPersistido != null)
                    return porIdPersistido;
            }

            if (providerAtual != null)
            {
                var porTipoModelo = providers.FirstOrDefault(p =>
                    string.Equals(p.ProviderType, providerAtual.ProviderType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ModelName, providerAtual.ModelName, StringComparison.OrdinalIgnoreCase));
                if (porTipoModelo != null)
                    return porTipoModelo;

                var porNomeModelo = providers.FirstOrDefault(p =>
                    string.Equals(p.Nome, providerAtual.Nome, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ModelName, providerAtual.ModelName, StringComparison.OrdinalIgnoreCase));
                if (porNomeModelo != null)
                    return porNomeModelo;

                var porNome = providers.FirstOrDefault(p => string.Equals(p.Nome, providerAtual.Nome, StringComparison.OrdinalIgnoreCase));
                if (porNome != null)
                    return porNome;
            }

            return providers.FirstOrDefault(p => p.Ativo) ?? providers.FirstOrDefault();
        }
    }
}