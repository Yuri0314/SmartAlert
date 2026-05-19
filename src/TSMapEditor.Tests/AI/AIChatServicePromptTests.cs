using System.Reflection;
using TSMapEditor.AI;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class AIChatServicePromptTests
    {
        private static string GetWorkflowProtocolPrompt()
        {
            var method = typeof(AIChatService).GetMethod(
                "BuildWorkflowProtocolPrompt",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var prompt = Assert.IsType<string>(method.Invoke(null, null));
            return prompt;
        }

        [Fact]
        public void WorkflowProtocolPrompt_MentionsSetWorkflowState()
        {
            var prompt = GetWorkflowProtocolPrompt();
            Assert.Contains("set_workflow_state", prompt);
        }

        [Fact]
        public void WorkflowProtocolPrompt_MentionsGetWorkflowState()
        {
            var prompt = GetWorkflowProtocolPrompt();
            Assert.Contains("get_workflow_state", prompt);
        }

        [Fact]
        public void WorkflowProtocolPrompt_InstructsNoInventedWorkflowTools()
        {
            var prompt = GetWorkflowProtocolPrompt();
            // Must instruct the AI not to invent tools outside the schema
            Assert.Contains("不要发明", prompt);
            Assert.Contains("schema", prompt);
        }

        [Fact]
        public void WorkflowProtocolPrompt_InstructsNoUnverifiedValidationClaims()
        {
            var prompt = GetWorkflowProtocolPrompt();
            // Must instruct the AI not to claim validation/testing without actually calling tools
            Assert.Contains("不要在回复中声称", prompt);
        }

        [Fact]
        public void WorkflowProtocolPrompt_InstructsPhaseTransitionUpdatesNotEveryTinyCall()
        {
            var prompt = GetWorkflowProtocolPrompt();
            // Must instruct phase-transition-level updates, not every tiny call
            Assert.Contains("阶段转换", prompt);
            Assert.Contains("不要在每一个工具调用", prompt);
        }
        [Fact]
        public void WorkflowOrSystemPrompt_InstructsGetHousesWhenOwnerUncertain()
        {
            var method = typeof(AIChatService).GetMethod(
                "BuildOwnerGuardrailPrompt",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var prompt = Assert.IsType<string>(method.Invoke(null, null));
            Assert.Contains("get_houses", prompt);
            Assert.Contains("精确", prompt);
        }

        private static string GetSelectionGuardrailPrompt()
        {
            var method = typeof(AIChatService).GetMethod(
                "BuildSelectionGuardrailPrompt",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return Assert.IsType<string>(method.Invoke(null, null));
        }

        [Fact]
        public void SelectionGuardrailPrompt_ExplainsSelectionRelativeCoordinates()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("活跃选区（Active Selection）", prompt);
            Assert.Contains("选区相对坐标", prompt);
            Assert.Contains("回退为全地图", prompt);
        }

        [Fact]
        public void SelectionGuardrailPrompt_ExplainsGlobalExceptions()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("set_spawn_point", prompt);
            Assert.Contains("set_map_name", prompt);
            Assert.Contains("永远作用于全图", prompt);
            Assert.Contains("不受选区约束", prompt);
        }

        [Fact]
        public void SelectionGuardrailPrompt_ExplainsQueryToolsDoNotMutate()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("get_map_info", prompt);
            Assert.Contains("get_workflow_state", prompt);
            Assert.Contains("不会修改任何地形", prompt);
        }

        [Fact]
        public void SelectionGuardrailPrompt_ExplainsPathWidthLimitation()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("draw_road", prompt);
            Assert.Contains("draw_river", prompt);
            Assert.Contains("轻微溢出选区边界", prompt);
        }

        [Fact]
        public void SelectionGuardrailPrompt_MentionsCoordinateScope()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("coordinate_scope", prompt);
            Assert.Contains("global", prompt);
        }

        [Fact]
        public void SelectionGuardrailPrompt_InstructsGlobalForMapCenter()
        {
            var prompt = GetSelectionGuardrailPrompt();
            Assert.Contains("coordinate_scope", prompt);
        }
    }
}
