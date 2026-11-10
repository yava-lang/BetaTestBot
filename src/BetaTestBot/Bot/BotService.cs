using BetaTestBot.Data;
using Microsoft.Extensions.Configuration;
using Serilog;
using BetaTestBot.Bot.Handlers;
namespace BetaTestBot.Bot;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class BotService(
    ITelegramBotClient _botClient,
    ILogger<BotService> _logger,
    IServiceProvider _serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _logger.LogInformation("Бот Запущен");

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            cancellationToken: stopToken
        );

        await Task.Delay(Timeout.Infinite, stopToken);
    }

    private async Task LogInfo(string info)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{info}", info);
        }
        return;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient _botClient, Update update, CancellationToken stopToken)
    {
        if (update.Type == UpdateType.Message && update.Message is { Text: not null, From: not null })
        {
            var message = update.Message;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramID == message.From.Id, stopToken);

            var ActiveApp = user != null
                ? await db.Applications.FirstOrDefaultAsync(a => a.UserID == user.ID && a.Status == "Draft", stopToken)
                : null;

            var applyHandler = scope.ServiceProvider.GetRequiredService<ApplyHandler>();
            var adminHandler = scope.ServiceProvider.GetRequiredService<AdminHandler>();

            if (ActiveApp != null)
            {

                if (message.Text == "/cancel") await applyHandler.CancelApplicationAsync(db, _botClient, message, stopToken);
                else await applyHandler.HandleAnswerAsync(db, _botClient, message, stopToken);
                return;
            }

            switch (message.Text)
            {
                case "/start" or "/help":
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "Привет!\nВот список команд доступных на данный момент:\n/help - выводит это сообщение\n/apply - начинает процесс заявки\n/cancel - отмена подачи текущей заявки",
                        cancellationToken: stopToken
                    );
                    break;
                case "/apply":
                    await applyHandler.StartApplicationAsync(db, _botClient, message, stopToken);
                    break;
                case "/cancel":
                    await applyHandler.CancelApplicationAsync(db, _botClient, message, stopToken);
                    break;
                case "/applications":
                    await adminHandler.ShowApplicationsAsync(db, _botClient, message, stopToken);
                    break;
                default:
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "Неизвестная команда.",
                        cancellationToken: stopToken
                    );
                    break;
            }
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken stopToken)
    {
        _logger.LogError(exception, "Ошибка сети.");
        return Task.CompletedTask;
    }
}
