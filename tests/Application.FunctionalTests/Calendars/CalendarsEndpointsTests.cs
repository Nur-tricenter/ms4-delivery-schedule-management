using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Application.FunctionalTests.Calendars;

/// <summary>
/// Exercises the actual HTTP surface (routing, DTO binding, JSON, status codes,
/// <see cref="delivery_schedule_management.Api.Infrastructure.ProblemDetailsExceptionHandler"/> mapping)
/// through <see cref="WebApiFactory"/>'s <c>HttpClient</c>, rather than dispatching MediatR requests
/// directly like <see cref="CalendarAcceptanceTests"/> does. Covers what Stage 5 (API layer) never got
/// automated coverage for.
/// </summary>
public class CalendarsEndpointsTests : TestBase
{
    // Mirrors the server's ConfigureHttpJsonOptions (Web/DependencyInjection.cs): enums serialize as
    // strings, so the client's deserializer needs the same converter or it throws on e.g. "Pending".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static HttpClient CreateClient() => FunctionalTestSetup.CreateHttpClient();

    [Test]
    public async Task PostSchedule_ShouldReturn200_WithCalendarIdAndDays()
    {
        using var client = CreateClient();

        var request = new CreateScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        var response = await client.PostAsJsonAsync("/api/v1/schedule", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        body.ShouldNotBeNull();
        body.CalendarId.ShouldBeGreaterThan(0);
        body.Days.Count.ShouldBe(15);
    }

    [Test]
    public async Task GetCalendar_ShouldReturn404_WithProblemDetails_WhenMissing()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/calendars/999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task CancelDay_ShouldReturn409_WithProblemDetails_WhenLessThan48HoursRemain()
    {
        using var client = CreateClient();

        var createRequest = new CreateScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow));

        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[0].Id;

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(dayId));

        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await cancelResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task SetAddress_ShouldReturn422_WithProblemDetails_WhenDayAlreadyCancelled()
    {
        using var client = CreateClient();

        // Day 25 (index) lands 25 days after start, safely outside the 48h lock window, so it can
        // be cancelled through the API and then re-targeted to prove the invalid-transition mapping.
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        var createRequest = new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30, startDate);

        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[25].Id;

        var firstCancel = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(dayId));
        firstCancel.StatusCode.ShouldBe(HttpStatusCode.OK);

        var setAddressResponse = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "1 Main St", "Metropolis", 5, 5, null, null));

        setAddressResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await setAddressResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }
}
