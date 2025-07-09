using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

namespace RedButtonService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service.log")
                )
                .CreateLogger();

            //var builder = Host.CreateApplicationBuilder(args);
            //builder.Services.AddHostedService<Worker>();
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(x => x.AddJsonFile("service.json"))
                .UseWindowsService(options =>
                {
                    options.ServiceName = "TestTest";
                })
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    if (WindowsServiceHelpers.IsWindowsService())
                    {
                        services.RemoveAll<IHostLifetime>();
                        services.AddSingleton<IHostLifetime, CustomService>();
                    }
                });

            var host = builder.Build();
            host.Run();
        }
    }
}