using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using AppValidationException = delivery_schedule_management.Application.Common.Exceptions.ValidationException;

namespace delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;

public record CreateScheduleCommand(Guid ContractId, Guid PatientId, Guid PlanId, int Duration, DateOnly StartDate)
    : IRequest<CreateScheduleResult>;

public record CreateScheduleResult(int CalendarId, List<DeliveryDayDto> Days);

public class CreateScheduleCommandHandler(
    ICalendarRepository calendarRepository,
    IApplicationDbContext context,
    IPlanCatalog planCatalog,
    ILogger<CreateScheduleCommandHandler> logger)
    : IRequestHandler<CreateScheduleCommand, CreateScheduleResult>
{
    public async Task<CreateScheduleResult> Handle(CreateScheduleCommand request, CancellationToken cancellationToken)
    {
        // Idempotent by contractId: replay of the same request returns the existing calendar
        // instead of creating a duplicate.
        var existing = await calendarRepository.GetByContractIdAsync(request.ContractId, cancellationToken);
        if (existing is not null)
        {
            return ToResult(existing);
        }

        await EnsureDurationMatchesPlanAsync(request, cancellationToken);

        var calendar = DeliveryCalendar.Create(
            request.PatientId,
            new ContractReference(request.ContractId),
            new PlanReference(request.PlanId),
            new PlanDuration(request.Duration),
            new DeliveryDate(request.StartDate));

        calendarRepository.Add(calendar);

        await context.SaveChangesAsync(cancellationToken);

        return ToResult(calendar);
    }

    /// <summary>
    /// A 15-day calendar for a 30-day plan would leave MS2 days that are never delivered (or ask MS2
    /// for days that don't exist). Checked against the local projection of MS2's plans; a plan the
    /// projection hasn't seen yet is accepted with a warning, since absence there means "unknown".
    /// </summary>
    private async Task EnsureDurationMatchesPlanAsync(CreateScheduleCommand request, CancellationToken cancellationToken)
    {
        var plan = await planCatalog.FindAsync(request.PlanId, cancellationToken);
        if (plan is null)
        {
            logger.LogWarning(
                "Plan {PlanId} is not in MS4's projection of MS2 plans yet; creating contract {ContractId}'s calendar without checking its duration.",
                request.PlanId, request.ContractId);
            return;
        }

        if (plan.TotalDays != request.Duration)
        {
            throw new AppValidationException([new ValidationFailure(nameof(request.Duration),
                $"Plan {plan.PlanId} ('{plan.Name}') lasts {plan.TotalDays} days, but the requested duration is {request.Duration}.")]);
        }
    }

    private static CreateScheduleResult ToResult(DeliveryCalendar calendar) =>
        new(calendar.Id, calendar.Days.Select(d => d.ToDto()).ToList());
}
