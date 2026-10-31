using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data;
using Joseco.Communication.External.Contracts.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nur.Tricenter.Integration.Messages.Catalog;
using Nur.Tricenter.Integration.Messages.Contracting;
using AppValidationException = delivery_schedule_management.Application.Common.Exceptions.ValidationException;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// <c>ms4.contract-activated</c> → creates the delivery calendar of a paid contract, through the same
/// <see cref="CreateScheduleCommand"/> the REST endpoint uses (idempotent by contract id).
/// </summary>
/// <remarks>
/// A request the command rejects (invalid duration, or a duration that does not match the plan) would
/// be rejected again on every redelivery, so it is logged and acknowledged instead of looping.
/// </remarks>
public sealed class ContractActivatedConsumer(
    ISender sender,
    ILogger<ContractActivatedConsumer> logger) : IIntegrationMessageConsumer<ContractActivated>
{
    public async Task HandleAsync(ContractActivated message, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await sender.Send(new CreateScheduleCommand(
                message.ContractId, message.PatientId, message.CatalogPlanId, message.DurationDays, message.StartDate),
                cancellationToken);

            logger.LogInformation("Contract {ContractId} activated: delivery calendar {CalendarId} ({DayCount} days).",
                message.ContractId, result.CalendarId, result.Days.Count);
        }
        catch (AppValidationException ex)
        {
            logger.LogWarning(ex, "Contract {ContractId} activated with data MS4 rejects ({Errors}); no calendar created.",
                message.ContractId, string.Join("; ", ex.Errors.SelectMany(e => e.Value)));
        }
    }
}

/// <summary>
/// <c>ms4.meal-plan-composition-changed</c> → upserts the plan's day menus into <see cref="PlanDayMenu"/>,
/// which <see cref="ProjectedCatalogPort"/> reads. Each message is a full snapshot of the plan, so a
/// snapshot older than the one already stored for a day (out-of-order delivery) is ignored.
/// </summary>
public sealed class MealPlanCompositionChangedConsumer(
    ApplicationDbContext context,
    ILogger<MealPlanCompositionChangedConsumer> logger) : IIntegrationMessageConsumer<MealPlanCompositionChanged>
{
    public async Task HandleAsync(MealPlanCompositionChanged message, CancellationToken cancellationToken = default)
    {
        var existing = await context.PlanDayMenus
            .Where(m => m.PlanId == message.PlanId)
            .ToDictionaryAsync(m => m.DayNumber, cancellationToken);

        var updated = 0;
        foreach (var day in message.Days)
        {
            if (!existing.TryGetValue(day.DayNumber, out var menu))
            {
                menu = new PlanDayMenu(message.PlanId, day.DayNumber);
                context.PlanDayMenus.Add(menu);
            }
            else if (menu.IsNewerThan(message.CreatedAt))
            {
                continue;
            }

            menu.Replace(ToMenuItems(day), message.CreatedAt);
            updated++;
        }

        logger.LogInformation("Projected composition of MS2 plan {PlanId}: {Updated} of {Total} day(s) updated.",
            message.PlanId, updated, message.Days.Count);
    }

    private static IEnumerable<MenuItem> ToMenuItems(MealPlanDay day) =>
        day.MealTimes
            .SelectMany(mealTime => mealTime.Recipes
                .Where(recipe => recipe.Rations > 0 && recipe.RecipeId != Guid.Empty)
                .Select(recipe => new MenuItem(
                    string.IsNullOrWhiteSpace(mealTime.Name) ? $"Meal time {mealTime.Order}" : mealTime.Name,
                    mealTime.Order,
                    recipe.RecipeId,
                    string.IsNullOrWhiteSpace(recipe.RecipeName) ? $"Recipe {recipe.RecipeId:N}" : recipe.RecipeName,
                    recipe.Rations)));
}
