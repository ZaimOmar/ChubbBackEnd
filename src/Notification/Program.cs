using Chubb.Notification.Api;
using Chubb.Notification.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceAuthentication();
builder.Services.AddNotificationPersistence();
builder.Services.AddClaimEventConsumer(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Runs before the host accepts traffic.
await app.StartNotificationStoreAsync();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapNotificationEndpoints();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

await app.RunAsync();
