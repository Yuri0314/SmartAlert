using System.Collections.Generic;
using System.Text.Json;

namespace TSMapEditor.AI.Workflow
{
    /// <summary>
    /// Applies partial updates to an AIWorkflowState from a JSON arguments object.
    /// Only fields present in the JSON are updated; missing fields are left unchanged.
    /// Pure logic — no Map or UI dependencies.
    /// </summary>
    public static class AIWorkflowStateUpdater
    {
        /// <summary>
        /// Applies fields from the given JSON element to the workflow state.
        /// Returns the number of fields updated.
        /// </summary>
        public static int Apply(AIWorkflowState state, JsonElement args)
        {
            if (state == null) return 0;

            int updated = 0;

            if (args.TryGetProperty("user_goal", out var goalEl) && goalEl.ValueKind == JsonValueKind.String)
            {
                state.UserGoal = goalEl.GetString();
                updated++;
            }

            if (args.TryGetProperty("intent", out var intentEl) && intentEl.ValueKind == JsonValueKind.String)
            {
                state.Intent = intentEl.GetString();
                updated++;
            }

            if (args.TryGetProperty("current_phase", out var phaseEl) && phaseEl.ValueKind == JsonValueKind.String)
            {
                state.CurrentPhase = phaseEl.GetString();
                updated++;
            }

            if (args.TryGetProperty("completed_steps", out var completedEl) && completedEl.ValueKind == JsonValueKind.Array)
            {
                state.CompletedSteps = ParseSteps(completedEl, "Completed");
                updated++;
            }

            if (args.TryGetProperty("pending_steps", out var pendingEl) && pendingEl.ValueKind == JsonValueKind.Array)
            {
                state.PendingSteps = ParseSteps(pendingEl, "Pending");
                updated++;
            }

            if (args.TryGetProperty("known_risks", out var risksEl) && risksEl.ValueKind == JsonValueKind.Array)
            {
                state.KnownRisks = ParseStringArray(risksEl);
                updated++;
            }

            return updated;
        }

        private static List<AIWorkflowStep> ParseSteps(JsonElement array, string status)
        {
            var steps = new List<AIWorkflowStep>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string desc = item.GetString();
                    if (!string.IsNullOrWhiteSpace(desc))
                        steps.Add(new AIWorkflowStep(desc, status));
                }
                // Ignore non-string array elements
            }
            return steps;
        }

        private static List<string> ParseStringArray(JsonElement array)
        {
            var result = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                        result.Add(val);
                }
                // Ignore non-string array elements
            }
            return result;
        }
    }
}
