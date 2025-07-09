using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Eraser.Util
{
    public static class MarshalHelper
    {
        public static T BytesToStruct<T>(ref byte[] rawData) where T : struct
        {
            T result = default(T);
            GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                IntPtr rawDataPtr = handle.AddrOfPinnedObject();
                result = (T)Marshal.PtrToStructure(rawDataPtr, typeof(T));
            }
            finally
            {
                handle.Free();
            }
            return result;
        }

        public static T[] BytesToStructs<T>(ref byte[] rawData, uint length) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            T[] result = new T[length];
            GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                IntPtr rawDataPtr = handle.AddrOfPinnedObject();
                for (int i = 0; i < length; i++)
                {
                    IntPtr ins;
                    if (Is64Bits())
                    {
                        ins = new IntPtr(rawDataPtr.ToInt64() + i * size);
                    }
                    else
                    {
                        ins = new IntPtr(rawDataPtr.ToInt32() + i * size);
                    }
                    result[i] = (T)Marshal.PtrToStructure(ins, typeof(T));
                }
            }
            finally
            {
                handle.Free();
            }
            return result;
        }

        public static byte[] StructToBytes<T>(T data) where T : struct
        {
            byte[] rawData = new byte[Marshal.SizeOf(data)];
            GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                IntPtr rawDataPtr = handle.AddrOfPinnedObject();
                Marshal.StructureToPtr(data, rawDataPtr, false);
            }
            finally
            {
                handle.Free();
            }
            return rawData;
        }

        public static byte[] StructsToBytes<T>(T[] data) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            byte[] rawData = new byte[size * data.Length];
            GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                IntPtr rawDataPtr = handle.AddrOfPinnedObject();
                for (int i = 0; i < data.Length; i++)
                {
                    IntPtr ins;
                    if (Is64Bits())
                    {
                        ins = new IntPtr(rawDataPtr.ToInt64() + i * size);
                    }
                    else
                    {
                        ins = new IntPtr(rawDataPtr.ToInt32() + i * size);
                    }
                    Marshal.StructureToPtr(data[i], ins, false);
                }
            }
            finally
            {
                handle.Free();
            }
            return rawData;
        }

        public static bool Is64Bits()
        {
            return Marshal.SizeOf(typeof(IntPtr)) == 8 ? true : false;
        }
    }
}
