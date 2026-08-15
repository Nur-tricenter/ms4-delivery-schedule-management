using System.Text.Json.Serialization;
using delivery_schedule_management.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        // Fallback ProblemDetails writer for exceptions ProblemDetailsExceptionHandler doesn't
        // recognise — without this, UseExceptionHandler re-executes the pipeline at a blank
        // path and blows up with a meta-exception instead of returning a clean 500.
        builder.Services.AddProblemDetails();

        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<DocumentInfoTransformer>();
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
        });
    }
}
