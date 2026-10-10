using System;

namespace GptBolDll
{
    public static class ProviderTypeHelper
    {
        public static string Normalize(string providerType)
        {
            if (providerType == null)
                return null;

            string tipo = providerType.Trim();
            if (string.Equals(tipo, "Grog", StringComparison.OrdinalIgnoreCase))
                return "Groq";
            if (string.Equals(tipo, "Mixtral", StringComparison.OrdinalIgnoreCase))
                return "Mistral";
            return tipo;
        }

        public static bool Equals(string providerType, string esperado)
        {
            return string.Equals(providerType, esperado, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGemini(string providerType)
        {
            return Equals(providerType, "Gemini");
        }

        public static bool IsOpenAI(string providerType)
        {
            return Equals(providerType, "OpenAI");
        }

        public static bool IsKimiOfficial(string providerType)
        {
            return Equals(providerType, AiProviderConfig.KimiOfficialProviderType);
        }

        public static bool IsKimiOpenAICompatible(string providerType)
        {
            return Equals(providerType, AiProviderConfig.KimiProviderType);
        }

        public static bool IsKimi(string providerType)
        {
            return !string.IsNullOrWhiteSpace(providerType) &&
                   providerType.IndexOf("Kimi", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsGroq(string providerType)
        {
            return Equals(providerType, "Groq") || Equals(providerType, "Grog");
        }

        public static bool IsMistral(string providerType)
        {
            return Equals(providerType, "Mistral") || Equals(providerType, "Mixtral");
        }

        public static bool IsDeepSeek(string providerType)
        {
            return Equals(providerType, "DeepSeek");
        }
    }
}