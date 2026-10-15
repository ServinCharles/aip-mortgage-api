using System.Text.Json;
using AipApi;
using Xunit;

namespace AipApi.Tests;

public class AipDecisionServiceTests
{
    private readonly AipDecisionService _service = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData(45000, 150000, 0, "Accepted")]
    [InlineData(30000, 200000, 0, "Referred")]
    [InlineData(45000, 150000, 1800, "Declined")]
    [InlineData(45000, 202500, 0, "Accepted")] // exactly 4.5x is still within the cap
    public void Decide_ReturnsExpectedDecision(double income, double loan, double commitments, string expected)
    {
        var result = _service.Decide(new AipInput(income, loan, commitments));

        Assert.Equal(expected, result.Decision);
    }

    [Fact]
    public void Decide_DeclineTakesPriorityOverReferral()
    {
        var result = _service.Decide(new AipInput(30000, 200000, 1500));

        Assert.Equal("Declined", result.Decision);
    }

    [Fact]
    public void Decide_CalculatesFiguresLikeTheNodeMock()
    {
        var result = _service.Decide(new AipInput(45000, 150000, 0));

        Assert.Equal(202500, result.BorrowingPower);
        Assert.Equal(150000, result.MaxBorrowing);
        Assert.Equal(3.33, result.LoanToIncomeRatio);
        Assert.Equal(19, result.MonthlyAffordability);
    }

    [Theory]
    [InlineData("""{"loanAmount":150000}""", "Missing required fields")]
    [InlineData("""{"annualIncome":0,"loanAmount":100000}""", "Missing required fields")]
    [InlineData("""{"annualIncome":45000}""", "Missing required fields")]
    [InlineData("""{"annualIncome":"invalid","loanAmount":150000}""", "Invalid data types")]
    [InlineData("""{"annualIncome":45000,"loanAmount":null}""", "Invalid data types")]
    [InlineData("""{"annualIncome":45000,"loanAmount":-50000}""", "Invalid values")]
    [InlineData("""{"annualIncome":-1,"loanAmount":1000}""", "Invalid values")]
    public void ParseDecisionRequest_RejectsInvalidInput(string json, string expectedError)
    {
        var (input, error) = AipDecisionService.ParseDecisionRequest(Json(json));

        Assert.Null(input);
        Assert.Equal(expectedError, error?.Error);
    }

    [Fact]
    public void ParseDecisionRequest_DefaultsMissingCommitmentsToZero()
    {
        var (input, error) = AipDecisionService.ParseDecisionRequest(Json("""{"annualIncome":45000,"loanAmount":150000}"""));

        Assert.Null(error);
        Assert.Equal(0, input!.MonthlyCommitments);
    }

    [Fact]
    public void CalculateBorrowingPower_SubtractsAnnualisedCommitments()
    {
        var (result, error) = AipDecisionService.CalculateBorrowingPower(Json("""{"annualIncome":50000,"existingCommitments":200}"""));

        Assert.Null(error);
        Assert.Equal(225000, result!.BorrowingPower);
        Assert.Equal(222600, result.MaxLoan);
    }

    [Fact]
    public void CalculateBorrowingPower_NeverReturnsNegativeMaxLoan()
    {
        var (result, _) = AipDecisionService.CalculateBorrowingPower(Json("""{"annualIncome":10000,"existingCommitments":5000}"""));

        Assert.Equal(0, result!.MaxLoan);
    }

    [Fact]
    public void CalculateBorrowingPower_RequiresIncome()
    {
        var (result, error) = AipDecisionService.CalculateBorrowingPower(Json("""{"existingCommitments":200}"""));

        Assert.Null(result);
        Assert.Equal("Missing annualIncome", error?.Error);
    }

    [Theory]
    [InlineData(2.5, 3)]
    [InlineData(-2.5, -2)]
    [InlineData(18.75, 19)]
    public void JsRound_MatchesJavaScriptMathRound(double value, long expected)
    {
        Assert.Equal(expected, AipDecisionService.JsRound(value));
    }
}
