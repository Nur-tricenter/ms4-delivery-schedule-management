using delivery_schedule_management.Shared;

namespace delivery_schedule_management.TestAppHost;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder.AddPostgres(Services.DatabaseServer)
            .AddDatabase(Services.Database);

        builder.Build().Run();
    }
}