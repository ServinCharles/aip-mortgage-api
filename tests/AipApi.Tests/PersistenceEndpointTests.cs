using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AipApi.Applications;
using AipApi.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AipApi.Tests;

/// <summary>
/// The API with persistence switched on, using in-memory stand-ins for MongoDB and Kafka.
/// Proves what is saved and published, and that a MongoDB or Kafka failure never blocks a decision.
/// </summary>
public class PersistenceEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly object Accepted = new { annualIncome = 45000, loanAmount = 150000, existingCommitments = 0 };

    private HttpClient Client(IApplicationStore store, IEventPublisher publisher) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(store);
            services.AddSingleton(publisher);
        })).CreateClient();

    private static async Task<string> DecideAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/mortgages/dip/decision", Accepted);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("applicationId").GetString()!;
    }

    [Fact]
    public async Task Decision_SavesTheApplicationAndPublishesOneEvent()
    {
        var store = new InMemoryStore();
        var publisher = new RecordingPublisher();

        var applicationId = await DecideAsync(Client(store, publisher));

        var saved = Assert.Single(store.Applications.Values);
        Assert.Equal(applicationId, saved.Id);
        Assert.Equal(45000, saved.Input.AnnualIncome);
        Assert.Equal("Accepted", saved.Result.Decision);

        var published = Assert.Single(publisher.Events);
        Assert.Equal(applicationId, published.ApplicationId);
        Assert.Equal("Accepted", published.Decision);
    }

    [Fact]
    public async Task GetApplication_ReturnsTheSavedForm()
    {
        var client = Client(new InMemoryStore(), new RecordingPublisher());
        var applicationId = await DecideAsync(client);

        var response = await client.GetAsync($"/api/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(applicationId, body.GetProperty("id").GetString());
        Assert.Equal(45000, body.GetProperty("input").GetProperty("annualIncome").GetDouble());
        Assert.Equal(150000, body.GetProperty("input").GetProperty("loanAmount").GetDouble());
        Assert.Equal("Accepted", body.GetProperty("result").GetProperty("decision").GetString());
        Assert.Equal(202500, body.GetProperty("result").GetProperty("borrowingPower").GetInt64());
    }

    [Fact]
    public async Task MongoDbDown_DecisionStillReturnedAndEventStillPublished()
    {
        var publisher = new RecordingPublisher();

        var applicationId = await DecideAsync(Client(new FailingStore(), publisher));

        Assert.True(ApplicationId.IsValid(applicationId));
        Assert.Single(publisher.Events);
    }

    [Fact]
    public async Task KafkaDown_DecisionStillReturnedAndApplicationStillSaved()
    {
        var store = new InMemoryStore();

        await DecideAsync(Client(store, new FailingPublisher()));

        Assert.Single(store.Applications);
    }

    [Fact]
    public async Task GetApplication_MongoDbDown_Returns503()
    {
        var client = Client(new FailingStore(), new RecordingPublisher());

        var response = await client.GetAsync("/api/applications/AIP-20261019-7F3C2A9B");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task InvalidDecision_SavesAndPublishesNothing()
    {
        var store = new InMemoryStore();
        var publisher = new RecordingPublisher();

        var response = await Client(store, publisher).PostAsJsonAsync("/api/mortgages/dip/decision",
            new { annualIncome = 45000, loanAmount = -50000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Applications);
        Assert.Empty(publisher.Events);
    }

    private sealed class InMemoryStore : IApplicationStore
    {
        public ConcurrentDictionary<string, AipApplication> Applications { get; } = new();

        public Task SaveAsync(AipApplication application, CancellationToken cancellationToken)
        {
            Applications[application.Id] = application;
            return Task.CompletedTask;
        }

        public Task<AipApplication?> FindAsync(string applicationId, CancellationToken cancellationToken) =>
            Task.FromResult(Applications.GetValueOrDefault(applicationId));
    }

    private sealed class FailingStore : IApplicationStore
    {
        public Task SaveAsync(AipApplication application, CancellationToken cancellationToken) =>
            throw new TimeoutException("MongoDB unreachable");

        public Task<AipApplication?> FindAsync(string applicationId, CancellationToken cancellationToken) =>
            throw new TimeoutException("MongoDB unreachable");
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public ConcurrentQueue<AipDecisionMadeEvent> Events { get; } = new();

        public void Publish(AipDecisionMadeEvent decisionEvent) => Events.Enqueue(decisionEvent);
    }

    private sealed class FailingPublisher : IEventPublisher
    {
        public void Publish(AipDecisionMadeEvent decisionEvent) =>
            throw new InvalidOperationException("Kafka unreachable");
    }
}
