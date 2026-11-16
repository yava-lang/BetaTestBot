    using Telegram.Bot.Types.Enums;
    namespace BetaTestBot.Bot.Handlers;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.DependencyInjection;
    using System.IO;
    using Telegram.Bot.Types;
    using Telegram.Bot;
    using BetaTestBot.Data;
    using BetaTestBot.Data.Entities;
    using Microsoft.EntityFrameworkCore;

    public class FeedbackHandler(IServiceProvider _serviceProvider,
    ILogger<FeedbackHandler> _logger)
    {
        private readonly Dictionary<long, FeedbackState> _activeFeedbacks = new();

        private class FeedbackState
        {
            public long? ProjectID { get; set; }
            public string? Type { get; set; }
            public string? Description { get; set; }
            public string? AttachmentUrl { get; set; }
        }

        public bool HasActiveFeedback(long telegramId)
        {
            return _activeFeedbacks.ContainsKey(telegramId);
        }

        public async Task StartFeedbackAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
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

            if (_activeFeedbacks.ContainsKey(message.From.Id))
            {
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "У тебя уже есть незавершённый фидбек.\nВы можете либо завершить фидбек, либо отменить его (/cancel)",
                    cancellationToken: stopToken
                );
                return;
            }

            _activeFeedbacks[message.From.Id] = new FeedbackState{ProjectID = tester.ProjectID};

            await _botClient.SendMessage(
                message.Chat.Id,
                "1. Тип фидбека" +
                "\nЕсли это баг-репорт, напишите <blockquote>баг</blockquote>" +
                "\nЕсли это общий фидбек, предложение или комментарий, напишите <blockquote>фидбек</blockquote>",
                parseMode: ParseMode.Html,
                cancellationToken: stopToken
            );
        }

        public async Task HandleFeedbackAnswerAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
        {
            if (message.From == null) return;

            if (!_activeFeedbacks.TryGetValue(message.From.Id, out var state)) return;

            switch (state)
            {
                case { Type: null }:
                    var typeText = message.Text?.ToLower();
                    if (typeText == "баг" || typeText == "bug")
                    {
                        state.Type = "Bug";
                        await _botClient.SendMessage(
                            message.Chat.Id,
                            "2. Опишите проблему максимально подробно, то есть:\n- Описание бага\n- Шаги воспроизведения\n- Ожидаемый результат\n- Фактический результат",
                            cancellationToken: stopToken
                        );
                    }
                    else if (typeText == "фидбек" || typeText == "feedback")
                    {
                        state.Type = "Feedback";
                        await _botClient.SendMessage(
                            message.Chat.Id,
                            "2. Напиши своё предложение или комментарий." +
                            "\nНапоминаем о конструктивности фидбека: нам важен ваш фидбек, и потому будет хорошо, если мы сможем правильно вас понять.",
                            cancellationToken: stopToken
                        );
                    }
                    else
                    {
                        await _botClient.SendMessage(
                            message.Chat.Id,
                            "Это не тип фидбека.\nПроверьте наличие опечаток, а в случае чего, задоксите админа и спросите лично, почему не работает.",
                            cancellationToken: stopToken
                        );
                    }
                    break;

                case { Description: null }:
                    state.Description = message.Text;
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "3. По необходимости, прикрепите вложение (скриншот, клип или документ)." +
                        "\nВажно: текущее ограничение не позволяет отправлять файлы больше 20 мегабайт" +
                        "\nЧтобы пропустить этот шаг, напишите / skip",
                        cancellationToken: stopToken
                    );
                    break;

                default:
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        "Это не файл, и не команда /skip",
                        cancellationToken: stopToken
                    );
                    break;
            }
        }

        public async Task HandleFeedbackFileAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
        {
            if (message.From == null) return;
            if (message.Document == null && message.Photo == null) return;

            if (!_activeFeedbacks.TryGetValue(message.From.Id, out var state)) return;

            if (state.Description == null)
            {
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "Придержите поней!\nСначала заполните описание фидбека, потом уже добавляй вложение.",
                    cancellationToken: stopToken
                );
                return;
            }

            string? filePath = null;

            if (message.Document != null)
            {
                var file = await _botClient.GetFile(message.Document.FileId, stopToken);
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "feedbacks");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                var fileName = $"feedback_{message.From.Username}_{message.Document.FileName ?? "file.bin"}";
                filePath = Path.Combine(uploadsPath, fileName);

                using var stream = File.Create(filePath);
                await _botClient.DownloadFile(file, stream, cancellationToken: stopToken);
            }
            else if (message.Photo?.Length > 0)
            {
                var photo = message.Photo[^1];
                var file = await _botClient.GetFile(photo.FileId, stopToken);
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "feedbacks");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                var fileName = $"feedback_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                filePath = Path.Combine(uploadsPath, fileName);

                using var stream = File.Create(filePath);
                await _botClient.DownloadFile(file, stream, cancellationToken: stopToken);
            }

            state.AttachmentUrl = filePath;
            await FinalizeFeedbackAsync(db, _botClient, message, stopToken);
        }

        public async Task SkipScreenshotAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
        {
            if (message.From == null) return;

            if (!_activeFeedbacks.TryGetValue(message.From.Id, out var state)) return;

            state.AttachmentUrl = null;
            await FinalizeFeedbackAsync(db, _botClient, message, stopToken);
        }

        private async Task FinalizeFeedbackAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
        {
            if (message.From == null) return;

            if (!_activeFeedbacks.TryGetValue(message.From.Id, out var state)) return;
            if (state.Description == null || state.Type == null) return;

            var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramID == message.From.Id, stopToken);
            if (user == null) return;

            var feedback = new Feedback
            {
                UserID = user.ID,
                ProjectID = state.ProjectID ?? 1,
                Type = state.Type,
                Description = state.Description,
                AttachmentUrl = state.AttachmentUrl,
                Status = "New",
                CreatedAt = DateTime.UtcNow
            };

            db.Feedbacks.Add(feedback);
            await db.SaveChangesAsync(stopToken);

            var project = await db.Projects.FirstOrDefaultAsync(p => p.ID == feedback.ProjectID, stopToken);
            var projectName = project?.Name ?? "Неизвестный проект";

            var adminIds = new List<long>();
            foreach (var adminIdSection in _serviceProvider
                .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
                .GetSection("BotSettings:AdminIds").GetChildren())
            {
                if (long.TryParse(adminIdSection.Value, out var adminId)) adminIds.Add(adminId);
            }
            
            if (adminIds.Count > 0)
            {
                foreach (var adminId in adminIds)
                {
                    try
                    {
                        await _botClient.SendMessage(
                            adminId,
                            $"Новый {(state.Type == "Bug" ? "баг-репорт" : "фидбек")}!" +
                            $"\n\nID: #{feedback.ID}" +
                            $"\nПроект: {projectName}" +
                            $"\nОт: @{user.Username ?? "без username"}" +
                            $"\n\nОписание:\n{state.Description}",
                            cancellationToken: stopToken
                        );

                        if (state.AttachmentUrl != null && File.Exists(state.AttachmentUrl))
                        {
                            using var fileStream = new FileStream(state.AttachmentUrl, FileMode.Open, FileAccess.Read);
                            var inputFile = new InputFileStream(fileStream, Path.GetFileName(state.AttachmentUrl));
                            await _botClient.SendDocument(adminId, inputFile, cancellationToken: stopToken);
                        }
                    }
                    catch (Exception)
                    {
                        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Не удалось отправить уведомление админу {AdminId}", adminId);
                    }
                }
            }

            _activeFeedbacks.Remove(message.From.Id);

            await _botClient.SendMessage(
                message.Chat.Id,
                $"{(state.Type == "Bug" ? "Баг-репорт" : "Фидбек")} отправлен админам.",
                cancellationToken: stopToken
            );
        }

        public async Task CancelFeedbackAsync(AppDbContext db, ITelegramBotClient _botClient, Message message, CancellationToken stopToken)
        {
            if (message.From == null) return;

            if (_activeFeedbacks.ContainsKey(message.From.Id))
            {
                _activeFeedbacks.Remove(message.From.Id);
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "Заполнение фидбека отменено.",
                    cancellationToken: stopToken
                );
            }
            else
            {
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "У вас нет активного фидбека для отмены.",
                    cancellationToken: stopToken
                );
            }
        }
    }