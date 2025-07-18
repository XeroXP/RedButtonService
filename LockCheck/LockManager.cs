using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LockCheck.Windows;
using System.Diagnostics;

#if NET
using LockCheck.Linux;
#endif


namespace LockCheck;

/// <summary>
/// Retrieves information about locked files and directories.
/// </summary>
public static class LockManager
{
    /// <summary>
    /// Attempt to find processes that lock the specified paths.
    /// </summary>
    /// <param name="paths">The paths to check.</param>
    /// <param name="features">Optional features</param>
    /// <returns>
    /// A list of processes that lock at least one of the specified paths.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is <c>null</c>.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// The current platform is not supported. This exception is only thrown, when the <paramref name="features"/>
    /// includes the <see cref="LockManagerFeatures.ThrowIfNotSupported"/> flag. Otherwise the function will
    /// simply return an empty enumeration when a platform is not supported.
    /// </exception>
    public static IEnumerable<ProcessInfo> GetLockingProcessInfos(string[] paths, LockManagerFeatures features = default)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));

        HashSet<ProcessInfo> processInfos = [];
        List<string>? directories = (features & LockManagerFeatures.CheckDirectories) != 0 ? [] : null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if ((features & LockManagerFeatures.UseLowLevelApi) != 0)
            {
                processInfos = NtDll.GetLockingProcessInfos(paths, ref directories);
            }
            else
            {
                processInfos = RestartManager.GetLockingProcessInfos(paths, ref directories);
            }

            if (directories?.Count > 0)
            {
                var matches = NtDll.GetProcessesByWorkingDirectory(directories);
                foreach (var match in matches)
                {
                    processInfos.Add(match.Value);
                }
            }
        }
#if NET
        // Linux sources are only build when building for .NET, not for .NET Framework.
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            processInfos = ProcFileSystem.GetLockingProcessInfos(paths, ref directories);

            if (directories?.Count > 0)
            {
                var matches = ProcFileSystem.GetProcessesByWorkingDirectory(directories);
                foreach (var match in matches)
                {
                    processInfos.Add(match.Value);
                }
            }
        }
#endif
        else
        {
            if ((features & LockManagerFeatures.ThrowIfNotSupported) != 0)
            {
                throw new PlatformNotSupportedException("Current OS platform is not supported");
            }
        }

        return processInfos;
    }

    /// <summary>
    /// Kill a process, and all of its children, grandchildren, etc.
    /// </summary>
    /// <param name="pid">Process ID.</param>
    public static void KillProcessAndChildren(int pid)
    {
        // Cannot close 'system idle process'.
        if (pid == 0)
        {
            return;
        }
        /*ManagementObjectSearcher searcher = new ManagementObjectSearcher
                ("Select * From Win32_Process Where ParentProcessID=" + pid);
        ManagementObjectCollection moc = searcher.Get();
        foreach (ManagementObject mo in moc)
        {
            KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
        }*/
        try
        {
            Process proc = Process.GetProcessById(pid);
            proc.Kill(true);
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
    }
}
