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
        private bool isSilent = false;

        private readonly object _tgLock = new();

        public bool IsWorking { get; private set; } = false;
        public EventHandler<EraseEventArgs> EraseStart { get; set; }
        public EventHandler EraseCancel { get; set; }
        public EventHandler<EraseBlockEventArgs> EraseBlock { get; set; }
        public EventHandler SessionsLogOff { get; set; }
        public EventHandler ServiceRestart { get; set; }

        public TelegramBotService(TelegramSettings telegramSettings, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("TelegramBot");
            _settings = telegramSettings;
        }

        public void Start()
        {
            lock (_tgLock)
            {
                if (IsWorking)
                    return;

                if (_settings == null || string.IsNullOrEmpty(_settings.Token))
                {
                    _logger.Log(LogLevel.Warning, $"Telegram bot not started because of missing config");
                    return;
                }

                try
                {
                    cts = new CancellationTokenSource();
                    bot = new TelegramBotClient(_settings.Token, cancellationToken: cts.Token);

                    me = bot.GetMe().GetAwaiter().GetResult();
                    //await bot.DeleteWebhook();
                    //await bot.DropPendingUpdates();

                    bot.OnError += OnError;
                    bot.OnMessage += OnMessage;
                    bot.OnUpdate += OnUpdate;

                    _logger.Log(LogLevel.Information, $"Telegram bot {me.Username} was started");
                    IsWorking = true;
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex, "Error on Start telegram bot");
                    UnlockedStop();
                }
            }
        }

        public void Stop()
        {
            lock (_tgLock)
            {
                UnlockedStop();
            }
        }

        private void UnlockedStop()
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

            IsWorking = false;
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
                    string startText = """
                Red Button Start
                """;
                    await Answer(startText, msg);
                    break;
                case "/help":
                    string helpText = """
                <b><u>Bot menu</u></b>:
                /help     - help
                /debug    - send debug info
                /erase    - trigger erase
                /cancel   - cancel running erase task
                /disable  - disable erase
                /enable   - enable erase
                /log_off  - log off all sessions
                /silent   - disable notifications
                /loud     - enable notifications
                /restart  - restart service
                """;
                    await Answer(helpText, msg);
                    break;
                case "/debug":
                    var options = new JsonSerializerOptions();
                    options.WriteIndented = true;
                    options.Encoder = JavaScriptEncoder.Default;
                    options.Converters.Add(new JsonStringEnumConverter());
                    string debugText = JsonSerializer.Serialize(msg, options);
                    await Answer(debugText, msg);
                    break;
                case "/erase":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        string eraseStartText = $"Telegram bot trigger erase start";
                        EraseStart?.Invoke(this, new EraseEventArgs(eraseStartText));
                        //await AnswerNoti(eraseStartText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/cancel":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseCancel?.Invoke(this, EventArgs.Empty);
                        string eraseCancelText = $"Telegram bot trigger erase cancel";
                        _logger.Log(LogLevel.Information, eraseCancelText);
                        await AnswerNoti(eraseCancelText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/disable":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseBlock?.Invoke(this, new EraseBlockEventArgs(true));
                        string eraseBlockText = $"Telegram bot trigger erase block";
                        _logger.Log(LogLevel.Information, eraseBlockText);
                        await AnswerNoti(eraseBlockText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/enable":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        EraseBlock?.Invoke(this, new EraseBlockEventArgs(false));
                        string eraseUnblockText = $"Telegram bot trigger erase unblock";
                        _logger.Log(LogLevel.Information, eraseUnblockText);
                        await AnswerNoti(eraseUnblockText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/log_off":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        SessionsLogOff?.Invoke(this, EventArgs.Empty);
                        string logOffText = $"Telegram bot trigger log off";
                        _logger.Log(LogLevel.Information, logOffText);
                        await AnswerNoti(logOffText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/silent":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        string silentText = $"Telegram bot now can not send notifications";
                        _logger.Log(LogLevel.Information, silentText);
                        await AnswerNoti(silentText, msg);
                        SetSilent(true);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/loud":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        SetSilent(false);
                        string loudText = $"Telegram bot now can send notifications";
                        _logger.Log(LogLevel.Information, loudText);
                        await AnswerNoti(loudText, msg);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                case "/restart":
                    if (_settings.AdminIds != null && _settings.AdminIds.Contains(msg.From?.Id.ToString()))
                    {
                        string restartText = $"Telegram bot trigger restart servce";
                        _logger.Log(LogLevel.Information, restartText);
                        await AnswerNoti(restartText, msg);
                        ServiceRestart?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        await AnswerNoPermissions(msg);
                    }
                    break;
                default:
                    await OnCommand("/help", "", msg);
                    break;
            }
        }

        private void SetSilent(bool silent)
        {
            isSilent = silent;
        }

        private async Task Answer(string text, Message msg)
        {
            await bot.SendMessage(msg.Chat, text, parseMode: ParseMode.Html, linkPreviewOptions: true, messageThreadId: msg.MessageThreadId,
                        replyParameters: new ReplyParameters() { MessageId = msg.Id },
                        replyMarkup: new ReplyKeyboardRemove()); // also remove keyboard to clean-up things
        }

        private async Task AnswerNoPermissions(Message msg)
        {
            await Answer("""
                No permissions
                """, msg);
        }

        private async Task AnswerNoti(string text, Message msg)
        {
            if (isSilent)
            {
                await Answer(text, msg);
            }
            else
            {
                await SendMessage(text);
            }
        }

        public async Task SendMessage(string message)
        {
            if (bot == null) return;
            if (string.IsNullOrEmpty(message)) return;
            if (_settings == null) return;
            if (_settings.AdminIds == null) return;
            if (isSilent) return;

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
