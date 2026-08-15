using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;

public record ApplyDeliveryResultCommand(int DayId, DeliveryResult Result, string? PhotoUrl, string? SignatureUrl)
    : IRequest<ApplyDeliveryResultResult>;

public record ApplyDeliveryResultResult(int DayId, DayStatus Status);

public class ApplyDeliveryResultCommandHandler(ICalendarRepository calendarRepository, IApplicationDbContext context, IClock clock)
    : IRequestHandler<ApplyDeliveryResultCommand, ApplyDeliveryResultResult>
{
    public async Task<ApplyDeliveryResultResult> Handle(ApplyDeliveryResultCommand request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByDeliveryDayIdAsync(request.DayId, cancellationToken)
            ?? throw new NotFoundException(request.DayId.ToString(), nameof(DeliveryDay));

        var day = calendar.Days.First(d => d.Id == request.DayId);

        var evidence = request.PhotoUrl is not null || request.SignatureUrl is not null
            ? new DeliveryEvidence(clock.Now, request.PhotoUrl, request.SignatureUrl)
            : null;

        day.ApplyDeliveryResult(request.Result, evidence, clock.Now);

        await context.SaveChangesAsync(cancellationToken);

        return new ApplyDeliveryResultResult(day.Id, day.Status);
    }
}
