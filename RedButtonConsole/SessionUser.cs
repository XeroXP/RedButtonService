using Eraser.Manager.RIPEMD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonConsole
{
    public class SessionUser
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionID;

            [MarshalAs(UnmanagedType.LPStr)]
            public string pWinStationName;

            public WTS_CONNECTSTATE_CLASS State;
        }

        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        private enum WtsInfoClass
        {
            WTSUserName = 5,
            WTSDomainName = 7,
        }

        [DllImport("Wtsapi32.dll")]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WtsInfoClass wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

        [DllImport("Wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pointer);

        [DllImport("Kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U4)]
        private static extern int WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool bWait);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern int WTSEnumerateSessions(IntPtr hServer, [MarshalAs(UnmanagedType.U4)] int Reserved, [MarshalAs(UnmanagedType.U4)] int Version, ref IntPtr ppSessionInfo, [MarshalAs(UnmanagedType.U4)] ref int pCount);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSLogoffSession(IntPtr hServer, int SessionId, bool bWait);

        public static int GetCurrentSessionId()
        {
            return WTSGetActiveConsoleSessionId();
        }

        public static string GetUserName(int? sessionId = null, bool addDomain = true)
        {
            sessionId ??= WTSGetActiveConsoleSessionId();

            IntPtr buffer;
            int strLen;
            string username = "SYSTEM";
            if (WTSQuerySessionInformation(IntPtr.Zero, sessionId.Value, WtsInfoClass.WTSUserName, out buffer, out strLen) && strLen > 1)
            {
                username = Marshal.PtrToStringAnsi(buffer);
                WTSFreeMemory(buffer);

                if (addDomain)
                {
                    if (WTSQuerySessionInformation(IntPtr.Zero, sessionId.Value, WtsInfoClass.WTSDomainName, out buffer, out strLen) && strLen > 1)
                    {
                        username = Marshal.PtrToStringAnsi(buffer) + "\\" + username;
                        WTSFreeMemory(buffer);
                    }
                }
            }
            return username;
        }

        public static bool LogOffSession(int? sessionId = null)
        {
            sessionId ??= WTSGetActiveConsoleSessionId();

            return WTSLogoffSession(IntPtr.Zero, sessionId.Value, false);
        }

        public static List<int> GetSessionIds()
        {
            List<int> sessionIds = new List<int>();

            IntPtr serverHandle = IntPtr.Zero;

            IntPtr userPtr = IntPtr.Zero;
            IntPtr sessionInfoPtr = IntPtr.Zero;
            int sessionCount = 0;
            int retVal = WTSEnumerateSessions(serverHandle, 0, 1, ref sessionInfoPtr, ref sessionCount);
            int dataSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
            IntPtr currentSession = sessionInfoPtr;
            int bytes = 0;

            if (retVal != 0)
            {
                for (int i = 0; i < sessionCount; i++)
                {
                    WTS_SESSION_INFO si = (WTS_SESSION_INFO)Marshal.PtrToStructure(currentSession, typeof(WTS_SESSION_INFO));
                    currentSession += dataSize;

                    if (WTSQuerySessionInformation(serverHandle, si.SessionID, WtsInfoClass.WTSUserName, out userPtr, out bytes) && bytes > 1)
                    {
                        string username = Marshal.PtrToStringAnsi(userPtr);
                        WTSFreeMemory(userPtr);
                        if (!string.IsNullOrEmpty(username))
                        {
                            sessionIds.Add(si.SessionID);
                        }
                    }
                }

                WTSFreeMemory(sessionInfoPtr);
            }

            return sessionIds;
        }
    }
}
