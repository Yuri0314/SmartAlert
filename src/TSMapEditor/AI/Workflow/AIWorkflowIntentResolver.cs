using System;

namespace TSMapEditor.AI.Workflow
{
    /// <summary>
    /// Resolves workflow intent from user messages using deterministic keyword matching.
    /// Pure logic — no AI provider, Map, or UI dependencies.
    /// </summary>
    public static class AIWorkflowIntentResolver
    {
        private static readonly string[] StatusOnlyPatterns = new[]
        {
            "继续", "看一下进度", "现在到哪了", "好的", "ok", "谢谢",
            "thanks", "thank you", "go on", "continue", "yes", "是的",
            "查看工作流状态", "状态", "进度"
        };

        /// <summary>
        /// Returns one of the seven canonical intent strings, or string.Empty
        /// when the message is not a substantial editing request.
        /// </summary>
        public static string ResolveIntent(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return string.Empty;

            // Status-only messages → no intent
            if (IsStatusOnlyMessage(userMessage))
                return string.Empty;

            string msg = userMessage.ToLowerInvariant();

            // TowerDefense
            if (msg.Contains("塔防") || msg.Contains("防守") || msg.Contains("tower defense"))
                return "TowerDefense";

            // ScenarioStory
            if (msg.Contains("剧情") || msg.Contains("任务") || msg.Contains("战役") ||
                msg.Contains("scenario") || msg.Contains("story") || msg.Contains("mission"))
                return "ScenarioStory";

            // SurvivalChallenge
            if (msg.Contains("生存") || msg.Contains("挑战") ||
                msg.Contains("survival") || msg.Contains("challenge"))
                return "SurvivalChallenge";

            // BeautifyExistingMap
            if (msg.Contains("美化") || msg.Contains("装饰") ||
                msg.Contains("beautify") || msg.Contains("decorate"))
                return "BeautifyExistingMap";

            // LocalEdit
            if (msg.Contains("选区") || msg.Contains("这里") || msg.Contains("局部") ||
                msg.Contains("local") || msg.Contains("selection"))
                return "LocalEdit";

            // CreativeSkirmish
            if (msg.Contains("创意") || msg.Contains("非对称") ||
                msg.Contains("creative") || msg.Contains("asymmetric"))
                return "CreativeSkirmish";

            // BalancedSkirmish (explicit keywords)
            if (msg.Contains("对战") || msg.Contains("遭遇战") || msg.Contains("平衡") ||
                msg.Contains("balanced") || msg.Contains("skirmish") ||
                msg.Contains("1v1") || msg.Contains("2v2"))
                return "BalancedSkirmish";

            // Substantial map request with no specific keyword → default to BalancedSkirmish
            if (IsSubstantialMapRequest(msg))
                return "BalancedSkirmish";

            return string.Empty;
        }

        /// <summary>
        /// Returns true for substantial map editing/generation messages.
        /// Returns false for empty messages and status/control acknowledgements.
        /// </summary>
        public static bool ShouldCaptureUserGoal(string userMessage)
        {
            return !string.IsNullOrEmpty(ResolveIntent(userMessage));
        }

        /// <summary>
        /// Applies fallback workflow hydration. Only sets empty fields.
        /// Returns the number of fields updated.
        /// </summary>
        public static int ApplyInitialState(AIWorkflowState state, string userMessage)
        {
            if (state == null) return 0;

            int updated = 0;

            if (string.IsNullOrWhiteSpace(state.UserGoal) && ShouldCaptureUserGoal(userMessage))
            {
                state.UserGoal = userMessage.Trim();
                updated++;
            }

            if (string.IsNullOrWhiteSpace(state.Intent))
            {
                string intent = ResolveIntent(userMessage);
                if (!string.IsNullOrEmpty(intent))
                {
                    state.Intent = intent;
                    updated++;
                }
            }

            return updated;
        }

        private static bool IsStatusOnlyMessage(string message)
        {
            string trimmed = message.Trim();
            foreach (var pattern in StatusOnlyPatterns)
            {
                if (string.Equals(trimmed, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSubstantialMapRequest(string lowerMessage)
        {
            // Heuristic: contains map-related action words
            return lowerMessage.Contains("生成") || lowerMessage.Contains("创建") ||
                   lowerMessage.Contains("地图") || lowerMessage.Contains("放置") ||
                   lowerMessage.Contains("添加") || lowerMessage.Contains("create") ||
                   lowerMessage.Contains("generate") || lowerMessage.Contains("map") ||
                   lowerMessage.Contains("place") || lowerMessage.Contains("add") ||
                   lowerMessage.Contains("build");
        }
    }
}
