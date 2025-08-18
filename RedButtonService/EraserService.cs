using Eraser.Manager;
using Eraser.Plugins.ExtensionPoints;
using Eraser.Plugins.Registrars;
using Eraser.Util;
using LockCheck;
using RedButtonService.Models;
using System.Threading.Tasks;

namespace RedButtonService
{
    internal class EraserService
    {
        /// <summary>
		/// The common program arguments shared between the GUI and console programs.
		/// </summary>
		class Arguments
        {
            /// <summary>
            /// True if the program should not be started with any user-visible interfaces.
            /// </summary>
            /// <remarks>Errors will also be silently ignored.</remarks>
            public bool Quiet { get; set; }
        }

        class ConsoleArguments : Arguments
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public ConsoleArguments()
            {
            }

            /// <summary>
            /// Copy constructor.
            /// </summary>
            /// <param name="arguments">The <see cref="ConsoleArguments"/> to use as a template
            /// for this instance.</param>
            protected ConsoleArguments(ConsoleArguments arguments)
            {
                Action = arguments.Action;
                PositionalArguments = arguments.PositionalArguments;
            }

            /// <summary>
            /// The Action which this handler is in charge of.
            /// </summary>
            public string Action { get; set; }

            /// <summary>
            /// The list of command line parameters not placed in a switch.
            /// </summary>
            public List<string> PositionalArguments { get; set; }
        }

        class EraseArguments : ConsoleArguments
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public EraseArguments()
            {
            }

            /// <summary>
            /// Copy constructor.
            /// </summary>
            /// <param name="arguments">The <see cref="EraseArguments"/> to use as a template
            /// for this instance.</param>
            protected EraseArguments(EraseArguments arguments)
                : base(arguments)
            {
                ErasureMethod = arguments.ErasureMethod;
            }

