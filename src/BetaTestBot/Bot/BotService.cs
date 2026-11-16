namespace BetaTestBot.Bot;

using BetaTestBot.Data;
using Microsoft.Extensions.Configuration;
using Serilog;
using BetaTestBot.Bot.Handlers;
using System.Text;
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
        if (update.Type != UpdateType.Message || update.Message?.From == null) return;
        if (update.Message.Text == null && update.Message.Document == null) return;

        var message = update.Message;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramID == message.From.Id, stopToken);

        if (user == null)
        {
            user = new BetaTestBot.Data.Entities.User
            {
                TelegramID = message.From.Id,
                Username = message.From.Username,
                FirstName = message.From.FirstName,
                LastName = message.From.LastName,
                Role = "User",
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(stopToken);
        }

        var ActiveApp = user != null
            ? await db.Applications.FirstOrDefaultAsync(a => a.UserID == user.ID && a.Status == "Draft", stopToken)
            : null;

        var applyHandler = scope.ServiceProvider.GetRequiredService<ApplyHandler>();
        var adminHandler = scope.ServiceProvider.GetRequiredService<AdminHandler>();
        var buildHandler = scope.ServiceProvider.GetRequiredService<BuildHandler>();
        var feedbackHandler = scope.ServiceProvider.GetRequiredService<FeedbackHandler>();

        await LogInfo($"{message.From.Id}: {message.Text}");

        if (buildHandler.HasActiveUpload(message.From.Id))
        {
            if (message.Text == "/cancel") await buildHandler.CancelUploadAsync(db, _botClient, message, stopToken);
            else if (message.Document != null) await buildHandler.HandleFileUploadAsync(db, _botClient, message, stopToken);
            else if (message.Text != null) await buildHandler.HandleUploadAsync(db, _botClient, message, stopToken);
            return;
        }

        if (ActiveApp != null)
        {

            if (message.Text == "/cancel") await applyHandler.CancelApplicationAsync(db, _botClient, message, stopToken);
            else await applyHandler.HandleAnswerAsync(db, _botClient, message, stopToken);
            return;
        }
        
        if (feedbackHandler.HasActiveFeedback(message.From.Id))
        {
            if (message.Text == "/cancel") await feedbackHandler.CancelFeedbackAsync(db, _botClient, message, stopToken);
            else if (message.Text == "/skip") await feedbackHandler.SkipScreenshotAsync(db, _botClient, message, stopToken);
            else if (message.Document != null || message.Photo != null) await feedbackHandler.HandleFeedbackFileAsync(db, _botClient, message, stopToken);
            else if (message.Text != null) await feedbackHandler.HandleFeedbackAnswerAsync(db, _botClient, message, stopToken);
            return;
        }

        if (message.Document != null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "зачем мне твой файл лол кек",
                cancellationToken: stopToken
            );
            return;
        }
        else if (message.Text == null)
        {
            return;
        }

        var parts = message.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower(); // "/approve"
        var args = parts.Skip(1).ToArray(); // ["5"]

        switch (command)
        {
            case "/start" or "/help":
                var help = new StringBuilder();
                help.Append($"Привет, {user?.FirstName}!");
                help.Append($"\nВаш статус: {user?.Role}");
                help.Append("\n\nДоступные вам команды:");
                help.Append("\n\n/help - выводит данное сообщение");
                help.Append("\n/apply - начать накатывать заяву (процесс можно отменить командой /cancel)");
                var userId = user?.ID;
                if (await db.BetaTesters.AnyAsync(bt => bt.UserID == userId && bt.IsActive, stopToken))
                {
                    help.Append("\n\n[TESTER] /getbuild - получить последний билд проекта");
                    help.Append("\n[TESTER] /feedback - отправить фидбек по проекту (процесс можно отменить командой /cancel)");
                }
                if (adminHandler.IsAdmin(message.From.Id))
                {
                    help.Append("\n\n[ADMIN] /applications - просмотр списка заявлений (по умолчанию для Pending)");
                    help.Append("\n[ADMIN] /approve - принятие заявления (требует аргумент ID заявления)");
                    help.Append("\n[ADMIN] /reject - отказ заявления (требует аргумент ID заявления)");
                    help.Append("\n[ADMIN] /uploadbuild - начать выгрузку билда (опционально принимает ID проекта)");
                    help.Append("\n[ADMIN] /testers - просмотр списка тестеров (требует аргумент ID проекта)");
                    help.Append("\n[ADMIN] /removetester - отключить тестера (требует аргумент UserID)");
                    help.Append("\n[ADMIN] /feedbacks - список фидбеков (по умолчанию для New)");
                    help.Append("\n[ADMIN] /feedbackinfo - подробная информация о фидбеке (требует аргумент ID)");
                    help.Append("\n[ADMIN] /resolve - обновить статус фидбека (требует аргументы: ID, новый статус, и необязательно комментарий)");
                }

                await _botClient.SendMessage(
                    message.Chat.Id,
                    help.ToString(),
                    cancellationToken: stopToken
                );
                break;
            case "/apply":
                await applyHandler.StartApplicationAsync(db, _botClient, message, stopToken);
                break;
            case "/cancel":
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "Ошибка. Нечего отменять.\nCancel culture пошёл по сбою.",
                    cancellationToken: stopToken
                );
                break;

            case "/getbuild":
                await buildHandler.GetLatestBuildAsync(db, _botClient, message, stopToken);
                break;
            case "/feedback":
                await feedbackHandler.StartFeedbackAsync(db, _botClient, message, stopToken);
                break;

            case "/applications":
                await adminHandler.ShowApplicationsAsync(db, _botClient, message, args, stopToken);
                break;
            case "/approve":
                await adminHandler.ApproveApplicationAsync(db, _botClient, message, args, stopToken);
                break;
            case "/reject":
                await adminHandler.RejectApplicationAsync(db, _botClient, message, args, stopToken);
                break;
            case "/uploadbuild":
                await buildHandler.StartUploadAsync(db, _botClient, message, args, stopToken);
                break;
            case "/testers":
                await adminHandler.ShowTestersAsync(db, _botClient, message, args, stopToken);
                break;
            case "/removetester":
                await adminHandler.RemoveTesterAsync(db, _botClient, message, args, stopToken);
                break;
            case "/feedbacks":
                await adminHandler.ShowFeedbacksAsync(db, _botClient, message, args, stopToken);
                break;
            case "/feedbackinfo":
                await adminHandler.ShowFeedbackInfoAsync(db, _botClient, message, args, stopToken);
                break;
            case "/resolve":
                await adminHandler.ResolveFeedbackAsync(db, _botClient, message, args, stopToken);
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

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken stopToken)
    {
        _logger.LogError(exception, "Ошибка сети.");
        return Task.CompletedTask;
    }
}