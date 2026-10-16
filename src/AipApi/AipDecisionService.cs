using System.Text.Json;

namespace AipApi;

public sealed record AipInput(double AnnualIncome, double LoanAmount, double MonthlyCommitments);

public sealed record AipDecision(
    string Decision,
    string Reason,
    long BorrowingPower,
    long MaxBorrowing,
    double LoanToIncomeRatio,
    long MonthlyAffordability);

public sealed record BorrowingPowerResult(
    long BorrowingPower,
    long MaxLoan,
    double AnnualIncome,
    double ExistingCommitments);

public sealed record ApiError(string Error, IReadOnlyList<string>? Details = null);

/// <summary>
/// Mortgage Agreement in Principle rules. Behaviour matches the original Node mock server
/// so the existing API and BDD suites pass unchanged against this service.
/// </summary>
public sealed class AipDecisionService
{
    public const double IncomeMultiple = 4.5;
    public const double MaxDebtToIncome = 0.4;

    /// <summary>Validates a decision request body. Returns either a typed input or an error.</summary>
    public static (AipInput? Input, ApiError? Error) ParseDecisionRequest(JsonElement body)
    {
        var hasIncome = body.TryGetProperty("annualIncome", out var income);
        var hasLoan = body.TryGetProperty("loanAmount", out var loan);

        if (!hasIncome || IsFalsy(income) || !hasLoan)
        {
            return (null, new ApiError("Missing required fields", ["annualIncome", "loanAmount"]));
        }

        if (income.ValueKind != JsonValueKind.Number || loan.ValueKind != JsonValueKind.Number)
        {
            return (null, new ApiError("Invalid data types", ["annualIncome and loanAmount must be numbers"]));
        }

        var annualIncome = income.GetDouble();
        var loanAmount = loan.GetDouble();

        if (annualIncome <= 0 || loanAmount < 0)
        {
            return (null, new ApiError("Invalid values", ["annualIncome must be > 0, loanAmount must be >= 0"]));
        }

        return (new AipInput(annualIncome, loanAmount, ReadCommitments(body)), null);
    }

    public AipDecision Decide(AipInput input)
    {
        var maxBorrowing = input.AnnualIncome * IncomeMultiple;
        var monthlyIncome = input.AnnualIncome / 12;
        var loanToIncome = input.LoanAmount / input.AnnualIncome;

        var decision = "Accepted";
        var reason = "Within standard affordability limits";

        // Priority 1: debt-to-income. Monthly commitments above 40% of monthly income decline.
        if (input.MonthlyCommitments > monthlyIncome * MaxDebtToIncome)
        {
            decision = "Declined";
            reason = "Existing credit commitments exceed affordability threshold (DTI > 40%).";
        }
        // Priority 2: loan-to-income. Above 4.5x income needs manual underwriting.
        else if (loanToIncome > IncomeMultiple)
        {
            decision = "Referred";
            reason = "Loan amount exceeds 4.5x income multiple. Manual underwriting required.";
        }

        return new AipDecision(
            decision,
            reason,
            JsRound(maxBorrowing),
            JsRound(Math.Min(input.LoanAmount, maxBorrowing)),
            Math.Round(loanToIncome, 2, MidpointRounding.AwayFromZero),
            JsRound((monthlyIncome - input.MonthlyCommitments) / 200));
    }

    public static (BorrowingPowerResult? Result, ApiError? Error) CalculateBorrowingPower(JsonElement body)
    {
        if (!body.TryGetProperty("annualIncome", out var income) || IsFalsy(income))
        {
            return (null, new ApiError("Missing annualIncome"));
        }

        if (income.ValueKind != JsonValueKind.Number)
        {
            return (null, new ApiError("Invalid data types", ["annualIncome must be a number"]));
        }

        var annualIncome = income.GetDouble();
        var commitments = ReadCommitments(body);
        var borrowingPower = annualIncome * IncomeMultiple;
        var maxLoan = borrowingPower - (commitments * 12);

        return (new BorrowingPowerResult(
            JsRound(borrowingPower),
            JsRound(Math.Max(0, maxLoan)),
            annualIncome,
            commitments), null);
    }

    /// <summary>JavaScript Math.round semantics (halves round towards +infinity).</summary>
    public static long JsRound(double value) => (long)Math.Floor(value + 0.5);

    /// <summary>Mirrors JavaScript falsiness for JSON values: null, false, 0 and "".</summary>
    private static bool IsFalsy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => true,
        JsonValueKind.Number => value.GetDouble() == 0,
        JsonValueKind.String => value.GetString()?.Length == 0,
        _ => false
    };

    private static double ReadCommitments(JsonElement body) =>
        body.TryGetProperty("existingCommitments", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetDouble()
            : 0;
}
