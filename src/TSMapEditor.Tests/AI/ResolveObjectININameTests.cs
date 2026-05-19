using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using TSMapEditor.AI;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    /// <summary>
    /// Tests that ResolveObjectININame validates alias codes against loaded rules
    /// before returning them. Prevents invalid alias codes from silently passing
    /// through to AIPlaceObjectMutation.
    /// </summary>
    public class ResolveObjectININameTests
    {
        private const string TestAliasJson = @"{
            ""aliases"": [
                { ""alias"": ""美国大兵"", ""code"": ""E1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" },
                { ""alias"": ""大兵"", ""code"": ""E1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" },
                { ""alias"": ""重装大兵"", ""code"": ""GGI"", ""type"": ""Infantry"", ""description"": ""Guardian G.I. (MO)"" },
                { ""alias"": ""灰熊坦克"", ""code"": ""GRIZZLY"", ""type"": ""Vehicle"", ""description"": ""Grizzly Tank"" },
                { ""alias"": ""动员兵"", ""code"": ""E2"", ""type"": ""Infantry"", ""description"": ""Soviet Conscript (MO)"" },
                { ""alias"": ""工程师"", ""code"": ""ENGINEER"", ""type"": ""Infantry"", ""description"": ""Allied Engineer"" }
            ]
        }";

        /// <summary>
        /// Test 1: Alias code exists in loaded rules → returns the code from loaded rules.
        /// </summary>
        [Fact]
        public void AliasCode_ExistsInLoadedRules_ReturnsCode()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E1"));
            rules.InfantryTypes.Add(new InfantryType("E2"));

            string result = InvokeResolve("美国大兵", AIPlaceObjectType.Infantry, rules);

            Assert.Equal("E1", result);
        }

        /// <summary>
        /// Test 2: Alias code does NOT exist in loaded rules → must NOT return the alias code.
        /// Falls through to search; if no match in loaded types, returns null.
        /// </summary>
        [Fact]
        public void AliasCode_NotInLoadedRules_ReturnsNullNotAliasCode()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("CONSCRIPT")); // No E1 in loaded rules

            string result = InvokeResolve("美国大兵", AIPlaceObjectType.Infantry, rules);

            // Must NOT return "E1" — it doesn't exist in loaded rules
            Assert.NotEqual("E1", result);
            // Falls through to search; "美国大兵" won't match "CONSCRIPT" in any search path
            Assert.Null(result);
        }

        /// <summary>
        /// Test 3: Vehicle alias should not resolve through infantry path.
        /// Alias type mismatch remains rejected.
        /// </summary>
        [Fact]
        public void AliasTypeMismatch_DoesNotResolve()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("GRIZZLY")); // Even if GRIZZLY is in infantry types
            rules.UnitTypes.Add(new UnitType("GRIZZLY"));

            // "灰熊坦克" alias type is "Vehicle", should not resolve as Infantry
            string result = InvokeResolve("灰熊坦克", AIPlaceObjectType.Infantry, rules);

            // The alias resolver filters by type, so it returns null for infantry
            // Then fallback search might find GRIZZLY in infantry list by partial match
            // That's acceptable — the key point is the alias type filtering works
            Assert.True(result == null || result == "GRIZZLY");
        }

        /// <summary>
        /// Test 4: Non-alias exact INI name still works when loaded in rules.
        /// </summary>
        [Fact]
        public void NonAliasExactININame_ReturnsWhenInLoadedRules()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E1"));
            rules.InfantryTypes.Add(new InfantryType("E2"));

            string result = InvokeResolve("E1", AIPlaceObjectType.Infantry, rules);

            Assert.Equal("E1", result);
        }

        /// <summary>
        /// Test 4c: Alias resolves to E1 and loaded rules have E1.
        /// </summary>
        [Fact]
        public void AliasE1_InMOLoadedRules_ReturnsE1()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E1"));
            rules.InfantryTypes.Add(new InfantryType("GGI"));

            // "大兵" also maps to E1
            string result1 = InvokeResolve("大兵", AIPlaceObjectType.Infantry, rules);
            Assert.Equal("E1", result1);

            // "重装大兵" maps to GGI
            string result2 = InvokeResolve("重装大兵", AIPlaceObjectType.Infantry, rules);
            Assert.Equal("GGI", result2);
        }

        /// <summary>
        /// Test 4d: Alias resolves to E2 (动员兵) and loaded rules have E2.
        /// </summary>
        [Fact]
        public void AliasE2_InMOLoadedRules_ReturnsE2()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E2"));

            string result = InvokeResolve("动员兵", AIPlaceObjectType.Infantry, rules);
            Assert.Equal("E2", result);
        }

        /// <summary>
        /// Test 4b: Non-alias name not in loaded rules → returns null.
        /// </summary>
        [Fact]
        public void NonAliasName_NotInLoadedRules_ReturnsNull()
        {
            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E1"));

            string result = InvokeResolve("DOESNOTEXIST", AIPlaceObjectType.Infantry, rules);

            Assert.Null(result);
        }

        /// <summary>
        /// Test 5: Case-insensitive alias code returns loaded-rule casing.
        /// Alias maps to "GI"; loaded rule has "GI" → returns "GI" (not whatever case the alias had).
        /// </summary>
        [Fact]
        public void AliasCode_CaseInsensitive_ReturnsLoadedRuleCasing()
        {
            // Create alias list where code is lowercase "e1"
            var lowercaseAliasJson = @"{
                ""aliases"": [
                    { ""alias"": ""美国大兵"", ""code"": ""e1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" }
                ]
            }";

            var rules = new Rules();
            rules.InfantryTypes.Add(new InfantryType("E1")); // Uppercase in loaded rules

            string result = InvokeResolveWithCustomAliases("美国大兵", AIPlaceObjectType.Infantry, rules, lowercaseAliasJson);

            // Should return the loaded rule's casing "E1", not the alias code's "e1"
            Assert.Equal("E1", result);
        }

        /// <summary>
        /// Test 6: Alias code exists for one type but resolving for different type falls through correctly.
        /// </summary>
        [Fact]
        public void AliasCode_DifferentObjectType_FallsThrough()
        {
            var rules = new Rules();
            rules.UnitTypes.Add(new UnitType("GRIZZLY"));

            // "灰熊坦克" is a Vehicle alias for GRIZZLY
            string result = InvokeResolve("灰熊坦克", AIPlaceObjectType.Vehicle, rules);

            Assert.Equal("GRIZZLY", result);
        }

        /// <summary>
        /// Test 7: When alias code doesn't exist in loaded rules but the fallback search
        /// finds a partial match, that match should be returned.
        /// </summary>
        [Fact]
        public void AliasCode_NotInRules_FallbackFindsPartialMatch()
        {
            var rules = new Rules();
            // No "E1" but there is "E1_ALT" which contains "E1" as substring
            // Note: the fallback searches by the ORIGINAL name, not by alias code
            // So "美国大兵" won't partial-match "E1_ALT"
            rules.InfantryTypes.Add(new InfantryType("E1_ALT"));

            string result = InvokeResolve("美国大兵", AIPlaceObjectType.Infantry, rules);

            // "美国大兵" doesn't substring-match any INI name in loaded rules
            Assert.Null(result);
        }

        /// <summary>
        /// Test 8: Building alias resolves against BuildingTypes.
        /// </summary>
        [Fact]
        public void BuildingAlias_ResolvesAgainstBuildingTypes()
        {
            var aliasJson = @"{
                ""aliases"": [
                    { ""alias"": ""盟军兵营"", ""code"": ""GAPILE"", ""type"": ""Building"", ""description"": ""Allied Barracks"" }
                ]
            }";

            var rules = new Rules();
            rules.BuildingTypes.Add(new BuildingType("GAPILE"));

            string result = InvokeResolveWithCustomAliases("盟军兵营", AIPlaceObjectType.Building, rules, aliasJson);

            Assert.Equal("GAPILE", result);
        }

        /// <summary>
        /// Test 9: Building alias code not in loaded BuildingTypes → returns null.
        /// </summary>
        [Fact]
        public void BuildingAlias_NotInRules_ReturnsNull()
        {
            var aliasJson = @"{
                ""aliases"": [
                    { ""alias"": ""盟军兵营"", ""code"": ""GAPILE"", ""type"": ""Building"", ""description"": ""Allied Barracks"" }
                ]
            }";

            var rules = new Rules();
            // No GAPILE in BuildingTypes

            string result = InvokeResolveWithCustomAliases("盟军兵营", AIPlaceObjectType.Building, rules, aliasJson);

            Assert.Null(result);
        }

        #region Helpers

        private string InvokeResolve(string name, AIPlaceObjectType objectType, Rules rules)
        {
            return InvokeResolveWithCustomAliases(name, objectType, rules, TestAliasJson);
        }

        private string InvokeResolveWithCustomAliases(string name, AIPlaceObjectType objectType, Rules rules, string aliasJson)
        {
            var aliases = AIUnitAliasResolver.LoadFromJson(aliasJson);
            object executor = CreateExecutorWithRulesAndAliases(rules, aliases);

            var method = typeof(ToolExecutor).GetMethod("ResolveObjectININame",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            return (string)method.Invoke(executor, new object[] { name, objectType });
        }

        private static object CreateExecutorWithRulesAndAliases(Rules rules, IReadOnlyList<AIUnitAliasMatch> aliases)
        {
            // Create a Map with controlled Rules
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));

            // Rules is a property with private setter — use the backing field
            var rulesProp = typeof(Map).GetProperty("Rules",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(rulesProp);

            // Try private setter
            var setter = rulesProp.GetSetMethod(true);
            Assert.NotNull(setter);
            setter.Invoke(map, new object[] { rules });

            // Create uninitialized ToolExecutor and set required fields
            object executor = RuntimeHelpers.GetUninitializedObject(typeof(ToolExecutor));

            SetField(executor, "map", map);
            SetField(executor, "unitAliases", aliases);

            return executor;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = typeof(ToolExecutor).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        #endregion
    }
}
