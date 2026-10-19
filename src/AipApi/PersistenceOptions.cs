namespace AipApi;

/// <summary>
/// Feature flag and connection settings for saving applications (MongoDB) and publishing decision events (Kafka).
/// Read from environment variables. With PERSISTENCE_ENABLED unset or false, the API behaves exactly as before:
/// it decides and responds, and never connects to MongoDB or Kafka.
/// </summary>
public sealed record PersistenceOptions(
    bool Enabled,
    string MongoUri,
    string MongoDatabase,
    string KafkaBootstrapServers,
    string KafkaTopic,
    int ApplicationRetentionDays)
{
    public static PersistenceOptions From(IConfiguration configuration) => new(
        configuration.GetValue("PERSISTENCE_ENABLED", false),
        configuration["MONGODB_URI"] ?? "mongodb://localhost:27017",
        configuration["MONGODB_DATABASE"] ?? "aip",
        configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092",
        configuration["KAFKA_TOPIC"] ?? "aip.decisions",
        // 0 keeps applications forever; test environments set e.g. 7 so test data cleans itself up
        configuration.GetValue("APPLICATION_RETENTION_DAYS", 0));
}
