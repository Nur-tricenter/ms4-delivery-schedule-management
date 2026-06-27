using MediatR;
using Microsoft.Extensions.Logging;

namespace NurtricenterDeliverySchedule.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        var response = await next(cancellationToken);
        logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
        return response;
    }
}
