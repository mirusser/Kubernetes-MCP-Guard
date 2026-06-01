namespace InfraGate.Planner.Tasks;

/// <summary>
/// Naming conventions for the Planner-owned durable A2A task store. The schema name is shared
/// between the migration runner and the persistence SQL, so it lives in one place.
/// </summary>
internal static class PlannerTaskStoreConventions
{
    public const string Schema = "planner_tasks";
    public const string TableName = "agent_tasks";
    public const string QualifiedTable = $"{Schema}.{TableName}";

    /// <summary>Output-relative directory holding the task-store migration scripts.</summary>
    public const string MigrationsRelativePath = "Tasks/Migrations";

    public static class DomainStates
    {
        public const string Planning = "planning";
        public const string Unremediable = "unremediable";
        public const string Waiting = "waiting";
    }

    public static class Artifacts
    {
        public const string PlanReferenceId = "plan-reference";
        public const string PlanReferenceName = "planId";
    }
}
