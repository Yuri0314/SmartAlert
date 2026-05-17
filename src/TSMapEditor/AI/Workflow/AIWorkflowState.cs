using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TSMapEditor.AI.Workflow
{
    public class AIWorkflowState
    {
        public string UserGoal { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = "Idle";
        public List<AIWorkflowStep> CompletedSteps { get; set; } = new List<AIWorkflowStep>();
        public List<AIWorkflowStep> PendingSteps { get; set; } = new List<AIWorkflowStep>();
        public List<string> KnownRisks { get; set; } = new List<string>();
        public string ActiveSelection { get; set; } = string.Empty;
        public List<string> ValidationIssues { get; set; } = new List<string>();

        public string ToSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 当前任务工作流状态 ===");
            sb.AppendLine($"UserGoal: {(string.IsNullOrWhiteSpace(UserGoal) ? "(无)" : UserGoal)}");
            sb.AppendLine($"Intent: {(string.IsNullOrWhiteSpace(Intent) ? "(无)" : Intent)}");
            sb.AppendLine($"CurrentPhase: {CurrentPhase}");
            sb.AppendLine($"ActiveSelection: {(string.IsNullOrWhiteSpace(ActiveSelection) ? "(无)" : ActiveSelection)}");

            sb.AppendLine("\n[Completed Steps]");
            if (CompletedSteps.Any())
            {
                foreach (var step in CompletedSteps)
                    sb.AppendLine($"- {step}");
            }
            else
            {
                sb.AppendLine("(无)");
            }

            sb.AppendLine("\n[Pending Steps]");
            if (PendingSteps.Any())
            {
                foreach (var step in PendingSteps)
                    sb.AppendLine($"- {step}");
            }
            else
            {
                sb.AppendLine("(无)");
            }

            sb.AppendLine("\n[Known Risks]");
            if (KnownRisks.Any())
            {
                foreach (var risk in KnownRisks)
                    sb.AppendLine($"- {risk}");
            }
            else
            {
                sb.AppendLine("(无)");
            }

            sb.AppendLine("\n[Last Validation Findings]");
            sb.AppendLine("(Note: These are findings from the last quality check pass and may have been auto-fixed)");
            if (ValidationIssues.Any())
            {
                foreach (var issue in ValidationIssues)
                    sb.AppendLine($"- {issue}");
            }
            else
            {
                sb.AppendLine("(无)");
            }

            return sb.ToString();
        }
    }
}
