using System.Collections.Generic;
using System.Text.Json;
using TSMapEditor.AI.Workflow;
using Xunit;

namespace TSMapEditor.Tests.AI.Workflow
{
    public class AIWorkflowStateUpdaterTests
    {
        private static JsonElement ParseArgs(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        [Fact]
        public void Apply_UpdatesUserGoalWhenPresent()
        {
            var state = new AIWorkflowState();
            var args = ParseArgs("{\"user_goal\": \"生成一张2人对战地图\"}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal("生成一张2人对战地图", state.UserGoal);
        }

        [Fact]
        public void Apply_DoesNotClearMissingFields()
        {
            var state = new AIWorkflowState
            {
                UserGoal = "已有目标",
                Intent = "BalancedSkirmish",
                CurrentPhase = "Terrain"
            };
            var args = ParseArgs("{\"current_phase\": \"Structures\"}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal("已有目标", state.UserGoal); // Not cleared
            Assert.Equal("BalancedSkirmish", state.Intent); // Not cleared
            Assert.Equal("Structures", state.CurrentPhase); // Updated
        }

        [Fact]
        public void Apply_ReplacesCompletedStepsWhenPresent()
        {
            var state = new AIWorkflowState();
            state.CompletedSteps.Add(new AIWorkflowStep("old step", "Completed"));

            var args = ParseArgs("{\"completed_steps\": [\"设置出生点\", \"铺设地形\"]}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal(2, state.CompletedSteps.Count);
            Assert.Equal("设置出生点", state.CompletedSteps[0].Description);
            Assert.Equal("Completed", state.CompletedSteps[0].Status);
            Assert.Equal("铺设地形", state.CompletedSteps[1].Description);
        }

        [Fact]
        public void Apply_ReplacesPendingStepsWhenPresent()
        {
            var state = new AIWorkflowState();

            var args = ParseArgs("{\"pending_steps\": [\"放置矿石\", \"添加装饰\"]}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal(2, state.PendingSteps.Count);
            Assert.Equal("放置矿石", state.PendingSteps[0].Description);
            Assert.Equal("Pending", state.PendingSteps[0].Status);
        }

        [Fact]
        public void Apply_ReplacesKnownRisksWhenPresent()
        {
            var state = new AIWorkflowState();
            state.KnownRisks.Add("旧风险");

            var args = ParseArgs("{\"known_risks\": [\"出生点距离可能过近\", \"矿石分布不均\"]}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal(2, state.KnownRisks.Count);
            Assert.Equal("出生点距离可能过近", state.KnownRisks[0]);
        }

        [Fact]
        public void Apply_IgnoresNonStringArrayItems()
        {
            var state = new AIWorkflowState();

            var args = ParseArgs("{\"completed_steps\": [\"valid step\", 123, null, true, \"another step\"]}");

            int updated = AIWorkflowStateUpdater.Apply(state, args);

            Assert.Equal(1, updated);
            Assert.Equal(2, state.CompletedSteps.Count);
            Assert.Equal("valid step", state.CompletedSteps[0].Description);
            Assert.Equal("another step", state.CompletedSteps[1].Description);
        }
    }
}
