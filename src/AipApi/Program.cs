using System.Text.Json;
using AipApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AipDecisionService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")));

var app = builder.Build();

app.UseCors();

// Liveness/readiness probe for Kubernetes
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "aip-api" }));

app.MapPost("/api/mortgages/dip/decision", async (HttpRequest request, AipDecisionService service) =>
{
    var body = await ReadJsonObject(request);
    if (body is null)
    {
        return Results.BadRequest(new ApiError("Invalid JSON"));
    }

    var (input, error) = AipDecisionService.ParseDecisionRequest(body.Value);
    return error is not null
        ? Results.BadRequest(error)
        : Results.Ok(service.Decide(input!));
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
