using Chubb.Reporting.Api.Problems;
using Chubb.Reporting.Application;
using FluentValidation;

namespace Chubb.Reporting.Api;

public static class ApiSetup
{
    public static IServiceCollection AddReportingApi(this IServiceCollection services)
    {
        services.AddExceptionHandler<ReportingExceptionHandler>();
        services.AddProblemDetails();
        services.AddSingleton<IValidator<PageRequest>, PageRequestValidator>();
        services.AddReportingAuthorization();
        return services;
    }
}
