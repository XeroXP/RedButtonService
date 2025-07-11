using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LockCheck.Linux;

internal static class ProcFileSystem
{
    private static volatile int s_procMatchesPidNamespace;

    internal static Dictionary<(int, DateTime), ProcessInfo> GetProcessesByWorkingDirectory(List<string> directories)
    {
        var result = new Dictionary<(int, DateTime), ProcessInfo>();

        foreach (int processId in EnumerateProcessIds())
        {
            var pi = new ProcInfo(processId);

            if (!pi.HasError && !string.IsNullOrEmpty(pi.CurrentDirectory))
            {
                // If the process' current directory is the search path itself, or it is somewhere nested below it,
                // we have to take it into account. This will also account for differences in the two when the
                // search path does not end with a '/'.
                if (directories.FindIndex(d => pi.CurrentDirectory.StartsWith(d, StringComparison.Ordinal)) != -1)
                {
                    result[(processId, pi.StartTime)] = ProcessInfoLinux.Create(pi);
                }
            }
        }

        return result;
    }

    private static IEnumerable<int> EnumerateProcessIds()
    {
        // This is an attempt to handle what was outlined in "https://github.com/dotnet/runtime/pull/100076".
        // Basically, when a process runs inside a root-less container, it can happen that the PID namespaces
        // of the process itself and /proc don't match up. In this case, we cannot reliably get information
        // about other processes.
        if (!ProcMatchesPidNamespace)
        {
            yield return Environment.ProcessId;
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };

        foreach (string fullPath in Directory.EnumerateDirectories("/proc", "*", options))
        {
            if (int.TryParse(Path.GetFileName(fullPath.AsSpan()), NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
            {
                yield return processId;
            }
        }
    }

    public static HashSet<ProcessInfo> GetLockingProcessInfos(string[] paths, [NotNullIfNotNull(nameof(directories))] ref List<string>? directories)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));

        Dictionary<long, string>? inodesToPaths = null;
        var result = new HashSet<ProcessInfo>();

        var xpaths = new HashSet<string>(paths.Length, StringComparer.Ordinal);

        foreach (string path in paths)
        {
            // Get directories, but don't exclude them from lookup via procfs (in contrast to Windows).
            // On Linux /proc/locks may also contain directory locks.
            if (Directory.Exists(path))
            {
                directories?.Add(path);
            }

            xpaths.Add(path);
        }

        using (var reader = new StreamReader("/proc/locks"))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (inodesToPaths == null)
                {
                    inodesToPaths = GetInodeToPaths(xpaths);
                }

