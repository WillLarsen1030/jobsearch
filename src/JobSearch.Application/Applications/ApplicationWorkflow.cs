namespace JobSearch.Application.Applications;

public static class ApplicationWorkflow
{
    public static bool CanTransition(ApplicationWorkflowStatus from, ApplicationWorkflowStatus to) =>
        (from, to) switch
        {
            (ApplicationWorkflowStatus.Draft, ApplicationWorkflowStatus.Prepared) => true,
            (ApplicationWorkflowStatus.Prepared, ApplicationWorkflowStatus.Approved) => true,
            (ApplicationWorkflowStatus.Approved, ApplicationWorkflowStatus.InProgress) => true,
            (ApplicationWorkflowStatus.NeedsInput, ApplicationWorkflowStatus.InProgress) => true,
            (ApplicationWorkflowStatus.InProgress, ApplicationWorkflowStatus.NeedsInput) => true,
            (ApplicationWorkflowStatus.InProgress, ApplicationWorkflowStatus.ReadyToSubmit) => true,
            (ApplicationWorkflowStatus.InProgress, ApplicationWorkflowStatus.Failed) => true,
            (ApplicationWorkflowStatus.ReadyToSubmit, ApplicationWorkflowStatus.Submitted) => true,
            (_, ApplicationWorkflowStatus.Withdrawn) when from is not ApplicationWorkflowStatus.Submitted => true,
            _ => false
        };

    public static void EnsureTransition(ApplicationWorkflowStatus from, ApplicationWorkflowStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Application cannot transition from {from} to {to}.");
        }
    }
}
