namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Read side of the MS5 delivery ↔ MS4 delivery day links, projected from <c>DeliveryAddedToRoute</c>
/// (queue <c>ms4.delivery-added-to-route</c>). Lets a caller follow a day to the MS5 delivery and route
/// that carry it.
/// </summary>
/// <remarks>
/// Eventually consistent: a day whose package was just validated may not be linked yet. Callers must
/// treat "no link" as "not routed yet", never as an error.
/// </remarks>
public interface IDeliveryRouteLinks
{
    /// <summary>The most recent link for <paramref name="dayId"/>, or <c>null</c> when MS5 has not routed it.</summary>
    Task<DeliveryRouteLinkSummary?> FindLatestForDayAsync(int dayId, CancellationToken cancellationToken);
}

public record DeliveryRouteLinkSummary(Guid DeliveryId, Guid RouteId, int DayId, DateTimeOffset LinkedAt);