            /// <summary>
            /// The erasure method which the user specified on the command line.
            /// </summary>
            public string ErasureMethod { get; set; }
        }

        class TaskArguments : EraseArguments
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public TaskArguments()
            {
            }

            /// <summary>
            /// Constructs Task arguments from Erase arguments.
            /// </summary>
            /// <param name="arguments">The <see cref="EraseArguments"/> to use as a template
            /// for this instance.</param>
            internal TaskArguments(EraseArguments arguments)
                : base(arguments)
            {
            }

            /// <summary>
            /// The schedule for the current set of targets.
            /// </summary>
            public string Schedule { get; set; }
        }

        private readonly ILogger _logger;
        private EraserSettings _settings;

        private CancellationTokenSource cts;
        private ManagerLibrary library;
        private Executor eraserClient;
        private int maxTasks = 1;
        private int timeStatusSendMinutes = 0;
        private bool isBlocked = false;

        private readonly object _eraseLock = new();

        public bool IsWorking { get; private set; } = false;
        public EventHandler<TGMessageEventArgs> TGMessageSend { get; set; }

        public EraserService(EraserSettings eraserSettings, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("EraserService");
            _settings = eraserSettings;
        }

        public void Start()
        {
            lock (_eraseLock)
            {
                if (IsWorking)
                    return;

                if (_settings == null || _settings.ToErase == null || _settings.ToErase.Count <= 0)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, $"EraserService not started because of missing config");
                    return;
                }

                if (_settings.MaxTasks.HasValue && _settings.MaxTasks > 0)
                {
                    maxTasks = _settings.MaxTasks.Value;
                }

                if (_settings.TimeStatusSendMinutes.HasValue && _settings.TimeStatusSendMinutes >= 0)
                {
                    timeStatusSendMinutes = _settings.TimeStatusSendMinutes.Value;
                }

                try
                {
                    cts = new CancellationTokenSource();
                    library = new ManagerLibrary();
                    eraserClient = new DirectExecutor();

                    eraserClient.TaskAdded += TaskAdded;
                    eraserClient.TaskDeleted += TaskDeleted;
                    eraserClient.Run();

                    IsWorking = true;
                }
                catch (Exception ex)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on Start eraser service");
                    UnlockedStop();
                }
            }
        }

        public void Stop()
        {
            lock (_eraseLock)
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
                if (eraserClient != null)
                {
                    foreach (var task in eraserClient.Tasks)
                    {
                        task?.Cancel();
                    }

                    eraserClient.Tasks.Clear();

                    eraserClient.TaskAdded -= TaskAdded;
                    eraserClient.TaskDeleted -= TaskDeleted;

                    eraserClient.Dispose();
                    eraserClient = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on Stop eraserClient");
            }

            try
            {
                if (library != null)
                {
                    library.Dispose();
                    library = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on Stop library");
            }

            IsWorking = false;
        }

        public void RunErase(string note = null)
        {
            lock (_eraseLock)
            {
                if (library == null || eraserClient == null)
                {
                    TGMessageSend?.Invoke(this, new TGMessageEventArgs("Error to Run erase because eraser service is not started"));
                    return;
                }

                if (isBlocked)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Run erase blocked");
                    return;
                }

                if (eraserClient.Tasks.Count >= maxTasks)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Max tasks reached: {maxTasks}");
                    //TGMessageSend?.Invoke(this, new TGMessageEventArgs($"Max tasks reached: {maxTasks}"));
                    return;
                }

                try
                {
                    var paths = _settings.ToErase.Select(te => te.GetPaths()).Where(te => te != null && te.Length > 0).SelectMany(te => te).ToArray();
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Erase run - Paths for lock checking found {paths.Length}:\n{string.Join('\n', paths)}");
                    if (paths.Length > 0)
                    {
                        var processInfos = LockManager.GetLockingProcessInfos(paths, LockManagerFeatures.CheckDirectories | LockManagerFeatures.UseLowLevelApi);
                        foreach (var processInfo in processInfos)
                        {
                            LockManager.KillProcessAndChildren(processInfo.ProcessId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error with killing all locking processes");
                }

                try
                {
                    if (!string.IsNullOrEmpty(note))
                    {
                        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, note);
                        TGMessageSend?.Invoke(this, new TGMessageEventArgs(note));
                    }
                    var cmds = _settings.ToErase.Select(te => te.GetCmd()).Where(te => te != null && te.Count > 0).SelectMany(te => te).ToList();
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Erase run - Cmds found {cmds.Count}:\n{string.Join('\n', cmds)}");
                    if (cmds.Count > 0)
                    {
                        CommandErase("EraseTask", new EraseArguments()
                        {
                            Action = "erase",
                            PositionalArguments = cmds
                        });
                    }
                    else
                    {
                        TGMessageSend?.Invoke(this, new TGMessageEventArgs("Error to Run erase because of incorrect cmds"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error to Run erase");
                    TGMessageSend?.Invoke(this, new TGMessageEventArgs("Error to Run erase"));
                }
            }
        }

        public void CancelErase()
        {
            lock (_eraseLock)
            {
                try
                {
                    if (eraserClient != null)
                    {
                        foreach (var task in eraserClient.Tasks)
                        {
                            task?.Cancel();
                        }
                        eraserClient.Tasks.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on Cancel erase");
                }
            }
        }

        public void BlockErase(bool block)
        {
            isBlocked = block;
        }

        private async void CheckTaskProgress(Eraser.Manager.Task task, CancellationToken cancellationToken = default)
        {
            if (timeStatusSendMinutes > 0)
            {
                while (task != null && task.Executing && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (task.Progress != null)
                        {
                            TGMessageSend?.Invoke(this, new TGMessageEventArgs($"{task.Name}: {task.Progress.Progress.ToString("0.00%")} ({(task.Progress.TimeLeft > TimeSpan.Zero ? task.Progress.TimeLeft.ToString(@"hh\:mm\:ss") : "endless")})"));
                        }

                        await System.Threading.Tasks.Task.Delay(timeStatusSendMinutes * 60 * 1000, cancellationToken);
                    }
                    catch
                    { }
                }
            }
        }

        private void TaskDeleted(object? sender, TaskEventArgs e)
        {
            _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"TaskDeleted: {e.Task.Name}");
            string allLogs = string.Join("\n", e.Task.Log.SelectMany(l => l.Select(le => $"{le.Timestamp} - {le.Message}")).ToArray());
            _logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Logs of {e.Task.Name}:\n{allLogs}");
            TGMessageSend?.Invoke(this, new TGMessageEventArgs($"Logs of {e.Task.Name}:\n{allLogs}"));
            try
            {
                e.Task.TaskStarted -= TaskStarted;
                e.Task.TaskFinished -= TaskFinished;
            }
            catch { }
        }

        private void TaskAdded(object? sender, TaskEventArgs e)
        {
            _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"TaskAdded: {e.Task.Name}");
        }

        /// <summary>
        /// Parses the command line for tasks and adds them to run immediately
        /// using the <see cref="RemoveExecutor"/> class.
        /// </summary>
        /// <param name="arg">The command line parameters passed to the program.</param>
        private void CommandErase(string name, ConsoleArguments arg)
        {
            TaskArguments arguments = new TaskArguments((EraseArguments)arg) { Schedule = "NOW" };

            CommandAddTask(name, arguments);
        }

        /// <summary>
        /// Parses the command line for tasks and adds them using the
        /// <see cref="RemoteExecutor"/> class.
        /// </summary>
        /// <param name="arg">The command line parameters passed to the program.</param>
        private void CommandAddTask(string name, ConsoleArguments arg)
        {
            TaskArguments arguments = (TaskArguments)arg;
            Eraser.Manager.Task task = TaskFromCommandLine(arguments);

            task.Name = name;
            task.TaskStarted += TaskStarted;
            task.TaskFinished += TaskFinished;

            //Send the task out.
            eraserClient.Tasks.Add(task);
        }

        private void TaskFinished(object? sender, EventArgs e)
        {
            _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"TaskFinished: {((Eraser.Manager.Task)sender).Name}");
            TGMessageSend?.Invoke(this, new TGMessageEventArgs($"TaskFinished: {((Eraser.Manager.Task)sender).Name}"));
            lock (_eraseLock)
            {
                try
                {
                    if (eraserClient != null)
                        eraserClient.Tasks.Remove((Eraser.Manager.Task)sender);
                }
                catch (Exception ex)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, "Error on Remove task from eraserClient");
                }
            }
        }

        private void TaskStarted(object? sender, EventArgs e)
        {
            _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"TaskStarted: {((Eraser.Manager.Task)sender).Name}");
            TGMessageSend?.Invoke(this, new TGMessageEventArgs($"TaskStarted: {((Eraser.Manager.Task)sender).Name}"));
            if (TGMessageSend != null)
            {
                System.Threading.Tasks.Task.Run(() => CheckTaskProgress((Eraser.Manager.Task)sender), cts != null ? cts.Token : default);
            }
        }

        /// <summary>
        /// Parses the command line for erasure targets and returns them as
        /// a Task object.
        /// </summary>
        /// <param name="arguments">The arguments specified on the command line.</param>
        /// <returns>The task represented on the command line.</returns>
        private Eraser.Manager.Task TaskFromCommandLine(TaskArguments arguments)
        {
            //Create the task
            Eraser.Manager.Task task = new Eraser.Manager.Task();

            //Get the erasure method the user wants to use
            IErasureMethod method = string.IsNullOrEmpty(arguments.ErasureMethod) ?
                ErasureMethodRegistrar.Default :
                ErasureMethodFromNameOrGuid(arguments.ErasureMethod);

            //Define the schedule
            if (!string.IsNullOrEmpty(arguments.Schedule))
                switch (arguments.Schedule.ToUpperInvariant())
                {
                    case "NOW":
                        task.Schedule = Schedule.RunNow;
                        break;
                    case "MANUALLY":
                        task.Schedule = Schedule.RunManually;
                        break;
                    case "RESTART":
                        task.Schedule = Schedule.RunOnRestart;
                        break;
                    default:
                        throw new ArgumentException(
                            S._("Unknown schedule type: {0}", arguments.Schedule), "/schedule");
                }

            //Parse the rest of the command line parameters as target expressions.
            foreach (string argument in arguments.PositionalArguments)
            {
                IErasureTarget selectedTarget = null;

                //Iterate over every defined erasure target
                foreach (IErasureTarget target in Eraser.Plugins.Host.Instance.ErasureTargetFactories)
                {
                    //See if this argument can be handled by the target's configurer
                    IErasureTargetConfigurer configurer = target.Configurer;
                    if (configurer.ProcessArgument(argument))
                    {
                        //Check whether a target has been set (implicitly: check whether two
                        //configurers can process the argument)
                        if (selectedTarget == null)
                        {
                            configurer.SaveTo(target);
                            selectedTarget = target;
                        }
                        else
                        {
                            //Yes, it is an ambiguity. Throw an error.
                            throw new ArgumentException(S._("Ambiguous argument: {0} can be " +
                                "handled by more than one erasure target.", argument));
                        }
                    }
                }

                //Check whether a target has been made from parsing the entry.
                if (selectedTarget == null)
                {
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, S._("Unknown argument: {0}, skipped.", argument));
                }
                else
                {
                    selectedTarget.Method = method;
                    task.Targets.Add(selectedTarget);
                }
            }

            //Check the number of tasks in the task.
            if (task.Targets.Count == 0)
                throw new ArgumentException(S._("Tasks must contain at least one erasure target."));

            return task;
        }

        private IErasureMethod ErasureMethodFromNameOrGuid(string param)
        {
            try
            {
                return Eraser.Plugins.Host.Instance.ErasureMethods[new Guid(param)];
            }
            catch (FormatException)
            {
                //Invalid GUID. Check every registered erasure method for the name
                string upperParam = param.ToUpperInvariant();
                IErasureMethod result = null;
                foreach (IErasureMethod method in Eraser.Plugins.Host.Instance.ErasureMethods)
                {
                    if (method.Name.ToUpperInvariant() == upperParam)
                        if (result == null)
                            result = method;
                        else
                            throw new ArgumentException(S._("Ambiguous erasure method name: {0} " +
                                "identifies more than one erasure method.", param));
                }
            }

            throw new ArgumentException(S._("The provided Erasure Method '{0}' does not exist.",
                param));
        }
    }
}
