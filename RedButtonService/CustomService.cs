using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using RedButtonService.Models;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedButtonService
{
    public class CustomService : WindowsServiceLifetime
    {
        private ILoggerFactory _loggerFactory;
        private IConfiguration _configuration;

        private readonly ILogger _logger;
        private ServiceSettings _settings;

        private EraserService _eraserService;
        private TelegramBotService _telegramBotService;
        private USBFlashDriveCheckerService _usbFlashDriveCheckerService;

        public CustomService(
            IConfiguration configuration,
            IHostEnvironment environment,
            IHostApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory,
            IOptions<HostOptions> optionsAccessor
            ) : base(environment, applicationLifetime, loggerFactory, optionsAccessor)
        {
            _loggerFactory = loggerFactory;
            _configuration = configuration;

            _logger = loggerFactory.CreateLogger("RedButtonService");
            _settings = _configuration.Get<ServiceSettings>();

            CanHandleSessionChangeEvent = true;
        }

        protected override void OnStart(string[] args)
        {
            _logger.Log(LogLevel.Debug, $"On Start args: '{(args != null ? string.Join(',', args) : "")}'.");
            base.OnStart(args);

            try
            {
                string userName = SessionUser.GetUserName();
                _logger.Log(LogLevel.Debug, $"User '{userName}', Service is started at " + DateTime.Now);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error OnStart with SessionUser.GetUserName");
            }

            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            _logger.Log(LogLevel.Information, $"Loaded configuration:\n{JsonSerializer.Serialize(_settings, options)}");

            _eraserService = new EraserService(_settings.Eraser, _loggerFactory);
            _eraserService.TGMessageSend += TGMessageSendEvent;
            _eraserService.Start();

            _telegramBotService = new TelegramBotService(_settings.Telegram, _loggerFactory);
            _telegramBotService.EraseStart += EraseStartEvent;
            _telegramBotService.EraseCancel += EraseCancelEvent;
            _telegramBotService.EraseBlock += EraseBlockEvent;
            _telegramBotService.SessionsLogOff += SessionsLogOffEvent;
            _telegramBotService.ServiceRestart += ServiceRestartEvent;
            _telegramBotService.Start();

            _usbFlashDriveCheckerService = new USBFlashDriveCheckerService(_settings.USBTrigger, _loggerFactory);
            _usbFlashDriveCheckerService.TGMessageSend += TGMessageSendEvent;
            _usbFlashDriveCheckerService.EraseStart += EraseStartEvent;
            _usbFlashDriveCheckerService.Start();
        }

        protected override void OnStop()
        {
            base.OnStop();

            try
            {
                _usbFlashDriveCheckerService?.Stop();
                _telegramBotService?.Stop();
                _eraserService?.Stop();

                _usbFlashDriveCheckerService.TGMessageSend -= TGMessageSendEvent;
                _usbFlashDriveCheckerService.EraseStart -= EraseStartEvent;
                _telegramBotService.EraseStart -= EraseStartEvent;
                _telegramBotService.EraseCancel -= EraseCancelEvent;
                _telegramBotService.EraseBlock -= EraseBlockEvent;
                _telegramBotService.SessionsLogOff -= SessionsLogOffEvent;
                _telegramBotService.ServiceRestart -= ServiceRestartEvent;
                _eraserService.TGMessageSend -= TGMessageSendEvent;

                _usbFlashDriveCheckerService = null;
                _telegramBotService = null;
                _eraserService = null;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error OnStop with stopping subservices");
            }

            try
            {
                string userName = SessionUser.GetUserName();
                _logger.Log(LogLevel.Debug, $"User '{userName}', Service is stopped at " + DateTime.Now);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error OnStop with SessionUser.GetUserName");
            }
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            if (_settings.UserLogonTrigger == null || _settings.UserLogonTrigger.Usernames == null || _settings.UserLogonTrigger.Usernames.Count <= 0)
            {
                _logger.Log(LogLevel.Warning, $"User logon checker not started because of missing config");
                return;
            }

            try
            {
                string userName = SessionUser.GetUserName(sessionId: changeDescription.SessionId);
                _logger.Log(LogLevel.Debug, $"User '{userName}' raise an event '{changeDescription.Reason.ToString()}'");
                if (changeDescription.Reason == SessionChangeReason.SessionLogon || changeDescription.Reason == SessionChangeReason.SessionUnlock)
                {
                    if (_settings.UserLogonTrigger.Usernames.Any(userLogonUsername => userName.Contains(userLogonUsername)))
                    {
                        eraseStart();
                        _logger.Log(LogLevel.Information, $"User '{userName}' trigger erase");
                        tgMessageSend($"User '{userName}' trigger erase").GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error OnSessionChange with SessionUser.GetUserName");
            }
        }

        private void SessionsLogOffEvent(object? sender, EventArgs e)
        {
            sessionsLogOff();
        }

        private void sessionsLogOff()
        {
            try
            {
                var sessionIds = SessionUser.GetSessionIds();
                foreach (var sessionId in sessionIds)
                {
                    SessionUser.LogOffSession(sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error on SessionsLogOffEvent");
            }
        }

        private void EraseStartEvent(object? sender, EventArgs e)
        {
            eraseStart();
        }

        private void EraseCancelEvent(object? sender, EventArgs e)
        {
            eraseCancel();
        }

        private void EraseBlockEvent(object? sender, EraseBlockEventArgs e)
        {
            eraseBlock(e.Block);
        }

        private void eraseStart()
        {
            if (_eraserService != null)
            {
                _eraserService.RunErase();
            }
        }

        private void eraseCancel()
        {
            if (_eraserService != null)
            {
                _eraserService.CancelErase();
            }
        }

        private void eraseBlock(bool block)
        {
            if (_eraserService != null)
            {
                _eraserService.BlockErase(block);
            }
        }

        private async Task tgMessageSend(string message)
        {
            if (_telegramBotService != null)
            {
                await _telegramBotService.SendMessage(message);
            }
        }

        private void TGMessageSendEvent(object? sender, TGMessageEventArgs e)
        {
            tgMessageSend(e.Message).GetAwaiter().GetResult();
        }

        private async Task RestartService()
        {
            await Task.Delay(5 * 1000);
            OnStop();
            await Task.Delay(5 * 1000);
            //OnStart();
            Environment.Exit(1);
        }

        private void ServiceRestartEvent(object? sender, EventArgs e)
        {
            Task.Run(() => RestartService());
        }
    }
}
