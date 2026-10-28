using Chubb.Reporting.Api;
using Chubb.Reporting.Api.Endpoints;
using Chubb.Reporting.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceAuthentication();
builder.Services.AddReportingApi();
builder.Services.AddReportingPersistence();
builder.Services.AddClaimEventConsumer(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Runs before the host accepts traffic.
await app.StartReportingStoreAsync();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapDashboardEndpoints();
app.MapWorkloadEndpoints();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

await app.RunAsync();
