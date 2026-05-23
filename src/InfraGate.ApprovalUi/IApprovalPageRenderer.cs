namespace InfraGate.ApprovalUi;

public interface IApprovalPageRenderer
{
    Task<string> RenderApprovalPageAsync(ApprovalPageData pageData);
    Task<string> RenderDecisionPageAsync(DecisionPageData decisionData);
}
