using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AipApi.Tests;

public class AipEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Decision_ReturnsCamelCaseContract()
    {
        var response = await _client.PostAsJsonAsync("/api/mortgages/dip/decision",
            new { annualIncome = 45000, loanAmount = 150000, existingCommitments = 0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Accepted", body.GetProperty("decision").GetString());
        foreach (var field in new[] { "reason", "borrowingPower", "maxBorrowing", "loanToIncomeRatio", "monthlyAffordability" })
        {
            Assert.True(body.TryGetProperty(field, out _), $"Missing field {field}");
        }
    }

    [Fact]
    public async Task Decision_IncludesAnApplicationId()
    {
        var response = await _client.PostAsJsonAsync("/api/mortgages/dip/decision",
            new { annualIncome = 45000, loanAmount = 150000, existingCommitments = 0 });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches("^AIP-[0-9]{8}-[0-9A-F]{8}$", body.GetProperty("applicationId").GetString());
    }

    [Fact]
    public async Task GetApplication_PersistenceOff_Returns404()
    {
        // Persistence is off by default, so nothing is saved and nothing can be found
        var decision = await _client.PostAsJsonAsync("/api/mortgages/dip/decision",
            new { annualIncome = 45000, loanAmount = 150000 });
        var applicationId = (await decision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationId").GetString();

        var response = await _client.GetAsync($"/api/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetApplication_InvalidId_Returns400()
    {
        var response = await _client.GetAsync("/api/applications/not-an-id");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decision_InvalidJson_Returns400()
    {
        var response = await _client.PostAsync("/api/mortgages/dip/decision",
            new StringContent("{not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decision_ValidationError_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/mortgages/dip/decision",
            new { annualIncome = 45000, loanAmount = -50000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CalculateBorrowingPower_ReturnsFigures()
    {
        var response = await _client.PostAsJsonAsync("/api/mortgages/dip/calculate-borrowing-power",
            new { annualIncome = 50000, existingCommitments = 200 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(222600, body.GetProperty("maxLoan").GetInt64());
    }

    [Fact]
    public async Task UnknownRoute_Returns404Json()
    {
        var response = await _client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
