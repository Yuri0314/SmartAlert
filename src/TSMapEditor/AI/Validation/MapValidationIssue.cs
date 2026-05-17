using System.Text;

namespace TSMapEditor.AI.Validation
{
    public class MapValidationIssue
    {
        public MapValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Area { get; }
        public bool AllowedByIntent { get; }
        public string SuggestedFix { get; }

        public bool RequiresAttention => Severity == MapValidationSeverity.Error || Severity == MapValidationSeverity.Warning;

        public MapValidationIssue(
            MapValidationSeverity severity,
            string code,
            string message,
            string area = "",
            bool allowedByIntent = false,
            string suggestedFix = "")
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Area = area ?? string.Empty;
            AllowedByIntent = allowedByIntent;
            SuggestedFix = suggestedFix ?? string.Empty;
        }

        public string ToSummary()
        {
            var sb = new StringBuilder();
            sb.Append($"[{Severity}] {Code}: {Message}");

            var details = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(Area))
            {
                details.Add($"area: {Area}");
            }

            if (AllowedByIntent)
            {
                details.Add("allowed by intent");
            }

            if (!string.IsNullOrEmpty(SuggestedFix))
            {
                details.Add($"suggested fix: {SuggestedFix}");
            }

            if (details.Count > 0)
            {
                sb.Append(" (");
                sb.Append(string.Join(", ", details));
                sb.Append(")");
            }

            return sb.ToString();
        }
    }
}
