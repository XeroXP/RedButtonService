using Eraser.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Eraser.DefaultPlugins
{
    internal class ComputerInfo
    {
        private readonly bool isOldOS;

        internal ComputerInfo()
        {
            isOldOS = Environment.OSVersion.Version.Major < 5;
        }

        public ulong AvailablePhysicalMemory
        {
            get
            {
                bool success = false;
                ulong dwAvailPhys = 0;
                if (!isOldOS)
                {
                    NativeMethods.MEMORYSTATUSEX memStatus = new NativeMethods.MEMORYSTATUSEX();
                    if (NativeMethods.GlobalMemoryStatusEx(memStatus))
                    {
                        dwAvailPhys = memStatus.ullAvailPhys;
                        success = true;
                    }
                }
                if (!success)
                {
                    NativeMethods.MEMORYSTATUS memStatus = new NativeMethods.MEMORYSTATUS();
                    NativeMethods.GlobalMemoryStatus(memStatus);
                    dwAvailPhys = memStatus.dwAvailPhys;
                }
                return dwAvailPhys;
            }
        }

        public ulong AvailableVirtualMemory
        {
            get
            {
                bool success = false;
                ulong dwAvailVirtual = 0;
                if (!isOldOS)
                {
                    NativeMethods.MEMORYSTATUSEX memStatus = new NativeMethods.MEMORYSTATUSEX();
                    if (NativeMethods.GlobalMemoryStatusEx(memStatus))
                    {
                        dwAvailVirtual = memStatus.ullAvailVirtual;
                        success = true;
                    }
                }
                if (!success)
                {
                    NativeMethods.MEMORYSTATUS memStatus = new NativeMethods.MEMORYSTATUS();
                    NativeMethods.GlobalMemoryStatus(memStatus);
                    dwAvailVirtual = memStatus.dwAvailVirtual;
                }
                return dwAvailVirtual;
            }
        }
    }
}
