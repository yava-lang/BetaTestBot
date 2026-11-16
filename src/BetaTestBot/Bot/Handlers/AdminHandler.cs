using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.Enums;
namespace BetaTestBot.Bot.Handlers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Telegram.Bot;
using Telegram.Bot.Types;
using BetaTestBot.Data;
using BetaTestBot.Data.Entities;

public class AdminHandler(IConfiguration _config,
ILogger<FeedbackHandler> _logger)
{

    public bool IsAdmin(long telegramID)
    {
        return _config.GetSection("BotSettings:AdminIds").Get<List<long>>()?.Contains(telegramID) is true;
    }

    public async Task UpdateRolesAsync(AppDbContext db, CancellationToken stopToken)
    {
        var adminIds = _config.GetSection("BotSettings:AdminIds").Get<List<long>>();
        if (adminIds == null || adminIds.Count == 0) return;

        var adminUsers = await db.Users
            .Where(u => adminIds.Contains(u.TelegramID) && u.Role != "Admin")
            .ToListAsync(stopToken);

        if (adminUsers.Count == 0) return;

        foreach (var user in adminUsers)
        {
            user.Role = "Admin";
        }

        await db.SaveChangesAsync(stopToken);
    }

    public async Task ShowApplicationsAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;

        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        string status = (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])) ? args[0] : "Pending";

        var _apps = await db.Applications.Where(a => a.Status == status).ToListAsync(stopToken);

        string result = string.Join("\n\n", _apps.Select(a => $"ID: {a.ID} | Project: {a.ProjectID} | User: {a.UserID} | Статус: {a.Status}\nИмя: {a.Name}\nПлатформы: {a.Platform}\nОпыт: {a.Experience}\nМотив: {a.Motivation}"));

        if (result == null || result.Length < 1)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                $"Не найдено ни одной заявки со статусом {status}.",
                cancellationToken: stopToken
            );
            return;
        }
        await _botClient.SendMessage(
            message.Chat.Id,
            result,
            cancellationToken: stopToken
        );
    }

    public async Task ApproveApplicationAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;

        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        if (args.Length < 1)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Отсутствуют параметры.\nДля использования команды требуется параметр ID заявки.",
                cancellationToken: stopToken
            );
            return;
        }

        if (!long.TryParse(args[0], out long approveID) || args[0] == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ошибка при обработке параметров команды.\nДля использования команды требуется параметр ID заявки.",
                cancellationToken: stopToken
            );
            return;
        }

        var app = await db.Applications.FirstOrDefaultAsync(a => a.ID == approveID, stopToken);

        if (app == null || app.Status != "Pending")
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ошибка при поиске заявки.\nЗаявка должна существовать и быть в статуе Pending.",
                cancellationToken: stopToken
            );
            return;
        }

        app.Status = "Approved";
        var betatester = await db.BetaTesters.FirstOrDefaultAsync(bt => bt.UserID == app.UserID, stopToken);
        if (betatester == null)
        {
            betatester = new BetaTester
            {
                UserID = app.UserID,
                ProjectID = app.ProjectID,
                Platform = app.Platform ?? "???",
                Group = "Public",
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            };

            db.BetaTesters.Add(betatester);
        }
        await db.SaveChangesAsync(stopToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.ID == app.UserID, stopToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == app.ProjectID, stopToken);

        if (user == null || project == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Кажется, user или project внезапно оказались null, и как-то не особо получается доставить пользователю уведомление...",
                cancellationToken: stopToken
            );
            return;
        }
        await _botClient.SendMessage(
            message.Chat.Id,
            $"Заявка #{app.ID} одобрена.\nЗаявитель получил уведомление о принятии в проект {project.Name}.",
            cancellationToken: stopToken
        );
        await _botClient.SendMessage(
            user.TelegramID,
            $"Ваша заявка ободренна!\nТеперь вы официально являетесь бета-тестером проекта {project.Name} 🤯🤯🤯",
            cancellationToken: stopToken
        );
        return;
    }

    public async Task RejectApplicationAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;

        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        if (args.Length < 1)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Отсутствуют параметры.\nДля использования команды требуется параметр ID заявки.",
                cancellationToken: stopToken
            );
            return;
        }

        if (!long.TryParse(args[0], out long rejectID) || args[0] == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ошибка при обработке параметров команды.\nДля использования команды требуется параметр ID заявки.",
                cancellationToken: stopToken
            );
            return;
        }

        var app = await db.Applications.FirstOrDefaultAsync(a => a.ID == rejectID, stopToken);

        if (app == null || app.Status != "Pending")
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ошибка при поиске заявки.\nЗаявка должна существовать и быть в статуе Pending.",
                cancellationToken: stopToken
            );
            return;
        }

        app.Status = "Rejected";
        await db.SaveChangesAsync(stopToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.ID == app.UserID, stopToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == app.ProjectID, stopToken);

        if (user == null || project == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Кажется, user или project внезапно оказались null, и как-то не особо получается доставить пользователю уведомление...",
                cancellationToken: stopToken
            );
            return;
        }
        await _botClient.SendMessage(
            message.Chat.Id,
            $"Заявка #{app.ID} отклонена.\nЗаявитель в скором времени получит повестку.",
            cancellationToken: stopToken
        );
        await _botClient.SendMessage(
            user.TelegramID,
            $"Наши сожаления, но...\nВаша заявка в проект {project.Name} была отклонена\n\nпоплачь",
            cancellationToken: stopToken
        );
    }

    public async Task ShowTestersAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;
        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        var testers = await db.BetaTesters
            .Include(bt => bt.User)
            .Include(bt => bt.Project)
            .Where(bt => bt.IsActive)
            .ToListAsync(stopToken);

        if (args.Length > 0 && long.TryParse(args[0], out long projectId)) testers = testers.Where(bt => bt.ProjectID == projectId).ToList();

        if (testers.Count == 0)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Активных тестеров не найдено.",
                cancellationToken: stopToken
            );
            return;
        }

        var result = "Активные тестеры:\n\n" +
            string.Join("\n", testers.Select(bt => $"@{bt.User?.Username ?? "???"} (ID: {bt.UserID}) — {bt.Platform} — с {bt.JoinedAt:yyyy-MM-dd}"));

        await _botClient.SendMessage(
            message.Chat.Id,
            result,
            cancellationToken: stopToken
        );
    }

    public async Task RemoveTesterAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;
        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        if (args.Length < 1)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Отсутствуют параметры.\nДля использования команды требуется параметр UserID.",
                cancellationToken: stopToken
            );
            return;
        }

        if (!long.TryParse(args[0], out long userId))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ошибка при обработке параметров.\nUserID должен быть числом.",
                cancellationToken: stopToken
            );
            return;
        }

        var tester = await db.BetaTesters
            .Include(bt => bt.User)
            .Include(bt => bt.Project)
            .FirstOrDefaultAsync(bt => bt.UserID == userId && bt.IsActive, stopToken);

        if (tester == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Тестер не найден или неактивен.",
                cancellationToken: stopToken
            );
            return;
        }

        tester.IsActive = false;
        await db.SaveChangesAsync(stopToken);

        await _botClient.SendMessage(
            message.Chat.Id,
            $"@{tester.User?.Username ?? "???"} отключён от проекта {tester.Project?.Name ?? "???"}.",
            cancellationToken: stopToken
        );

        try
        {
            await _botClient.SendMessage(
                tester.User!.TelegramID,
                $"Вы были отключены от бета-тестирования проекта {tester.Project?.Name ?? "???"}." +
                "Соррямба.",
                cancellationToken: stopToken
            );
        }
        catch (Exception)
        {
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Не удалось уведомить пользователя {UserId} об отключении", userId);
        }
    }

    public async Task ShowFeedbacksAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;
        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        string statusFilter = (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])) ? args[0] : "New";

        var feedbacks = await db.Feedbacks
            .Include(f => f.User)
            .Include(f => f.Project)
            .Where(f => statusFilter == "All" || f.Status == statusFilter)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(stopToken);

        if (feedbacks.Count == 0)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                $"Фидбеков со статусом '{statusFilter}' не найдено.",
                cancellationToken: stopToken);
            return;
        }

        var result = $"Список фидбеков (Статус: {statusFilter}):\n\n" +
            string.Join("\n\n", feedbacks.Select(f =>
                $"#{f.ID} | {f.Type}" +
                $"\n{f.Project?.Name ?? "Неизвестный проект"}" +
                $"\n@{f.User?.Username ?? "без имени"} (ID: {f.UserID})" +
                $"\n<blockquote>{f.Description[..Math.Min(50, f.Description.Length)]}...</blockquote>\n" +
                $"Для полной информации, используйте /feedbackinfo {f.ID}"));

        await _botClient.SendMessage(
            message.Chat.Id,
            result,
            parseMode: ParseMode.Html,
            cancellationToken: stopToken
        );
    }

    public async Task ShowFeedbackInfoAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null || !IsAdmin(message.From.Id) || args.Length == 0) return;

        if (!long.TryParse(args[0], out long feedbackId))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Неверный формат. Используйте: /feedbackinfo с аргументом ID проекта",
                cancellationToken: stopToken
                );
            return;
        }

        var feedback = await db.Feedbacks
            .Include(f => f.User)
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.ID == feedbackId, stopToken);

        if (feedback == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                $"Фидбек #{feedbackId} не найден.",
                cancellationToken: stopToken
            );
            return;
        }

        var info = $"{feedback.Status} - Детали фидбека #{feedback.ID}" +
                   $"\n\nОт: @{feedback.User?.Username ?? "без имени"} (ID: {feedback.UserID})\n" +
                   $"\nПроект: {feedback.Project?.Name ?? "Неизвестный проект"}" +
                   $"\nТип: {feedback.Type}" +
                   $"\nСтатус: {feedback.Status}" +
                   $"\n\nОписание:\n{feedback.Description}";

        if (!string.IsNullOrEmpty(feedback.AdminComment))
        {
            info += $"\n\nКомментарий админа:\n{feedback.AdminComment}\n\n";
        }
        
        await _botClient.SendMessage(
            message.Chat.Id,
            info,
            cancellationToken: stopToken
        );

        try
        {
            if (feedback.AttachmentUrl != null && File.Exists(feedback.AttachmentUrl))
            {
                using var fileStream = new FileStream(feedback.AttachmentUrl, FileMode.Open, FileAccess.Read);
                var inputFile = new InputFileStream(fileStream, Path.GetFileName(feedback.AttachmentUrl));
                await _botClient.SendDocument(message.From.Id, inputFile, cancellationToken: stopToken);
            }
        }
        catch (Exception)
        {
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Не удалось отправить вложение при просмотре заявки {FeedbackId}", feedback.ID);
        }
    }

    public async Task ResolveFeedbackAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, string[] args, CancellationToken stopToken)
    {
        if (message.From == null) return;

        if (!IsAdmin(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Недостаточно прав для выполнения данной команды.",
                cancellationToken: stopToken
            );
            return;
        }

        if (args.Length < 2)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                $"{(args.Length == 0 ? "Отсутствуют параметры" : "Недостаточно параметров")}." +
                "\nФормат команды: /resolve [ID] [Статус] [Комментарий]" +
                "\nДоступные статусы: New, Viewed, Fixed, Rejected",
                cancellationToken: stopToken
            );
            return;
        }

        if (!long.TryParse(args[0], out long feedbackID))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "ID фидбека должно быть числом.",
                cancellationToken: stopToken
            );
            return;
        }

        string newStatus = args[1];
        string comment = args.Length > 2 ? string.Join(" ", args.Skip(2)) : "Комментарий не указан.";

        var feedback = await db.Feedbacks
            .Include(f => f.User)
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.ID == feedbackID, stopToken);

        if (feedback == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Фидбек с таким ID не был выявлен.",
                cancellationToken: stopToken
            );
            return;
        }

        feedback.Status = newStatus;
        feedback.AdminComment = comment;
        await db.SaveChangesAsync(stopToken);

        await _botClient.SendMessage(
            message.Chat.Id,
            $"Фидбек #{feedback.ID} обновлён.\nНовый статус: {newStatus}.\nКомментарий сохранён.",
            cancellationToken: stopToken
        );
    }
}