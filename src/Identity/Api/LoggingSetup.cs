using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Chubb.Identity.Api;

public static class LoggingSetup
{
    public static void AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((_, services, configuration) => configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .ReadFrom.Services(services)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));
    }

    public static void UseRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                diagnosticContext.Set("userId", httpContext.User.FindFirst(Contracts.ClaimNames.Subject)?.Value));
    }
}
