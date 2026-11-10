using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using BetaTestBot.Data;
namespace BetaTestBot.Bot.Handlers;

public class AdminHandler(IConfiguration _config)
{

    private bool IsAdmin(long telegramID)
    {
        return _config.GetSection("BotSettings:AdminIds").Get<List<long>>()?.Contains(telegramID) is true;
    }

    public async Task ShowApplicationsAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

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
    }
}
