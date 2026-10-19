using System.Text;
using System.Text.Json;
using AipApi.Applications;
using Confluent.Kafka;

namespace AipApi.Events;

/// <summary>
/// Published to the aip.decisions topic after every decision, keyed by applicationId so all events for one
/// application stay in order. Contract (schemaVersion 1) shared with aip-audit-service. IDs and figures only.
/// </summary>
public sealed record AipDecisionMadeEvent(
    string EventId,
    string EventType,
    int SchemaVersion,
    DateTime OccurredAt,
    string ApplicationId,
    string Decision,
    double LoanAmount,
    long MaxBorrowing)
{
    public const string Type = "AipDecisionMade";
    public const int CurrentSchemaVersion = 1;

    public static AipDecisionMadeEvent From(AipApplication application) => new(
        Guid.NewGuid().ToString(),
        Type,
        CurrentSchemaVersion,
        application.CreatedAt,
        application.Id,
        application.Result.Decision,
        application.Input.LoanAmount,
        application.Result.MaxBorrowing);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public interface IEventPublisher
{
    /// <summary>Hands the event to the publisher without waiting for Kafka, so the customer's response is never delayed.</summary>
    void Publish(AipDecisionMadeEvent decisionEvent);
}

/// <summary>Used when PERSISTENCE_ENABLED is off.</summary>
public sealed class NoOpEventPublisher : IEventPublisher
{
    public void Publish(AipDecisionMadeEvent decisionEvent)
    {
    }
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly ProducerConfig _config;
    private readonly object _buildLock = new();
    private volatile IProducer<string, string>? _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(PersistenceOptions options, ILogger<KafkaEventPublisher> logger)
    {
        _topic = options.KafkaTopic;
        _logger = logger;

        _config = new ProducerConfig
        {
            BootstrapServers = options.KafkaBootstrapServers,
            ClientId = "aip-api",
            Acks = Acks.All,
            EnableIdempotence = true, // no duplicates from producer retries
            MessageTimeoutMs = 10_000 // give up (and log) after 10s if Kafka is unreachable
        };
    }

    // Built on first use, and retried next time if it fails, so a Kafka problem never breaks API start-up
    private IProducer<string, string> Producer
    {
        get
        {
            if (_producer is not null)
            {
                return _producer;
            }

            lock (_buildLock)
            {
                return _producer ??= new ProducerBuilder<string, string>(_config)
                    .SetErrorHandler((_, error) => _logger.LogWarning("Kafka: {Reason}", error.Reason))
                    .Build();
            }
        }
    }

    public void Publish(AipDecisionMadeEvent decisionEvent)
    {
        var message = new Message<string, string>
        {
            Key = decisionEvent.ApplicationId,
            Value = decisionEvent.ToJson(),
            Headers = new Headers { { "eventType", Encoding.UTF8.GetBytes(decisionEvent.EventType) } }
        };

        Producer.Produce(_topic, message, report =>
        {
            if (report.Error.IsError)
            {
                _logger.LogWarning("Event {EventId} for {ApplicationId} was not published: {Reason}",
                    decisionEvent.EventId, decisionEvent.ApplicationId, report.Error.Reason);
            }
        });
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}
