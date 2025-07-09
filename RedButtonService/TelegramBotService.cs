using RedButtonService.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RedButtonService
{
    internal class TelegramBotService
    {
        private readonly ILogger _logger;
        private TelegramSettings _settings;

        private CancellationTokenSource cts;
        private User me;
        private TelegramBotClient bot;

        public EventHandler EraseStart { get; set; }
        public EventHandler EraseCancel { get; set; }
        public EventHandler<EraseBlockEventArgs> EraseBlock { get; set; }
        public EventHandler SessionsLogOff { get; set; }

        public TelegramBotService(TelegramSettings telegramSettings, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("TelegramBot");
            _settings = telegramSettings;
        }

        public async void Start()
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.Token))
            {
                _logger.Log(LogLevel.Warning, $"Telegram bot not started because of missing config");
                return;
            }

            try
            {
                cts = new CancellationTokenSource();
                bot = new TelegramBotClient(_settings.Token, cancellationToken: cts.Token);

                me = await bot.GetMe();
                //await bot.DeleteWebhook();
                //await bot.DropPendingUpdates();

                bot.OnError += OnError;
                bot.OnMessage += OnMessage;
                bot.OnUpdate += OnUpdate;

                _logger.Log(LogLevel.Information, $"Telegram bot {me.Username} was started");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error on Start telegram bot");
                Stop();
            }
        }

        public void Stop()
        {
            cts?.Cancel();
            cts = null;

            try
            {
                if (bot != null)
                {
                    bot.OnError -= OnError;
                    bot.OnMessage -= OnMessage;
                    bot.OnUpdate -= OnUpdate;
                    bot = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error on Stop telegram bot");
            }
        }

        async Task OnError(Exception exception, HandleErrorSource source)
        {
            _logger.Log(LogLevel.Error, exception, "Error in telegram bot");
            await Task.Delay(2000, cts.Token);
        }

        async Task OnUpdate(Update update)
        {
            _logger.Log(LogLevel.Debug, $"Received unhandled update {update.Type}");
        }

        async Task OnMessage(Message msg, UpdateType type)
        {
            if (msg.Text is not { } text)
                _logger.Log(LogLevel.Debug, $"Received a message of type {msg.Type}");
            else if (text.StartsWith('/'))
            {
                var space = text.IndexOf(' ');
                if (space < 0) space = text.Length;
                var command = text[..space].ToLower();
                if (command.LastIndexOf('@') is > 0 and int at) // it's a targeted command
                    if (command[(at + 1)..].Equals(me.Username, StringComparison.OrdinalIgnoreCase))
                        command = command[..at];
                    else
                        return; // command was not targeted at me
                await OnCommand(command, text[space..].TrimStart(), msg);
            }
            else
                await OnCommand("/help", "", msg);
        }

        async Task OnCommand(string command, string args, Message msg)
        {
            _logger.Log(LogLevel.Debug, $"Received command: {command} {args}");
            switch (command)
            {
                case "/start":
                    await bot.SendMessage(msg.Chat, """
                Red Button Start
                """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                        replyParameters: new ReplyParameters() { MessageId = msg.Id },
                        replyMarkup: new ReplyKeyboardRemove()); // also remove keyboard to clean-up things
                    break;
                case "/help":
                    await bot.SendMessage(msg.Chat, """
                <b><u>Bot menu</u></b>:
                /help    - help
                /debug   - send debug info
                /erase   - trigger erase
                /cancel  - cancel running erase task
                /disable - disable erase
                /enable  - enable erase
                /log_off  - log off all sessions
                """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                        replyParameters: new ReplyParameters() { MessageId = msg.Id },
                        replyMarkup: new ReplyKeyboardRemove());
                    break;
                case "/debug":
                    var options = new JsonSerializerOptions();
                    options.WriteIndented = true;
                    options.Encoder = JavaScriptEncoder.Default;
                    options.Converters.Add(new JsonStringEnumConverter());
                    await bot.SendMessage(msg.Chat, JsonSerializer.Serialize(msg, options), parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                        replyParameters: new ReplyParameters() { MessageId = msg.Id },
                        replyMarkup: new ReplyKeyboardRemove());
                    break;
                case "/erase":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseStart?.Invoke(this, EventArgs.Empty);
                        _logger.Log(LogLevel.Information, $"Telegram bot trigger erase start");
                        await bot.SendMessage(msg.Chat, $"Telegram bot trigger erase start", parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await bot.SendMessage(msg.Chat, """
                    No permissions
                    """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    break;
                case "/cancel":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseCancel?.Invoke(this, EventArgs.Empty);
                        _logger.Log(LogLevel.Information, $"Telegram bot trigger erase cancel");
                        await bot.SendMessage(msg.Chat, $"Telegram bot trigger erase cancel", parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await bot.SendMessage(msg.Chat, """
                    No permissions
                    """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    break;
                case "/disable":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseBlock?.Invoke(this, new EraseBlockEventArgs(true));
                        _logger.Log(LogLevel.Information, $"Telegram bot trigger erase block");
                        await bot.SendMessage(msg.Chat, $"Telegram bot trigger erase block", parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await bot.SendMessage(msg.Chat, """
                    No permissions
                    """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    break;
                case "/enable":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseBlock?.Invoke(this, new EraseBlockEventArgs(false));
                        _logger.Log(LogLevel.Information, $"Telegram bot trigger erase unblock");
                        await bot.SendMessage(msg.Chat, $"Telegram bot trigger erase unblock", parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await bot.SendMessage(msg.Chat, """
                    No permissions
                    """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    break;
                case "/log_off":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        SessionsLogOff?.Invoke(this, EventArgs.Empty);
                        _logger.Log(LogLevel.Information, $"Telegram bot trigger log off");
                        await bot.SendMessage(msg.Chat, $"Telegram bot trigger log off", parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await bot.SendMessage(msg.Chat, """
                    No permissions
                    """, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                            replyParameters: new ReplyParameters() { MessageId = msg.Id },
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    break;
                default:
                    await OnCommand("/help", "", msg);
                    break;
            }
        }

        public async Task SendMessage(string message)
        {
            if (bot == null) return;
            if (string.IsNullOrEmpty(message)) return;
            if (_settings == null) return;
            if (_settings.AdminIds == null) return;

            foreach (var adminIdStr in _settings.AdminIds)
            {
                try
                {
                    if (long.TryParse(adminIdStr, out var adminId))
                    {
                        await bot.SendMessage(adminId, message, parseMode: ParseMode.Html, linkPreviewOptions: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex, "Error on SendMessage telegram bot");
                }
            }
        }
    }
}
