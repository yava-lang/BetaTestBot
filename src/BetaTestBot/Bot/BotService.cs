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
    IServiceProvider _serviceProvider,
    ApplyHandler _applyHandler,
    IConfiguration _config) : BackgroundService
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

    private bool IsAdmin(long telegramID)
    {
        return _config.GetSection("BotSettings:AdminIds").Get<List<long>>()?.Contains(telegramID) is true;
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
                        "Привет!\nВот список команд доступных на данный момент:\n/help - выводит это сообщение\n/apply - начинает процесс заявки",
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
                    if (!IsAdmin(message.From.Id))
                    {
                        await _botClient.SendMessage(
                            message.Chat.Id,
                            "Недостаточно прав для выполнения данной команды.",
                            cancellationToken: stopToken
                        );
                    }
                    else
                    {
                        var _apps = await db.Applications.Where(a => a.Status == "Pending").ToListAsync(stopToken);

                        string result = string.Join("\n\n", _apps.Select(a => $"ID: {a.ID} | Project: {a.ProjectID} | User: {a.UserID} | Статус: {a.Status}\nИмя: {a.Name}\nПлатформы: {a.Platform}\nОпыт: {a.Experience}\nМотив: {a.Motivation}"));

                        await _botClient.SendMessage(
                            message.Chat.Id,
                            result,
                            cancellationToken: stopToken
                        );
                    }
                    break;
                default:
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "Неизвестная команда",
                        cancellationToken: stopToken
                    );
                    break;
            }
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken stopToken)
    {
        _logger.LogError(exception, "Ошибка при получении обновления");
        return Task.CompletedTask;
    }
}
