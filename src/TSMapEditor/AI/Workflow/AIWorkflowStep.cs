namespace TSMapEditor.AI.Workflow
{
    public class AIWorkflowStep
    {
        public string Description { get; set; }
        public string Status { get; set; }

        public AIWorkflowStep(string description, string status)
        {
            Description = description;
            Status = status;
        }

        public override string ToString()
        {
            return $"[{Status}] {Description}";
        }
    }
}
