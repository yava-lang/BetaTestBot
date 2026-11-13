namespace BetaTestBot.Bot.Handlers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Telegram.Bot;
using Telegram.Bot.Types;
using BetaTestBot.Data;
using BetaTestBot.Data.Entities;

public class AdminHandler(IConfiguration _config)
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

    public async Task ShowApplicationsAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken, string[] args)
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

    public async Task ApproveApplicationAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken, string[] args)
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
        await db.SaveChangesAsync();

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

    public async Task RejectApplicationAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken, string[] args)
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
        await db.SaveChangesAsync();

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
}