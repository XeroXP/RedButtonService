using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using RedButtonService.Models;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telegram.Bot.Types;

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

        private CancellationTokenSource cts;

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

            cts = new CancellationTokenSource();

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
            _telegramBotService.PCShutdown += PCShutdownEvent;
            _telegramBotService.ServiceRestart += ServiceRestartEvent;
            _telegramBotService.Start();

            _usbFlashDriveCheckerService = new USBFlashDriveCheckerService(_settings.USBTrigger, _loggerFactory);
            _usbFlashDriveCheckerService.EraseStart += EraseStartEvent;
            _usbFlashDriveCheckerService.Start();

            Task.Run(() => Heartbeat(cts.Token));
        }

        protected override void OnStop()
        {
            base.OnStop();

            cts?.Cancel();
            cts = null;

            try
            {
                _usbFlashDriveCheckerService?.Stop();
                _telegramBotService?.Stop();
                _eraserService?.Stop();

                _usbFlashDriveCheckerService.EraseStart -= EraseStartEvent;
                _telegramBotService.EraseStart -= EraseStartEvent;
                _telegramBotService.EraseCancel -= EraseCancelEvent;
                _telegramBotService.EraseBlock -= EraseBlockEvent;
                _telegramBotService.SessionsLogOff -= SessionsLogOffEvent;
                _telegramBotService.PCShutdown -= PCShutdownEvent;
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
            try
            {
                string userName = SessionUser.GetUserName(sessionId: changeDescription.SessionId);
                _logger.Log(LogLevel.Debug, $"User '{userName}' raise an event '{changeDescription.Reason.ToString()}'");
                //SessionChangeReason.RemoteConnect;
                if (changeDescription.Reason == SessionChangeReason.SessionLogon || changeDescription.Reason == SessionChangeReason.SessionUnlock)
                {
                    tgLogUnlock($"User {userName}: {(changeDescription.Reason == SessionChangeReason.SessionLogon ? "Logon" : "Unlock")}").GetAwaiter().GetResult();

                    if (_settings.UserLogonTrigger == null || _settings.UserLogonTrigger.Usernames == null || _settings.UserLogonTrigger.Usernames.Count <= 0)
                    {
                        _logger.Log(LogLevel.Warning, $"User logon checker not started because of missing config");
                        return;
                    }
                    if (_settings.UserLogonTrigger.Usernames.Any(userLogonUsername => userName.Contains(userLogonUsername)))
                    {
                        eraseStart($"User '{userName}' trigger erase");
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

        private void PCShutdownEvent(object? sender, EventArgs e)
        {
            pcShutdown();
        }

        private void pcShutdown()
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5 * 1000);
                    ExitWindows.Shutdown(true);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex, "Error on PCShutdownEvent");
                }
            });
        }

        private void EraseStartEvent(object? sender, EraseEventArgs e)
        {
            eraseStart(e.Note);
        }

        private void EraseCancelEvent(object? sender, EventArgs e)
        {
            eraseCancel();
        }

        private void EraseBlockEvent(object? sender, EraseBlockEventArgs e)
        {
            eraseBlock(e.Block);
        }

        private void eraseStart(string note = null)
        {
            if (_eraserService != null)
            {
                _eraserService.RunErase(note);
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

        private async Task tgLogUnlock(string message)
        {
            if (_telegramBotService != null)
            {
                await _telegramBotService.LogUnlock(message);
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

        private async void Heartbeat(CancellationToken cancellationToken = default)
        {
            var delaySeconds = _settings.HeartbeatDelaySeconds ?? 60;
            if (delaySeconds <= 0) delaySeconds = 1;

            try
            {
                await Task.Delay(60 * 1000, cancellationToken);
            }
            catch { }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_settings.Eraser != null && _eraserService != null && !_eraserService.IsWorking)
                        _eraserService.Start();

                    if (_settings.Telegram != null && _telegramBotService != null && !_telegramBotService.IsWorking)
                        _telegramBotService.Start();

                    if (_settings.USBTrigger != null && _usbFlashDriveCheckerService != null && !_usbFlashDriveCheckerService.IsWorking)
                        _usbFlashDriveCheckerService.Start();

                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException)
                        _logger.Log(LogLevel.Error, ex, "Error on heartbeat");
                }
            }
        }
    }
}
