using System;
using System.Collections.Generic;
using System.Text.Json;
using TSMapEditor.Mutations.Classes;

namespace TSMapEditor.AI
{
    /// <summary>
    /// A single alias entry mapping a Chinese name to a canonical INI code.
    /// </summary>
    public sealed class AIUnitAliasMatch
    {
        public string Alias { get; }
        public string Code { get; }
        public string Type { get; }
        public string Description { get; }

        public AIUnitAliasMatch(string alias, string code, string type, string description)
        {
            Alias = alias ?? string.Empty;
            Code = code ?? string.Empty;
            Type = type ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>
    /// Resolves Chinese unit aliases to canonical INI codes.
    /// Pure logic — no Map or UI dependencies.
    /// </summary>
    public static class AIUnitAliasResolver
    {
        /// <summary>
        /// Parses the alias JSON and returns a list of alias entries.
        /// </summary>
        public static IReadOnlyList<AIUnitAliasMatch> LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<AIUnitAliasMatch>();

            var result = new List<AIUnitAliasMatch>();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("aliases", out var aliasArray))
                return result;

            foreach (var item in aliasArray.EnumerateArray())
            {
                string alias = item.TryGetProperty("alias", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                string code = item.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                string type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                string desc = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

                if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(code))
                    result.Add(new AIUnitAliasMatch(alias, code, type, desc));
            }

            return result;
        }

        /// <summary>
        /// Finds all aliases matching the given keyword (substring match).
        /// Returns multiple results for broad terms like "坦克".
        /// </summary>
        public static IReadOnlyList<AIUnitAliasMatch> FindMatches(IEnumerable<AIUnitAliasMatch> aliases, string keyword)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(keyword))
                return Array.Empty<AIUnitAliasMatch>();

            var results = new List<AIUnitAliasMatch>();
            foreach (var entry in aliases)
            {
                // Match if keyword is contained in alias, code, or description
                if (entry.Alias.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.Code.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.Description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(entry);
                }
            }
            return results;
        }

        /// <summary>
        /// Resolves an exact alias to a canonical INI code, respecting object type.
        /// Returns null for broad generic terms (where multiple aliases match the exact keyword),
        /// unknown aliases, or type mismatches.
        /// </summary>
        public static string ResolveExact(IEnumerable<AIUnitAliasMatch> aliases, string name, AIPlaceObjectType objectType)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(name))
                return null;

            string expectedType = objectType switch
            {
                AIPlaceObjectType.Infantry => "Infantry",
                AIPlaceObjectType.Vehicle => "Vehicle",
                AIPlaceObjectType.Building => "Building",
                _ => null
            };

            if (expectedType == null)
                return null;

            // Find all entries where alias matches exactly
            var exactMatches = new List<AIUnitAliasMatch>();
            foreach (var entry in aliases)
            {
                if (string.Equals(entry.Alias, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Alias, name, StringComparison.Ordinal))
                {
                    exactMatches.Add(entry);
                }
            }

            if (exactMatches.Count == 0)
                return null;

            // Filter by type
            var typeMatches = new List<AIUnitAliasMatch>();
            foreach (var m in exactMatches)
            {
                if (string.Equals(m.Type, expectedType, StringComparison.OrdinalIgnoreCase))
                    typeMatches.Add(m);
            }

            // If exactly one type-filtered match, return it
            if (typeMatches.Count == 1)
                return typeMatches[0].Code;

            // Multiple type matches with same code → return that code
            if (typeMatches.Count > 1)
            {
                string firstCode = typeMatches[0].Code;
                bool allSame = true;
                for (int i = 1; i < typeMatches.Count; i++)
                {
                    if (!string.Equals(typeMatches[i].Code, firstCode, StringComparison.OrdinalIgnoreCase))
                    {
                        allSame = false;
                        break;
                    }
                }
                if (allSame) return firstCode;
            }

            // Multiple matches with different codes → ambiguous, return null
            return null;
        }
    }
}
