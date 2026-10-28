using Chubb.Gateway.Api;
using Chubb.Gateway.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.Services.AddGatewayProblemDetails();
builder.Services.AddGatewayAuthentication();
builder.Services.AddFrontendCors();
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ProxyTransforms.Configure);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOptions<OpenApiOptions>().BindConfiguration(OpenApiOptions.SectionName)
        .ValidateDataAnnotations()
        .Validate<IHostEnvironment>(
            (options, environment) => Directory.Exists(options.ResolveContractsPath(environment)),
            "OpenApi:ContractsPath does not point to a directory.")
        .ValidateOnStart();
    builder.Services.AddSingleton<AggregatedOpenApi>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Merge the committed contracts now so a bad contract stops startup instead of failing on first request.
    _ = app.Services.GetRequiredService<AggregatedOpenApi>();
}

// First, so nothing registered after it can leak a stack trace (the developer page sits outside this).
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLogging();
app.UseStatusCodePages();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseCors(CorsSetup.FrontendPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapPayeeDetailsNotImplemented();
app.MapReverseProxy();
app.MapFallback("{**path}", () => Results.NotFound()).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/openapi/v1.json", (AggregatedOpenApi openApi) => Results.Text(openApi.Json, "application/json"))
        .AllowAnonymous();
    // Registered after routing and authorization: its fallback-matched requests are anonymous, so it serves them.
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Chubb Claims API");
    });
}

await app.RunAsync();

public partial class Program;
