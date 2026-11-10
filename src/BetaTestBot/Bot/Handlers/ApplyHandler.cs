using Microsoft.Extensions.Logging;
using BetaTestBot.Data;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Telegram.Bot.Types;
using Telegram.Bot;
using Microsoft.EntityFrameworkCore;
namespace BetaTestBot.Bot.Handlers;
using BetaTestBot.Data.Entities;

public class ApplyHandler(ILogger<ApplyHandler> _logger)
{
    private async Task LogInfo(string info)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{info}", info);
        }
        return;
    }

    public async Task StartApplicationAsync(AppDbContext db, ITelegramBotClient bot, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramID == message.From.Id, stopToken);

        if (user == null)
        {
            user = new User
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

        if (await db.Applications
            .AsNoTracking()
            .AnyAsync(a => a.UserID == user.ID && a.Status == "Draft", stopToken))
        {
            await bot.SendMessage(
                message.Chat.Id,
                "У вас уже есть незавершённая заявка. Ты!!! Либо продолжаешь её, либо отменяешь её (/cancel)",
                cancellationToken: stopToken
            );
            return;
        }

        var app = new Application
        {
            UserID = user.ID,
            ProjectID = 1,
            Status = "Draft",
            CurrentStep = "WaitingForName",
            CreatedAt = DateTime.UtcNow
        };

        db.Applications.Add(app);
        await db.SaveChangesAsync(stopToken);

        await bot.SendMessage(
            message.Chat.Id,
            "Вопрос 1. Каково ваше наименование?",
            cancellationToken: stopToken
        );
    }

    public async Task HandleAnswerAsync(AppDbContext db, ITelegramBotClient bot, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.TelegramID == message.From.Id, stopToken);
        if (user == null) return;

        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.UserID == user.ID && a.Status == "Draft", stopToken);
        if (app == null) return;
        switch (app.CurrentStep)
        {
            case "WaitingForName":
                app.Name = message.Text;
                if (app.Name == null || app.Name.Length < 2) return;
                app.CurrentStep = "WaitingForPlatform";
                await db.SaveChangesAsync(stopToken);
                await bot.SendMessage(
                    message.Chat.Id,
                    "Вопрос 2. Ваши устройства для тестирования? (Например: Windows, Linux, Android)",
                    cancellationToken: stopToken
                );
                break;
            case "WaitingForPlatform":
                app.Platform = message.Text;
                app.CurrentStep = "WaitingForExperience";
                await db.SaveChangesAsync(stopToken);
                await bot.SendMessage(
                    message.Chat.Id,
                    "Вопрос 3. Сколько у вас опыта тестирования?\nЖелательно писать подробно и с деталями, но и ответы \"Нет опыта\" принимаются.",
                    cancellationToken: stopToken
                );
                break;
            case "WaitingForExperience":
                app.Experience = message.Text;
                app.CurrentStep = "WaitingForMotivation";
                await db.SaveChangesAsync(stopToken);
                await bot.SendMessage(
                    message.Chat.Id,
                    "Вопрос 4. Откуда вообще у вас взялась мотивация подать заявку?",
                    cancellationToken: stopToken
                );
                break;
            case "WaitingForMotivation":
                app.Motivation = message.Text;
                app.CurrentStep = null;
                app.Status = "Pending";
                await db.SaveChangesAsync(stopToken);
                await bot.SendMessage(
                    message.Chat.Id,
                    "Заявка отправлена на рассмотрение.\nПосле рассмотрения, мы отправим вам вердикт",
                    cancellationToken: stopToken
                );
                break;
            default:
                await LogInfo($"Шаг не найден: {app.CurrentStep}");
                break;
        }
    }

    public async Task CancelApplicationAsync(AppDbContext db, ITelegramBotClient bot, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.TelegramID == message.From.Id, stopToken);
        if (user == null) return;

        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.UserID == user.ID && (a.Status == "Draft"), stopToken);
        if (app != null)
        {
            db.Applications.Remove(app);
            await db.SaveChangesAsync(stopToken);
            await bot.SendMessage(
                message.Chat.Id,
                "Заявка успешно удалена",
                cancellationToken: stopToken
            );
        }
        else
        {
            await bot.SendMessage(
                message.Chat.Id,
                "У вас и так нет активных заявок.",
                cancellationToken: stopToken
            );
        }
    }
}