                var lockInfo = LockInfo.ParseLine(line);
                if (inodesToPaths.ContainsKey(lockInfo.InodeInfo.INodeNumber))
                {
                    var processInfo = ProcessInfoLinux.Create(lockInfo);
                    if (processInfo != null)
                    {
                        result.Add(processInfo);
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<long, string> GetInodeToPaths(HashSet<string> paths)
    {
        var inodesToPaths = new Dictionary<long, string>();

        foreach (string path in paths)
        {
            long inode = NativeMethods.GetInode(path);
            if (inode != -1)
            {
                inodesToPaths.Add(inode, path);
            }
        }

        return inodesToPaths;
    }

    // Idea for handling of proc/pid-namespace mismatch is largely copied from dotnet/runtime.

    internal static bool ProcMatchesPidNamespace
    {
        get
        {
            // s_procMatchesPidNamespace is set to:
            // - 0: when uninitialized,
            // - 1: '/proc' and the process pid namespace match,
            // - 2: when they don't match.
            if (s_procMatchesPidNamespace == 0)
            {
                // '/proc/self' is a symlink to the pid used by '/proc' for the current process.
                // We compare it with the pid of the current process to see if the '/proc' and pid namespace match up.

                int? procSelfPid = null;

                if (Directory.ResolveLinkTarget("/proc/self", false)?.FullName is string target &&
                    int.TryParse(Path.GetFileName(target), out int pid))
                {
                    procSelfPid = pid;
                }

                Debug.Assert(procSelfPid.HasValue);

                s_procMatchesPidNamespace = !procSelfPid.HasValue || procSelfPid == Environment.ProcessId ? 1 : 2;
            }
            return s_procMatchesPidNamespace == 1;
        }
    }

    internal enum ProcPid : int
    {
        Invalid = -1,
        Self = 0, // Current process: this will also work in root less containers, if accessed via /proc/self/...
        // Actual PIDs from /proc, cast as "ProcPid"
    }

    internal static bool TryGetProcPid(int pid, out ProcPid procPid)
    {
        if (pid == Environment.ProcessId)
        {
            // Use '/proc/self' for the current process.
            procPid = ProcPid.Self;
            return true;
        }

        if (ProcMatchesPidNamespace)
        {
            // Since namespaces match, we can handle any process.
            procPid = (ProcPid)pid;
            return true;
        }

        // Cannot handle arbitrary processes when namespaces do not match.
        procPid = ProcPid.Invalid;
        return false;
    }

    private static string GetProcCmdline(ProcPid procPid) => procPid == ProcPid.Self ? "/proc/self/cmdline" : string.Create(null, stackalloc char[128], $"/proc/{(int)procPid}/cmdline");
    private static string GetProcExe(ProcPid procPid) => procPid == ProcPid.Self ? "/proc/self/exe" : string.Create(null, stackalloc char[128], $"/proc/{(int)procPid}/exe");
    private static string GetProcCwd(ProcPid procPid) => procPid == ProcPid.Self ? "/proc/self/cwd" : string.Create(null, stackalloc char[128], $"/proc/{(int)procPid}/cwd");
    private static string GetProcStat(ProcPid procPid) => procPid == ProcPid.Self ? "/proc/self/stat" : string.Create(null, stackalloc char[128], $"/proc/{(int)procPid}/stat");
    private static string GetProcDir(ProcPid procPid) => procPid == ProcPid.Self ? "/proc/self" : string.Create(null, stackalloc char[128], $"/proc/{(int)procPid}");

    internal static bool Exists(int processId) => TryGetProcPid(processId, out var procPid) && Directory.Exists(GetProcDir(procPid));

    internal static string? GetProcessOwner(int processId)
    {
        if (TryGetProcPid(processId, out var procPid))
        {
            if (procPid == ProcPid.Self)
            {
                return Environment.UserName;
            }

            if (NativeMethods.TryGetUid(GetProcDir(procPid), out uint uid))
            {
                return NativeMethods.GetUserName(uid);
            }
        }

        return null;
    }

    internal static DateTime GetProcessStartTime(int processId)
    {
        if (TryGetProcPid(processId, out ProcPid procPid))
        {
            // Apparently it is currently impossible to fully recreate the time that Process.StartTime is.
            // Also see https://github.com/dotnet/runtime/issues/108959.

            if (procPid == ProcPid.Self)
            {
                using var process = Process.GetCurrentProcess();
                return process.StartTime;
            }
            else
            {
                using var process = Process.GetProcessById(processId);
                return process.StartTime;
            }
        }

        return default;
    }

    internal static int GetProcessSessionId(int processId)
    {
        int sessionId = -1;
        if (TryGetProcPid(processId, out ProcPid procPid))
        {
            var content = File.ReadAllText(GetProcStat(procPid)).AsSpan().Trim();
            if (int.TryParse(GetField(content, ' ', 5).Trim(), CultureInfo.InvariantCulture, out int sid))
            {
                sessionId = sid;
            }
        }

        return sessionId;
    }

    internal static string? GetProcessCurrentDirectory(int processId)
    {
        if (TryGetProcPid(processId, out ProcPid procPid))
        {
            return Directory.ResolveLinkTarget(GetProcCwd(procPid), true)?.FullName;
        }

        return null;
    }

    internal static string[]? GetProcessCommandLineArgs(int processId, int maxArgs = -1)
    {
        if (TryGetProcPid(processId, out ProcPid procPid))
        {
            byte[]? rentedBuffer = null;
            try
            {
                using (var file = new FileStream(GetProcCmdline(procPid), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: false))
                {
                    Span<byte> buffer = stackalloc byte[256];
                    int bytesRead = 0;
                    while (true)
                    {
                        if (bytesRead == buffer.Length)
                        {
                            // Resize buffer
                            uint newLength = (uint)buffer.Length * 2;
                            // Store what was read into new buffer
                            byte[] tmp = ArrayPool<byte>.Shared.Rent((int)newLength);
                            buffer.CopyTo(tmp);
                            // Remember current "rented" buffer (might be null)
                            byte[]? lastRentedBuffer = rentedBuffer;
                            // From now on, we did rent a buffer. And it will be used for further reads.
                            buffer = tmp;
                            rentedBuffer = tmp;
                            // Return previously rented buffer, if any.
                            if (lastRentedBuffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(lastRentedBuffer);
                            }
                        }

                        Debug.Assert(bytesRead < buffer.Length);
                        int n = file.Read(buffer.Slice(bytesRead));
                        bytesRead += n;
                        if (n == 0)
                        {
                            break;
                        }
                    }

                    return ConvertToArgs(ref buffer, maxArgs);
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        return null;
    }

    internal static string[] ConvertToArgs(ref Span<byte> buffer, int maxArgs = -1)
    {
        if (buffer.IsEmpty || maxArgs == 0)
        {
            return [];
        }

        // Removing ending '\0\0' from buffer. That is how /proc/<pid>/cmdline is documented to end.
        // We need to strip those to not get a phony, empty, trailing argv[argc-1] element.
        if (buffer[^1] == '\0' && buffer[^2] == '\0')
        {
            buffer = buffer.Slice(0, buffer.Length - 2);
        }

        if (buffer.IsEmpty)
        {
            return [];
        }

        // Individual argv elements in the buffer are separated by a null byte.
        int actual = buffer.Count((byte)'\0') + 1;
        int count = maxArgs > 0 ? Math.Min(maxArgs, actual) : actual;
        string[] args = new string[count];
        int start = 0;
        int p = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '\0')
            {
                args[p++] = Encoding.UTF8.GetString(buffer.Slice(start, i - start));
                start = i + 1;

                if (maxArgs > 0 && maxArgs == p)
                {
                    return args;
                }
            }
        }

        if (start < buffer.Length)
        {
            args[p++] = Encoding.UTF8.GetString(buffer.Slice(start));
        }

        return args;
    }

    internal static string? GetProcessExecutablePath(int processId)
    {
        if (TryGetProcPid(processId, out ProcPid procPid))
        {
            return File.ResolveLinkTarget(GetProcExe(procPid), true)?.FullName;
        }

        return null;
    }

    internal static string? GetProcessExecutablePathFromCmdLine(int processId)
    {
        // This is a little more expensive than a specific function only reading up to argv[0] from /proc/<pid>/cmdline
        // would be - GetProcessCommandLineArgs() reads all arguments, but then only converts "maxArgs" of them to an
        // actual System.String. On the other hand it saves quite some code duplication.
        string[]? args = GetProcessCommandLineArgs(processId, maxArgs: 1);
        return args?.Length > 0 ? args[0] : null;
    }

    internal static ReadOnlySpan<char> GetField(ReadOnlySpan<char> content, char delimiter, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Field index cannot be negative.");
        }

#if NET9_0_OR_GREATER
        // PERF NOTE: For larger index values this will perform actually worse than the manual usage of
        // Count()/MemoryExtensions.Split() below. However, currently we use rather small indexes (5 out of 52)
        // where this performs actually better.
        // Also, this is cleaner an less error prone.
        // In .NET 10+ the ref struct enumerator returned here will implement IEnumerable<> so that we could
        // try using LINQ here, to make things even more simple (and possibly performant, as LINQ is getting
        // improved also!)

        int count = 0;
        foreach (var range in content.Split(delimiter))
        {
            if (count < index)
            {
                count++;
                continue;
            }
            return content[range];
        }

        throw new ArgumentOutOfRangeException(nameof(index), index, $"Cannot access field at index {index}, only {count} fields available.");
#else
        int fieldCount = content.Count(delimiter) + 1;
        if (fieldCount <= index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Cannot access field at index {index}, only {fieldCount} fields available.");
        }

        // We need to split into N+1 fields, where N is the field denoted by the index.
        // The extra field will receive the remainder of content, that doesn't need to
        // be split further, because we're not interested. That also means, that if we
        // are supposed to read the last field of content, we don't need that extra field.
        int rangeCount = index == fieldCount - 1 ? index + 1 : index + 2;
        Span<Range> ranges = rangeCount < 128 ? stackalloc Range[rangeCount] : new Range[rangeCount];
        int num = MemoryExtensions.Split(content, ranges, delimiter);

        // Shouldn't trigger, because of pre-checks done above.
        Debug.Assert(num == rangeCount);

        return content[ranges[index]];
#endif
    }
}
