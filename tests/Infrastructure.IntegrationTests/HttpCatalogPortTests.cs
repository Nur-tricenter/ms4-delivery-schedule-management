using System.Net;
using System.Text;
using delivery_schedule_management.Infrastructure.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

/// <summary>
/// <see cref="HttpCatalogPort"/> against canned MS2 responses (shape copied from
/// ms2-plans-and-recipes-catalog's <c>ApiResponse&lt;MealPlanCompositionDto&gt;</c>), no network.
/// </summary>
public class HttpCatalogPortTests
{
    private static readonly Guid PlanId = Guid.Parse("8f1c2c55-0000-0000-0000-000000000001");
    private static readonly Guid LunchRecipeId = Guid.Parse("8f1c2c55-0000-0000-0000-0000000000aa");

    // camelCase, as ASP.NET Core serializes it; Ingredients present but unused by MS4.
    private static readonly string CompositionJson = $$"""
        {
          "success": true,
          "message": null,
          "errors": null,
          "data": {
            "mealPlanId": "{{PlanId}}",
            "name": "Keto 15",
            "days": [
              { "dayNumber": 1, "mealTimes": [] },
              { "dayNumber": 2, "mealTimes": [
                  { "mealTimeId": "{{Guid.NewGuid()}}", "name": "Breakfast", "order": 1,
                    "recipes": [ { "recipeId": "{{Guid.NewGuid()}}", "recipeName": "Eggs", "ration": { "quantity": 1 }, "ingredients": [] } ] },
                  { "mealTimeId": "{{Guid.NewGuid()}}", "name": "Lunch", "order": 3,
                    "recipes": [ { "recipeId": "{{LunchRecipeId}}", "recipeName": "Salmon", "ration": { "quantity": 2 },
                                   "ingredients": [ { "foodId": "{{Guid.NewGuid()}}", "foodName": "Salmon", "portion": { "quantity": 150.0 } } ] } ] }
              ] }
            ]
          }
        }
        """;

    private static (HttpCatalogPort Port, StubHandler Handler) CreatePort(HttpStatusCode status, string body = "")
    {
        var handler = new StubHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://gateway:8080") };
        var port = new HttpCatalogPort(client, Options.Create(new CatalogOptions { UseMock = false, BaseUrl = "http://gateway:8080" }),
            NullLogger<HttpCatalogPort>.Instance);
        return (port, handler);
    }

    [Test]
    public async Task GetDayMenu_ShouldFlattenMealTimesAndRecipes_ForTheRequestedDay()
    {
        var (port, handler) = CreatePort(HttpStatusCode.OK, CompositionJson);

        var menu = await port.GetDayMenuAsync(PlanId, 2, CancellationToken.None);

        menu.ShouldNotBeNull();
        menu.Items.Count.ShouldBe(2);
        menu.Items.ShouldContain(i => i.MealTime == "Lunch" && i.MealTimeOrder == 3 && i.RecipeId == LunchRecipeId
                                      && i.RecipeName == "Salmon" && i.Rations == 2);
        handler.Requests.Single().ShouldBe($"http://gateway:8080/api/catalog/v1/meal-plans/{PlanId}");
    }

    [Test]
    public async Task GetDayMenu_ShouldReturnNull_WhenPlanHasNoSuchDay()
    {
        var (port, _) = CreatePort(HttpStatusCode.OK, CompositionJson);

        (await port.GetDayMenuAsync(PlanId, 9, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task GetDayMenu_ShouldReturnNull_WhenMs2DoesNotKnowThePlan()
    {
        var (port, _) = CreatePort(HttpStatusCode.NotFound);

        (await port.GetDayMenuAsync(PlanId, 1, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task GetDayMenu_ShouldThrow_OnServerError_SoTheJobRetriesThatDayLater()
    {
        var (port, _) = CreatePort(HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<HttpRequestException>(() => port.GetDayMenuAsync(PlanId, 1, CancellationToken.None));
    }

    [Test]
    public async Task GetDayMenu_ShouldFetchEachPlanOnce_PerInstance()
    {
        var (port, handler) = CreatePort(HttpStatusCode.OK, CompositionJson);

        await port.GetDayMenuAsync(PlanId, 1, CancellationToken.None);
        await port.GetDayMenuAsync(PlanId, 2, CancellationToken.None);

        handler.Requests.Count.ShouldBe(1);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
