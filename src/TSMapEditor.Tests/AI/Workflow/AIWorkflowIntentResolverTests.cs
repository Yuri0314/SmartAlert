using TSMapEditor.AI.Workflow;
using Xunit;

namespace TSMapEditor.Tests.AI.Workflow
{
    public class AIWorkflowIntentResolverTests
    {
        [Fact]
        public void ResolveIntent_MapsTowerDefenseChinese()
        {
            Assert.Equal("TowerDefense", AIWorkflowIntentResolver.ResolveIntent("帮我生成一个塔防地图"));
            Assert.Equal("TowerDefense", AIWorkflowIntentResolver.ResolveIntent("做一个防守地图"));
        }

        [Fact]
        public void ResolveIntent_MapsScenarioChinese()
        {
            Assert.Equal("ScenarioStory", AIWorkflowIntentResolver.ResolveIntent("创建一个剧情任务"));
            Assert.Equal("ScenarioStory", AIWorkflowIntentResolver.ResolveIntent("生成战役地图"));
        }

        [Fact]
        public void ResolveIntent_MapsLocalEditChinese()
        {
            Assert.Equal("LocalEdit", AIWorkflowIntentResolver.ResolveIntent("在这里加几棵树"));
            Assert.Equal("LocalEdit", AIWorkflowIntentResolver.ResolveIntent("局部编辑选区"));
        }

        [Fact]
        public void ResolveIntent_MapsBalancedChinese()
        {
            Assert.Equal("BalancedSkirmish", AIWorkflowIntentResolver.ResolveIntent("生成一张对战地图"));
            Assert.Equal("BalancedSkirmish", AIWorkflowIntentResolver.ResolveIntent("做一张1v1平衡地图"));
        }

        [Fact]
        public void ResolveIntent_DefaultsSubstantialUnknownMapRequestToBalanced()
        {
            // Contains "地图"/"生成" but no specific intent keyword
            Assert.Equal("BalancedSkirmish", AIWorkflowIntentResolver.ResolveIntent("生成一张地图"));
            Assert.Equal("BalancedSkirmish", AIWorkflowIntentResolver.ResolveIntent("create a map"));
        }

        [Fact]
        public void ResolveIntent_ReturnsEmptyForStatusOnlyMessage()
        {
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("继续"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("ok"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("好的"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("看一下进度"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("谢谢"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent(""));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent(null));
        }

        [Fact]
        public void ApplyInitialState_CapturesFirstSubstantialUserGoal()
        {
            var state = new AIWorkflowState();
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "生成一张2人对战地图");

            Assert.Equal(2, updated); // UserGoal + Intent
            Assert.Equal("生成一张2人对战地图", state.UserGoal);
            Assert.Equal("BalancedSkirmish", state.Intent);
        }

        [Fact]
        public void ApplyInitialState_DoesNotOverwriteExistingUserGoal()
        {
            var state = new AIWorkflowState { UserGoal = "已有目标" };
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "新的消息");

            Assert.Equal("已有目标", state.UserGoal); // Not overwritten
        }

        [Fact]
        public void ApplyInitialState_DoesNotOverwriteExistingIntent()
        {
            var state = new AIWorkflowState { Intent = "TowerDefense" };
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "生成一张对战地图");

            Assert.Equal("TowerDefense", state.Intent); // Not overwritten to BalancedSkirmish
        }

        [Fact]
        public void ApplyInitialState_LeavesEmptyStateForStatusOnlyMessage()
        {
            var state = new AIWorkflowState();
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "ok");

            Assert.Equal(0, updated);
            Assert.Equal(string.Empty, state.UserGoal);
            Assert.Equal(string.Empty, state.Intent);
        }
        [Fact]
        public void ResolveIntent_ReturnsEmptyForNonMapMessage()
        {
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("hello"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("tell me a joke"));
            Assert.Equal(string.Empty, AIWorkflowIntentResolver.ResolveIntent("天气怎么样"));
        }

        [Fact]
        public void ShouldCaptureUserGoal_ReturnsFalseForNonMapMessage()
        {
            Assert.False(AIWorkflowIntentResolver.ShouldCaptureUserGoal("hello"));
            Assert.False(AIWorkflowIntentResolver.ShouldCaptureUserGoal("tell me a joke"));
        }

        [Fact]
        public void ApplyInitialState_DoesNotCaptureNonMapFirstMessage()
        {
            var state = new AIWorkflowState();
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "hello");

            Assert.Equal(0, updated);
            Assert.Equal(string.Empty, state.UserGoal);
            Assert.Equal(string.Empty, state.Intent);
        }

        [Fact]
        public void ApplyInitialState_CapturesMapGoalAfterIgnoredNonMapMessage()
        {
            var state = new AIWorkflowState();

            // First: non-map message → ignored
            AIWorkflowIntentResolver.ApplyInitialState(state, "hello");
            Assert.Equal(string.Empty, state.UserGoal);

            // Second: real map request → captured
            int updated = AIWorkflowIntentResolver.ApplyInitialState(state, "生成一张2人对战地图");
            Assert.Equal(2, updated);
            Assert.Equal("生成一张2人对战地图", state.UserGoal);
            Assert.Equal("BalancedSkirmish", state.Intent);
        }
    }
}
