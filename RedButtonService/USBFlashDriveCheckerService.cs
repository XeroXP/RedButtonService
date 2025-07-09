using Eraser.Util;
using RedButtonService.Models;

namespace RedButtonService
{
    internal class USBFlashDriveCheckerService
    {
        private readonly ILogger _logger;
        private USBTriggerSettings _settings;

        private CancellationTokenSource cts;

        public EventHandler<TGMessageEventArgs> TGMessageSend { get; set; }
        public EventHandler EraseStart { get; set; }

        public USBFlashDriveCheckerService(USBTriggerSettings usbTriggerSettings, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("USBFlashDriveChecker");
            _settings = usbTriggerSettings;
        }

        public void Start()
        {
            if (_settings == null)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, $"USB Flash drive checker not started because of missing config");
                return;
            }

            cts = new CancellationTokenSource();

            Task.Run(() => Check(cts.Token));
        }

        public void Stop()
        {
            cts?.Cancel();
            cts = null;
        }

        private async void Check(CancellationToken cancellationToken = default)
        {
            var delaySeconds = _settings.TimeCheckSeconds ?? 60;
            if (delaySeconds <= 0) delaySeconds = 1;
            var fileCheck = _settings.FileName;
            if (string.IsNullOrEmpty(fileCheck)) fileCheck = "erase";

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    bool usbFileExists = false;
                    foreach (var volume in VolumeInfo.Volumes)
                    {
                        if (volume.VolumeType != DriveType.Removable)
                            continue;

                        foreach (var mountPoint in volume.MountPoints)
                        {
                            if (File.Exists(Path.Combine(mountPoint.FullName, fileCheck)))
                            {
                                usbFileExists = true;
                            }
                        }
                    }
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"UsbFlash exists: {usbFileExists}");
                    if (!usbFileExists)
                    {
                        EraseStart?.Invoke(this, EventArgs.Empty);
                        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"UsbFlash trigger erase");
                        TGMessageSend?.Invoke(this, new TGMessageEventArgs($"UsbFlash trigger erase"));
                    }

                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                }
                catch
                { }
            }
        }
    }
}
