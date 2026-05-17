using TSMapEditor.AI.Workflow;
using Xunit;

namespace TSMapEditor.Tests.AI.Workflow
{
    public class AIWorkflowStateTests
    {
        [Fact]
        public void NewState_InitializesCollections()
        {
            var state = new AIWorkflowState();

            Assert.NotNull(state.CompletedSteps);
            Assert.NotNull(state.PendingSteps);
            Assert.NotNull(state.KnownRisks);
            Assert.NotNull(state.ValidationIssues);

            Assert.Empty(state.CompletedSteps);
            Assert.Empty(state.PendingSteps);
            Assert.Empty(state.KnownRisks);
            Assert.Empty(state.ValidationIssues);
        }

        [Fact]
        public void NewState_DefaultsToIdlePhase()
        {
            var state = new AIWorkflowState();
            Assert.Equal("Idle", state.CurrentPhase);
        }

        [Fact]
        public void ToSummary_IncludesUserGoal()
        {
            var state = new AIWorkflowState { UserGoal = "Test Goal" };
            var summary = state.ToSummary();
            Assert.Contains("Test Goal", summary);
        }

        [Fact]
        public void ToSummary_IncludesActiveSelection()
        {
            var state = new AIWorkflowState { ActiveSelection = "(10,10) 5x5" };
            var summary = state.ToSummary();
            Assert.Contains("(10,10) 5x5", summary);
        }

        [Fact]
        public void ToSummary_HandlesEmptyCollections()
        {
            var state = new AIWorkflowState();
            var summary = state.ToSummary();

            // It should say (无) or something similar instead of failing
            Assert.Contains("(无)", summary);
            Assert.DoesNotContain("System.NullReferenceException", summary);
        }

        [Fact]
        public void ToSummary_LabelsValidationIssuesAsLastFindings()
        {
            var state = new AIWorkflowState();
            state.ValidationIssues.Add("Test Issue");
            var summary = state.ToSummary();
            Assert.Contains("[Last Validation Findings]", summary);
        }

        [Fact]
        public void ToSummary_ExplainsValidationFindingsMayBeAutoFixed()
        {
            var state = new AIWorkflowState();
            state.ValidationIssues.Add("Test Issue");
            var summary = state.ToSummary();
            Assert.Contains("auto-fixed", summary);
        }
    }
}
