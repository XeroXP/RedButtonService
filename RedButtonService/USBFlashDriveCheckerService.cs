using Eraser.Util;
using RedButtonService.Models;

namespace RedButtonService
{
    internal class USBFlashDriveCheckerService
    {
        private readonly ILogger _logger;
        private USBTriggerSettings _settings;

        private readonly object _usbCheckLock = new();

        private CancellationTokenSource cts;

        public bool IsWorking { get; private set; } = false;
        public EventHandler<EraseEventArgs> EraseStart { get; set; }

        public USBFlashDriveCheckerService(USBTriggerSettings usbTriggerSettings, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("USBFlashDriveChecker");
            _settings = usbTriggerSettings;
        }

        public void Start()
        {
            lock (_usbCheckLock)
            {
                if (IsWorking)
                    return;

                if (_settings == null)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, $"USB Flash drive checker not started because of missing config");
                    return;
                }

                cts = new CancellationTokenSource();

                IsWorking = true;
                Task.Run(() => Check(cts.Token));
            }
        }

        public void Stop()
        {
            lock (_usbCheckLock)
            {
                UnlockedStop();
            }
        }

        private void UnlockedStop()
        {
            cts?.Cancel();
            cts = null;

            IsWorking = false;
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
                        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"UsbFlash trying to trigger erase");
                        EraseStart?.Invoke(this, new EraseEventArgs($"UsbFlash trigger erase"));
                    }

                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException)
                        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on USB Check");
                }
            }
        }
    }
}
