namespace AipApi.Applications;

/// <summary>
/// A submitted AIP form and its decision. Stored as one MongoDB document (collection "applications")
/// and returned by GET /api/applications/{id}. Holds figures only: no name, contact or other personal details.
/// </summary>
public sealed class AipApplication
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public ApplicationInput Input { get; set; } = new();
    public ApplicationResult Result { get; set; } = new();

    public static AipApplication Create(string id, DateTime createdAtUtc, AipInput input, AipDecision decision) => new()
    {
        Id = id,
        CreatedAt = createdAtUtc,
        Input = new ApplicationInput
        {
            AnnualIncome = input.AnnualIncome,
            MonthlyCommitments = input.MonthlyCommitments,
            LoanAmount = input.LoanAmount
        },
        Result = new ApplicationResult
        {
            Decision = decision.Decision,
            Reason = decision.Reason,
            BorrowingPower = decision.BorrowingPower,
            MaxBorrowing = decision.MaxBorrowing,
            LoanToIncomeRatio = decision.LoanToIncomeRatio,
            MonthlyAffordability = decision.MonthlyAffordability
        }
    };
}

public sealed class ApplicationInput
{
    public double AnnualIncome { get; set; }
    public double MonthlyCommitments { get; set; }
    public double LoanAmount { get; set; }
}

public sealed class ApplicationResult
{
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public long BorrowingPower { get; set; }
    public long MaxBorrowing { get; set; }
    public double LoanToIncomeRatio { get; set; }
    public long MonthlyAffordability { get; set; }
}

/// <summary>Decision response: the original fields plus the new applicationId (added, nothing renamed).</summary>
public sealed record AipDecisionResponse(
    string ApplicationId,
    string Decision,
    string Reason,
    long BorrowingPower,
    long MaxBorrowing,
    double LoanToIncomeRatio,
    long MonthlyAffordability)
{
    public static AipDecisionResponse From(string applicationId, AipDecision decision) => new(
        applicationId,
        decision.Decision,
        decision.Reason,
        decision.BorrowingPower,
        decision.MaxBorrowing,
        decision.LoanToIncomeRatio,
        decision.MonthlyAffordability);
}
