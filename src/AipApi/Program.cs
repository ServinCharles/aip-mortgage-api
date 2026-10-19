using System.Text.Json;
using AipApi;
using AipApi.Applications;
using AipApi.Events;
using ApplicationId = AipApi.Applications.ApplicationId;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AipDecisionService>();

// Feature flag: PERSISTENCE_ENABLED=true saves applications to MongoDB and publishes decision events to Kafka.
// Off (the default), the no-op versions are used and the API never connects to either.
var persistence = PersistenceOptions.From(builder.Configuration);
builder.Services.AddSingleton(persistence);
if (persistence.Enabled)
{
    builder.Services.AddSingleton<IApplicationStore, MongoApplicationStore>();
    builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IApplicationStore, NoOpApplicationStore>();
    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
}
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")));

var app = builder.Build();

app.UseCors();

if (persistence.Enabled)
{
    app.Logger.LogInformation("Persistence ON: applications saved to MongoDB database '{Database}', events published to Kafka topic '{Topic}'",
        persistence.MongoDatabase, persistence.KafkaTopic);
}
else
{
    app.Logger.LogInformation("Persistence OFF: applications are not saved and no events are published");
}

// Liveness/readiness probe for Kubernetes
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "aip-api", persistence = persistence.Enabled }));

app.MapPost("/api/mortgages/dip/decision", async (
    HttpRequest request,
    AipDecisionService service,
    IApplicationStore store,
    IEventPublisher events,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var body = await ReadJsonObject(request);
    if (body is null)
    {
        return Results.BadRequest(new ApiError("Invalid JSON"));
    }

    var (input, error) = AipDecisionService.ParseDecisionRequest(body.Value);
    if (error is not null)
    {
        return Results.BadRequest(error);
    }

    var decision = service.Decide(input!);
    var now = DateTime.UtcNow;
    var application = AipApplication.Create(ApplicationId.New(now), now, input!, decision);

    // Saving and publishing never block the decision: failures are logged and the customer still gets an answer
    try
    {
        await store.SaveAsync(application, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Application {ApplicationId} was not saved", application.Id);
    }

    try
    {
        events.Publish(AipDecisionMadeEvent.From(application));
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Decision event for {ApplicationId} was not published", application.Id);
    }

    return Results.Ok(AipDecisionResponse.From(application.Id, decision));
});

app.MapGet("/api/applications/{applicationId}", async (
    string applicationId,
    IApplicationStore store,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!ApplicationId.IsValid(applicationId))
    {
        return Results.BadRequest(new ApiError("Invalid application ID", [$"Expected {ApplicationId.Format}"]));
    }

    try
    {
        var saved = await store.FindAsync(applicationId, cancellationToken);
        return saved is null
            ? Results.NotFound(new ApiError("Application not found"))
            : Results.Ok(saved);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Could not read application {ApplicationId}", applicationId);
        return Results.Json(new ApiError("Application store unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/mortgages/dip/calculate-borrowing-power", async (HttpRequest request) =>
{
    var body = await ReadJsonObject(request);
    if (body is null)
    {
        return Results.BadRequest(new ApiError("Invalid JSON"));
    }

    var (result, error) = AipDecisionService.CalculateBorrowingPower(body.Value);
    return error is not null ? Results.BadRequest(error) : Results.Ok(result);
});

app.MapFallback(() => Results.NotFound(new ApiError("Not Found")));

app.Run();

static async Task<JsonElement?> ReadJsonObject(HttpRequest request)
{
    try
    {
        using var document = await JsonDocument.ParseAsync(request.Body);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.Clone()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

// Exposes Program to WebApplicationFactory in the test project
public partial class Program { }
