using AipApi;
using AipApi.Applications;
using AipApi.Events;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AipApi.Tests;

public class ApplicationIdTests
{
    [Fact]
    public void New_UsesTheDateAndEightHexCharacters()
    {
        var id = ApplicationId.New(new DateTime(2026, 10, 19, 11, 30, 0, DateTimeKind.Utc));

        Assert.Matches("^AIP-20261019-[0-9A-F]{8}$", id);
        Assert.True(ApplicationId.IsValid(id));
    }

    [Fact]
    public void New_IsUnique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => ApplicationId.New(DateTime.UtcNow)).ToHashSet();

        Assert.Equal(1000, ids.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AIP-2026101-7F3C2A9B")]
    [InlineData("AIP-20261019-7f3c2a9b")]
    [InlineData("AIP-20261019-7F3C2A9B-extra")]
    [InlineData("../../etc/passwd")]
    public void IsValid_RejectsAnythingElse(string? value)
    {
        Assert.False(ApplicationId.IsValid(value));
    }
}

public class AipApplicationTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 11, 30, 0, DateTimeKind.Utc);

    private static AipApplication Accepted() =>
        AipApplication.Create("AIP-20261019-7F3C2A9B", Now, new AipInput(45000, 150000, 0),
            new AipDecisionService().Decide(new AipInput(45000, 150000, 0)));

    [Fact]
    public void Create_KeepsTheFormAndTheDecision()
    {
        var application = Accepted();

        Assert.Equal(45000, application.Input.AnnualIncome);
        Assert.Equal(150000, application.Input.LoanAmount);
        Assert.Equal(0, application.Input.MonthlyCommitments);
        Assert.Equal("Accepted", application.Result.Decision);
        Assert.Equal(202500, application.Result.BorrowingPower);
        Assert.Equal(Now, application.CreatedAt);
    }

    [Fact]
    public void DecisionEvent_FollowsTheContract()
    {
        var application = Accepted();

        var decisionEvent = AipDecisionMadeEvent.From(application);

        Assert.Equal("AipDecisionMade", decisionEvent.EventType);
        Assert.Equal(1, decisionEvent.SchemaVersion);
        Assert.Equal(application.Id, decisionEvent.ApplicationId);
        Assert.Equal("Accepted", decisionEvent.Decision);
        Assert.Equal(150000, decisionEvent.LoanAmount);
        Assert.Equal(150000, decisionEvent.MaxBorrowing);
        Assert.Equal(Now, decisionEvent.OccurredAt);
        Assert.True(Guid.TryParse(decisionEvent.EventId, out _));
    }

    [Fact]
    public void DecisionEvent_HasCamelCaseJsonAndNoIncome()
    {
        var json = AipDecisionMadeEvent.From(Accepted()).ToJson();

        foreach (var field in new[] { "\"eventId\"", "\"eventType\":\"AipDecisionMade\"", "\"schemaVersion\":1", "\"occurredAt\"", "\"applicationId\":\"AIP-20261019-7F3C2A9B\"", "\"decision\":\"Accepted\"" })
        {
            Assert.Contains(field, json);
        }

        // Events carry IDs and decision figures only, never the applicant's income
        Assert.DoesNotContain("annualIncome", json);
    }

    [Fact]
    public void DecisionEvent_EachEventHasItsOwnId()
    {
        var application = Accepted();

        Assert.NotEqual(AipDecisionMadeEvent.From(application).EventId, AipDecisionMadeEvent.From(application).EventId);
    }

    [Fact]
    public void Persistence_IsOffByDefault()
    {
        var options = PersistenceOptions.From(new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.Equal("aip.decisions", options.KafkaTopic);
        Assert.Equal("aip", options.MongoDatabase);
        Assert.Equal(0, options.ApplicationRetentionDays);
    }

    [Fact]
    public void Persistence_ReadsTheFlagAndSettings()
    {
        var options = PersistenceOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PERSISTENCE_ENABLED"] = "true",
                ["MONGODB_URI"] = "mongodb://mongodb.sit-01-data.svc.cluster.local:27017",
                ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka.sit-01-data.svc.cluster.local:9092",
                ["APPLICATION_RETENTION_DAYS"] = "7"
            })
            .Build());

        Assert.True(options.Enabled);
        Assert.Equal("mongodb://mongodb.sit-01-data.svc.cluster.local:27017", options.MongoUri);
        Assert.Equal("kafka.sit-01-data.svc.cluster.local:9092", options.KafkaBootstrapServers);
        Assert.Equal(7, options.ApplicationRetentionDays);
    }
}
