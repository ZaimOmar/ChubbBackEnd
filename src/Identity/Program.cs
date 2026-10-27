using Chubb.Identity.Api;
using Chubb.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

const long MaxBodyBytes = 4096;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.Services.AddIdentityServices(isDevelopment: builder.Environment.IsDevelopment());
builder.Services.AddIdentityProblemDetails();
builder.Services.AddIdentityOpenApi();

var app = builder.Build();

// First, so nothing registered after it can leak a stack trace (the developer page sits outside this).
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLogging();
app.UseStatusCodePages();
app.UseMiddleware<RequestBodyLimitMiddleware>(MaxBodyBytes);
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(app.Lifetime.ApplicationStopping);
    }

    app.UseSwagger();
}

app.MapAuthEndpoints();
app.MapFallback("{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

await app.RunAsync();

public partial class Program;
