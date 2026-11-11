using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Telegram.Bot.Types;
using Telegram.Bot;
using BetaTestBot.Data;
using BetaTestBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
namespace BetaTestBot.Bot.Handlers;

public class BuildHandler(IServiceProvider _serviceProvider,
ILogger<ApplyHandler> _logger)
{
    private readonly Dictionary<long, AdminUploadState> _activeUploads = new();

    private class AdminUploadState
    {
        public long ProjectID { get; set; }
        public string? Version { get; set; }
        public string? Changelog { get; set; }
    }

    public bool HasActiveUpload(long telegramId)
    {
        return _activeUploads.ContainsKey(telegramId);
    }

    public async Task StartUploadAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken, string[] args)
    {
        if (message.From == null) return;

        var adminHandler = _serviceProvider.GetRequiredService<AdminHandler>();

        if (!adminHandler.IsAdmin(message.From.Id)) return;

        if (_activeUploads.ContainsKey(message.From.Id))
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "У вас уже есть незавершённый пост билда. Ты!!! Либо продолжаешь его, либо отменяешь (/cancel)",
                cancellationToken: stopToken
            );
            return;
        }
        _activeUploads[message.From.Id] = new AdminUploadState();

        if (args.Length > 0 && long.TryParse(args[0], out long project))
        {
            _activeUploads[message.From.Id].ProjectID = project;
            await HandleUploadAsync(db, _botClient, message, stopToken);
            return;
        }

        await _botClient.SendMessage(
            message.Chat.Id,
            "1. Укажите ID проекта для нового билда\n(Интересный факт: ID проекта можно указать как аргумент)",
            cancellationToken: stopToken
        );
        return;
    }

    public async Task HandleUploadAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var adminHandler = _serviceProvider.GetRequiredService<AdminHandler>();
        if (!adminHandler.IsAdmin(message.From.Id)) return;

        if (!_activeUploads.TryGetValue(message.From.Id, out var state)) return;

        _logger.LogInformation(state.ProjectID.ToString());

        switch (state)
        {
            case { ProjectID: 0 }:
                if (!long.TryParse(message.Text, out long project))
                {
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "Укажите. ID. Проекта.\nИли отменяй заяву нафек, ты что это, не знаешь что ли про /cancel",
                        cancellationToken: stopToken
                    );
                    return;
                }
                state.ProjectID = project;
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "2. Укажите версию нового билда",
                    cancellationToken: stopToken
                );
                break;
            case { Version: null }:
                state.Version = message.Text;
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "3. Напишите чендж лог для нового билда",
                    cancellationToken: stopToken
                );
                break;
            case { Changelog: null }:
                state.Changelog = message.Text;
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "ждусь файл билда :3",
                    cancellationToken: stopToken
                );
                break;
            default:
            state.Changelog = message.Text;
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "это не файл билда! >w<\nили здесь произошёл пролом логики... мяу :^",
                    cancellationToken: stopToken
                );
                break;
        }
    }

    public async Task HandleFileUploadAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
    {
        if (message.From == null || message.Document == null) return;

        var adminHandler = _serviceProvider.GetRequiredService<AdminHandler>();
        if (!adminHandler.IsAdmin(message.From.Id)) return;

        if (!_activeUploads.TryGetValue(message.From.Id, out var state)) return;

        if (state.Version == null || state.Changelog == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Придержите поней!\nСначала требуется заполнить остальную информацию о новой версии",
                cancellationToken: stopToken
            );
            return;
        }

        using var fileStream = new MemoryStream();
        var file = await _botClient.GetInfoAndDownloadFile(message.Document.FileId, fileStream, stopToken);
        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        if (!Directory.Exists(uploadsPath))
            Directory.CreateDirectory(uploadsPath);

        var fileName = $"{state.Version}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{message.Document.FileName ?? "build.bin"}";
        var filePath = Path.Combine(uploadsPath, fileName);
        using var stream = File.Create(filePath);
        await _botClient.DownloadFile(file.FilePath, stream, cancellationToken: stopToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.ID == message.From.Id, stopToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == state.ProjectID, stopToken);

        var build = new Build
        {
            ProjectID = state.ProjectID,
            Version = state.Version,
            ChangeLog = state.Changelog,
            FileUrl = filePath,
            FileSize = message.Document.FileSize ?? 0,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = user?.ID ?? message.From.Id
        };

        db.Builds.Add(build);
        await db.SaveChangesAsync(stopToken);

        var sentCount = await SendBuildNotificationsAsync(db, _botClient, build, stopToken);

        _activeUploads.Remove(message.From.Id);

        await _botClient.SendMessage(
            message.Chat.Id,
            $"Билд для проекта {(project == null ? "что" : project.Name)} успешно выгружен!\n{sentCount} пользователей получили новый билд.",
            cancellationToken: stopToken
        );
    }

    public async Task CancelUploadAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var adminHandler = _serviceProvider.GetRequiredService<AdminHandler>();
        if (!adminHandler.IsAdmin(message.From.Id)) return;

        if (_activeUploads.ContainsKey(message.From.Id))
        {
            _activeUploads.Remove(message.From.Id);
            await _botClient.SendMessage(
                message.Chat.Id,
                "Текущий пост билда успешно отменён.",
                cancellationToken: stopToken
            );
        }
        else
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Ч-что? Отменить пост билда?..",
                cancellationToken: stopToken
            );
            await _botClient.SendMessage(
                message.Chat.Id,
                "А я не могу найти поста билда, так что увы, но нет.",
                cancellationToken: stopToken
            );
        }
    }

    public async Task<int> SendBuildNotificationsAsync(AppDbContext db, ITelegramBotClient _botClient, Build build, CancellationToken stopToken)
    {
        int sentCount = 0;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == build.ProjectID, stopToken);
        var projectName = project?.Name ?? "Какой-то проект";

        var testers = await db.BetaTesters
            .Include(bt => bt.User)
            .Where(bt => bt.ProjectID == build.ProjectID && bt.IsActive)
            .ToListAsync(stopToken);

        foreach (var tester in testers)
        {
            try
            {
                await _botClient.SendMessage(
                    tester.User?.TelegramID,
                    $"Получен новый билд для проекта {projectName}!" +
                    $"\n\nВерсия: {build.Version}" +
                    $"\nСписок изменений:\n{build.ChangeLog}" +
                    $"\n\nДля получения нового билда, используйте /getbuild",
                    cancellationToken: stopToken
                );
                sentCount++;
            }
            catch (Exception)
            {
                _logger.LogInformation(
                    "Ошибка при отправке билда проекта {ProjectName} пользователю {TelegramID}",
                    projectName,
                    tester.User?.TelegramID
                );
            }
        }

        return sentCount;
    }

    public async Task GetLatestBuildAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
    {
        if (message.From == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramID == message.From.Id, stopToken);
        if (user == null) return;

        var tester = await db.BetaTesters.FirstOrDefaultAsync(bt => bt.UserID == user.ID && bt.IsActive, stopToken);

        if (tester == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Вы не являетесь бета-тестером ни одного из проектов.\nЧтобы подать заявку на бета-тест, используйте команду /apply",
                cancellationToken: stopToken
            );
            return;
        }

        var latestBuild = await db.Builds
            .Where(b => b.ProjectID == tester.ProjectID)
            .OrderByDescending(b => b.UploadedAt)
            .FirstOrDefaultAsync(stopToken);

        if (latestBuild == null)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Билда для проекта не найдено.",
                cancellationToken: stopToken
            );
            return;
        }

        var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == tester.ProjectID, stopToken);
        var projectName = project?.Name ?? "Неизвестный проект";

        await _botClient.SendMessage(
            message.Chat.Id,
            $"Отправляю билд версии {latestBuild.Version}\n",
            cancellationToken: stopToken
        );

        try
        {
            using var fileStream = new FileStream(latestBuild.FileUrl, FileMode.Open, FileAccess.Read);
            var inputFile = new InputFileStream(fileStream, Path.GetFileName(latestBuild.FileUrl));
            
            await _botClient.SendDocument(
                message.Chat.Id,
                inputFile,
                cancellationToken: stopToken
            );
        }
        catch (Exception)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "При отправке билда произошла ошибка",
                cancellationToken: stopToken
            );
        }
    }
}
