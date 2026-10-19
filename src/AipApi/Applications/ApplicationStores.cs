using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace AipApi.Applications;

public interface IApplicationStore
{
    Task SaveAsync(AipApplication application, CancellationToken cancellationToken);

    Task<AipApplication?> FindAsync(string applicationId, CancellationToken cancellationToken);
}

/// <summary>Used when PERSISTENCE_ENABLED is off: nothing is saved and nothing is found.</summary>
public sealed class NoOpApplicationStore : IApplicationStore
{
    public Task SaveAsync(AipApplication application, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AipApplication?> FindAsync(string applicationId, CancellationToken cancellationToken) =>
        Task.FromResult<AipApplication?>(null);
}

/// <summary>Saves applications to MongoDB, collection "applications", one document per application (_id = application ID).</summary>
public sealed class MongoApplicationStore : IApplicationStore
{
    private readonly IMongoCollection<AipApplication> _collection;
    private readonly int _retentionDays;
    private readonly ILogger<MongoApplicationStore> _logger;
    private volatile bool _indexesReady;

    static MongoApplicationStore()
    {
        // Store fields in camelCase, like the JSON API, and ignore fields added by later versions
        ConventionRegistry.Register(
            "aip-api-applications",
            new ConventionPack { new CamelCaseElementNameConvention(), new IgnoreExtraElementsConvention(true) },
            type => type.Namespace == typeof(AipApplication).Namespace);
    }

    public MongoApplicationStore(PersistenceOptions options, ILogger<MongoApplicationStore> logger)
    {
        var settings = MongoClientSettings.FromConnectionString(options.MongoUri);
        // Fail fast if MongoDB is down, so a customer's decision is never held up for long
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        settings.ConnectTimeout = TimeSpan.FromSeconds(2);

        _collection = new MongoClient(settings)
            .GetDatabase(options.MongoDatabase)
            .GetCollection<AipApplication>("applications");
        _retentionDays = options.ApplicationRetentionDays;
        _logger = logger;
    }

    public async Task SaveAsync(AipApplication application, CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);
        await _collection.InsertOneAsync(application, cancellationToken: cancellationToken);
    }

    public async Task<AipApplication?> FindAsync(string applicationId, CancellationToken cancellationToken) =>
        await _collection.Find(a => a.Id == applicationId).FirstOrDefaultAsync(cancellationToken);

    private async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexesReady || _retentionDays <= 0)
        {
            return;
        }

        try
        {
            // TTL index: MongoDB deletes applications older than the retention period (test data clean-up)
            var ttl = new CreateIndexModel<AipApplication>(
                Builders<AipApplication>.IndexKeys.Ascending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "createdAt_ttl", ExpireAfter = TimeSpan.FromDays(_retentionDays) });
            await _collection.Indexes.CreateOneAsync(ttl, cancellationToken: cancellationToken);
        }
        catch (MongoCommandException ex)
        {
            // e.g. the retention period changed: keep saving, and say how to fix the index
            _logger.LogWarning(ex, "Could not create the createdAt_ttl index; drop it to apply a new APPLICATION_RETENTION_DAYS");
        }

        _indexesReady = true;
    }
}
