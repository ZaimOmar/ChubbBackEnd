using Chubb.Reporting.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceAuthentication();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

await app.RunAsync();
