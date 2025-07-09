using Eraser.Manager;
using Eraser.Plugins;
using Eraser.Plugins.ExtensionPoints;
using Eraser.Plugins.Registrars;
using Eraser.Util;
using LockCheck;
using Microsoft.Win32;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.ServiceProcess;
using System.Text;

namespace RedButtonConsole
{
    internal class Program
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

        private static Executor eraserClient;

        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            try
            {
                var processInfos = LockManager.GetLockingProcessInfos(new string[] { "E:\\" }, LockManagerFeatures.CheckDirectories | LockManagerFeatures.UseLowLevelApi);
                foreach (var processInfo in processInfos)
                {
                    Console.WriteLine($"{processInfo.ExecutableName}");
                    //LockManager.KillProcessAndChildren(processInfo.ProcessId);
                }
            }
            catch (Exception)
            { }

            var curSessionId = SessionUser.GetCurrentSessionId();
            var sessionIds = SessionUser.GetSessionIds();
            SessionUser.LogOffSession(curSessionId);
            Console.WriteLine(sessionIds.Count);

            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            System.Threading.Tasks.Task.Run(() => CheckUSBFlashDrive(ct));

            /*using (ManagerLibrary library = new ManagerLibrary())
            {
                using (eraserClient = new DirectExecutor())
                {
                    eraserClient.TaskAdded += TaskAdded;
                    eraserClient.TaskDeleted += TaskDeleted;
                    eraserClient.Run();

                    CommandErase(new EraseArguments()
                    {
                        Action = "erase",
                        PositionalArguments = new List<string>()
                        {
                            //"file=E:\\test\\test2\\test.txt"
                            //"dir=E:\\test\\test2"
                            "unused=E:\\"
                            //"drive=\\\\?\\Volume{d86c557e-8dc6-4bf5-b30e-b560f7f05aad}\\"
                            //"recyclebin"
                            //"move=<source>|<destination>"
                            //"drive=\\\\?\\Volume{da05f656-57d7-11f0-b94a-ea9e9b34d4c8}\\"
                        }
                    });

                    Console.WriteLine("Press enter to exit");
                    Console.ReadLine();
                    cts.Cancel();
                    foreach (var task in eraserClient.Tasks)
                    {
                        task?.Cancel();
                    }
                }
            }*/

            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
            cts.Cancel();
        }

        private static async void CheckUSBFlashDrive(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool usbFileExists = false;
                foreach (var volume in VolumeInfo.Volumes)
                {
                    if (volume.VolumeType != DriveType.Removable)
                        continue;

                    foreach (var mountPoint in volume.MountPoints)
                    {
                        //Console.WriteLine($"{mountPoint.FullName}");
                        if (File.Exists(Path.Combine(mountPoint.FullName, "erase")))
                        {
                            usbFileExists = true;
                        }
                    }
                }
                Console.WriteLine($"UsbFlash exists: {usbFileExists}");
                try
                {
                    await System.Threading.Tasks.Task.Delay(60 * 1000, cancellationToken);
                }
                catch
                { }
            }
        }

        private static async void CheckTaskProgress(Eraser.Manager.Task task)
        {
            while (task != null && task.Executing)
            {
                if (task.Progress != null)
                {
                    Console.WriteLine($"{task.Name}: {task.Progress.Progress.ToString("0.00%")} ({(task.Progress.TimeLeft > TimeSpan.Zero ? task.Progress.TimeLeft.ToString(@"hh\:mm\:ss") : "endless")})");
                }
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }

        private static void TaskDeleted(object? sender, TaskEventArgs e)
        {
            Console.WriteLine($"TaskDeleted: {e.Task.Name}");
            string allLogs = string.Join("\n", e.Task.Log.SelectMany(l => l.Select(le => $"{le.Timestamp} - {le.Message}")).ToArray());
            Console.WriteLine(allLogs);
        }

        private static void TaskAdded(object? sender, TaskEventArgs e)
        {
            Console.WriteLine($"TaskAdded: {e.Task.Name}");
        }

        /// <summary>
        /// Parses the command line for tasks and adds them to run immediately
        /// using the <see cref="RemoveExecutor"/> class.
        /// </summary>
        /// <param name="arg">The command line parameters passed to the program.</param>
        private static void CommandErase(ConsoleArguments arg)
        {
            TaskArguments arguments = new TaskArguments((EraseArguments)arg) { Schedule = "NOW" };

            CommandAddTask(arguments);
        }

        /// <summary>
        /// Parses the command line for tasks and adds them using the
        /// <see cref="RemoteExecutor"/> class.
        /// </summary>
        /// <param name="arg">The command line parameters passed to the program.</param>
        private static void CommandAddTask(ConsoleArguments arg)
        {
            TaskArguments arguments = (TaskArguments)arg;
            Eraser.Manager.Task task = TaskFromCommandLine(arguments);

            task.Name = "Test1";
            task.TaskStarted += TaskStarted;
            task.TaskFinished += TaskFinished;

            //Send the task out.
            //using (eraserClient = new DirectExecutor())
            eraserClient.Tasks.Add(task);
            Console.WriteLine("Ok");
        }

        private static void TaskFinished(object? sender, EventArgs e)
        {
            Console.WriteLine($"TaskFinished: {((Eraser.Manager.Task)sender).Name}");
            eraserClient.Tasks.Remove((Eraser.Manager.Task)sender);
        }

        private static void TaskStarted(object? sender, EventArgs e)
        {
            Console.WriteLine($"TaskStarted: {((Eraser.Manager.Task)sender).Name}");
            System.Threading.Tasks.Task.Run(() => CheckTaskProgress((Eraser.Manager.Task)sender));
        }

        /// <summary>
        /// Parses the command line for erasure targets and returns them as
        /// a Task object.
        /// </summary>
        /// <param name="arguments">The arguments specified on the command line.</param>
        /// <returns>The task represented on the command line.</returns>
        private static Eraser.Manager.Task TaskFromCommandLine(TaskArguments arguments)
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
                foreach (IErasureTarget target in Host.Instance.ErasureTargetFactories)
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
                    Console.WriteLine(S._("Unknown argument: {0}, skipped.", argument));
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

        private static IErasureMethod ErasureMethodFromNameOrGuid(string param)
        {
            try
            {
                return Host.Instance.ErasureMethods[new Guid(param)];
            }
            catch (FormatException)
            {
                //Invalid GUID. Check every registered erasure method for the name
                string upperParam = param.ToUpperInvariant();
                IErasureMethod result = null;
                foreach (IErasureMethod method in Host.Instance.ErasureMethods)
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
