using System.Security.Cryptography;
using System.Text;
using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>
/// Stands in for MS2 when <c>Catalog:UseMock</c> is true (standalone Aspire runs and tests).
/// Returns a deterministic three-meal menu per plan day so the daily resolution job and the
/// active-tomorrow flow are exercisable end-to-end without MS2 or identity online.
/// </summary>
public class MockCatalogPort : ICatalogPort
{
    private static readonly (string MealTime, int Order)[] MealTimes =
        [("Breakfast", 1), ("Lunch", 3), ("Dinner", 5)];

    public Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken)
    {
        var items = MealTimes
            .Select(meal => new CatalogMenuItem(
                meal.MealTime,
                meal.Order,
                StableGuid($"{planId:N}/{dayNumber}/{meal.MealTime}"),
                $"{meal.MealTime} recipe (plan day {dayNumber})",
                Rations: meal.Order == 3 ? 2 : 1))
            .ToList();

        return Task.FromResult<CatalogDayMenu?>(new CatalogDayMenu(items));
    }

    // Same input, same recipe id, across runs and processes (GetHashCode is randomised per process).
    private static Guid StableGuid(string seed) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
}
