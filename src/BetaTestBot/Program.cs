using BetaTestBot.Bot;
using BetaTestBot.Data.Entities;
using BetaTestBot.Data;
using BetaTestBot.Bot.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Telegram.Bot;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureAppConfiguration((config) => config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true))
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

            var botToken = config["BotSettings:token"];
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken!));

            services.AddHostedService<BotService>();

            services.AddTransient<ApplyHandler>();
            services.AddTransient<AdminHandler>();
        })
        .Build();
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        if (!await db.Projects.AnyAsync())
        {
            db.Projects.Add(new Project { Name = "Тестовая игра", Description = "Проект для бета-тестирования" });
            await db.SaveChangesAsync();
        }

        var adminHandler = scope.ServiceProvider.GetRequiredService<AdminHandler>();
        await adminHandler.UpdateRolesAsync(db, CancellationToken.None);
    }
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "По сервису прошёл крит. урон");
}
finally
{
    Log.CloseAndFlush();
}