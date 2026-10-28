using Chubb.Claims.Api;
using Chubb.Claims.Application;
using Chubb.Claims.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ApiSetup.MaxRequestBodyBytes);
builder.Services.AddServiceAuthentication();
builder.Services.AddClaimsApi();
builder.Services.AddClaimsPersistence();
builder.Services.AddClaimsCommands();
builder.Services.AddOutboxPublisher(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddClaimsSeeding();
}

builder.Services.AddHealthChecks();

var app = builder.Build();

// Runs before the host accepts traffic.
await app.MigrateClaimsDatabaseAsync();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestBufferingMiddleware>();

app.MapClaimCommandEndpoints();
app.MapClaimQueryEndpoints();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

await app.RunAsync();
