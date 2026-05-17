using System;

namespace TSMapEditor.AI.Validation
{
    /// <summary>
    /// Infers MapValidationPolicy from workflow intent or user goal text.
    /// Pure logic — does not depend on Map or UI objects.
    /// </summary>
    public static class MapValidationPolicyResolver
    {
        /// <summary>
        /// Resolves the validation policy.
        /// Explicit intent wins over userGoal heuristics.
        /// Empty/null values default to Balanced.
        /// </summary>
        public static MapValidationPolicy Resolve(string intent, string userGoal)
        {
            // 1. Try explicit intent first
            if (!string.IsNullOrWhiteSpace(intent))
            {
                var policy = ResolveFromIntent(intent.Trim());
                if (policy.HasValue)
                    return policy.Value;
            }

            // 2. Fall back to user-goal heuristic
            if (!string.IsNullOrWhiteSpace(userGoal))
            {
                var policy = ResolveFromUserGoal(userGoal);
                if (policy.HasValue)
                    return policy.Value;
            }

            // 3. Default
            return MapValidationPolicy.Balanced;
        }

        private static MapValidationPolicy? ResolveFromIntent(string intent)
        {
            if (string.Equals(intent, "BalancedSkirmish", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.Balanced;
            if (string.Equals(intent, "CreativeSkirmish", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.Creative;
            if (string.Equals(intent, "SurvivalChallenge", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.Creative;
            if (string.Equals(intent, "TowerDefense", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.Scenario;
            if (string.Equals(intent, "ScenarioStory", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.Scenario;
            if (string.Equals(intent, "BeautifyExistingMap", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.LocalOnly;
            if (string.Equals(intent, "LocalEdit", StringComparison.OrdinalIgnoreCase))
                return MapValidationPolicy.LocalOnly;

            return null; // Unknown intent, fall through to goal heuristic
        }

        private static MapValidationPolicy? ResolveFromUserGoal(string userGoal)
        {
            string goal = userGoal.ToLowerInvariant();

            // Scenario keywords (Chinese + English)
            if (goal.Contains("塔防") || goal.Contains("防守") || goal.Contains("tower defense"))
                return MapValidationPolicy.Scenario;
            if (goal.Contains("剧情") || goal.Contains("战役") || goal.Contains("任务") ||
                goal.Contains("scenario") || goal.Contains("story"))
                return MapValidationPolicy.Scenario;

            // LocalOnly keywords
            if (goal.Contains("局部") || goal.Contains("选区") || goal.Contains("这里") ||
                goal.Contains("美化") || goal.Contains("beautify") || goal.Contains("local"))
                return MapValidationPolicy.LocalOnly;

            // Creative keywords
            if (goal.Contains("创意") || goal.Contains("生存") || goal.Contains("挑战") ||
                goal.Contains("creative") || goal.Contains("survival") || goal.Contains("challenge"))
                return MapValidationPolicy.Creative;

            // Balanced keywords
            if (goal.Contains("平衡") || goal.Contains("对战") ||
                goal.Contains("balanced") || goal.Contains("skirmish"))
                return MapValidationPolicy.Balanced;

            return null;
        }
    }
}
