using System;
using System.Runtime.InteropServices;

namespace Eraser.Util
{
    public class Fat
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct FatBootSector
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] JumpInstruction;          // jmp to executable code

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] OemName;                  // OEM name and version

            public ushort BytesPerSector;           // bytes per sector
            public byte SectorsPerCluster;          // sectors per cluster
            public ushort ReservedSectorCount;      // number of reserved sectors (starting at 0)
            public byte FatCount;                   // number of file allocation tables
            public ushort RootDirectoryEntryCount;  // number of root-directory entries (directory size)
            public ushort SectorCount16;            // total number of sectors (0 if partition > 32Mb)
            public byte MediaDescriptor;            // media descriptor
            public ushort SectorsPerFat;            // number of sectors per FAT, only for FAT12/FAT16
            public ushort SectorsPerTrack;          // number of sectors per track
            public ushort HeadCount;                // number of read/write heads
            public uint HiddenSectorCount;          // number of hidden sectors
            public uint SectorCount32;              // number of sectors if SectorCount16 is 0

            // union
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 476)]
            private byte[] _parameterBlock;

            public ExtendedBiosParameterBlock ExtendedBiosParameterBlock
            {
                get
                {
                    //var span = new Span<byte>(_parameterBlock);
                    //return MemoryMarshal.AsRef<ExtendedBiosParameterBlock>(span);
                    //var span = new ReadOnlySpan<byte>(_parameterBlock);
                    //return MemoryMarshal.Read<ExtendedBiosParameterBlock>(span);
                    return MarshalHelper.BytesToStruct<ExtendedBiosParameterBlock>(ref _parameterBlock);
                }
            }

            public Fat32ParameterBlock Fat32ParameterBlock
            {
                get
                {
                    //var span = new Span<byte>(_parameterBlock);
                    //return MemoryMarshal.AsRef<Fat32ParameterBlock>(span);
                    //var span = new ReadOnlySpan<byte>(_parameterBlock);
                    //return MemoryMarshal.Read<Fat32ParameterBlock>(span);
                    return MarshalHelper.BytesToStruct<Fat32ParameterBlock>(ref _parameterBlock);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ExtendedBiosParameterBlock
        {
            public byte DriveNumber;            // 0x80 if first hard drive
            public byte Reserved;               // may be used for dirty drive checking under NT
            public byte BootSignature;          // 0x29 if extended boot-signature record
            public uint VolumeID;               // volume ID number

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
            public byte[] VolumeLabel;          // volume label

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] FileSystemType;       // file-system type ("FAT12   " or "FAT16   ")

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 448)]
            public byte[] BootLoader;           // operating system boot loader code

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] BpbSignature;         // must be 0x55 0xAA
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Fat32ParameterBlock
        {
            public uint SectorsPerFat;
            public ushort FatFlags;
            public ushort Version;                  // version
            public uint RootDirectoryCluster;       // cluster number of root directory
            public ushort FsInformationSector;      // sector number of the FS Information Sector
            public ushort BootSectorCopySector;     // sector number for a copy of this boot sector

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Reserved1;

            public byte DriveNumber;
            public byte Reserved2;
            public byte BootSignature;
            public uint VolumeID;                   // volume ID number

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
            public byte[] VolumeLabel;              // volume label

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] FileSystemType;           // file system type ("FAT32   ")

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 420)]
            public byte[] BootLoader;               // operating system boot loader code

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] BootSectorSignature;      //
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct FatFsInformationSector
        {
            public uint Signature;                  // Fs Information Sector signature, must be 0x52 0x52 0x61 0x41 (RRaA), or 0x41615252

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 480)]
            public byte[] Reserved;                 // must be 0

            public uint Signature2;                 // Fs Information Sector signature, must be 0x72 0x72 0x41 0x61 (rrAa), or 0x61417272
            public uint FreeClusters;               // number of free clusters on the drive, or -1 if unknown
            public uint MostRecentlyAllocated;      // the number of the most recently allocated cluster

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
            public byte[] Reserved2;                // must be 0

            public ushort Signature3;               // Fs Information Sector signature, must be 0x55 0xAA
        }

        /// Represents a short (8.3) directory entry.
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        public struct Fat8Dot3DirectoryEntry
        {
            /// Base name. If Name[0] is:
            /// 
            /// 0x00		Entry is available and no subsequent entry is in use
            /// 0x05		Initial character is actually 0xE5. 0x05 is a valid kanji lead
            ///				byte, and is used for support for filenames written in kanji.
            /// 0x2E 		'Dot' entry; either '.' or '..'
            /// 0xE5		Entry has been previously erased and is available. File undelete
            ///				utilities must replace this character with a regular character
            ///				as part of the undeletion process.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Name;

            /// File extension
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] Extension;

            /// File or directory attributes
            public byte Attributes;
            public byte Reserved;

            /// File creation time, fine resolution, multiples of 10ms from 0 to 199.
            public byte CreateTimeFine;

            /// File creation time, coarse resolution.
            /// The following bitmask encodes time information:
            ///		15-11 	Hours (0-23)
            ///		10-5 	Minutes (0-59)
            ///		4-0 	Seconds/2 (0-29)
            public ushort CreateTimeCoarse;

            /// File creation date. The following bitmask encodes the information:
            ///		15-9 	Year (0 = 1980, 127 = 2107)
            ///		8-5 	Month (1 = January, 12 = December)
            ///		4-0 	Day (1 - 31)
            public ushort CreateDate;

            /// Last access date.  The following bitmask encodes the information:
            ///		15-9 	Year (0 = 1980, 127 = 2107)
            ///		8-5 	Month (1 = January, 12 = December)
            ///		4-0 	Day (1 - 31)
            public ushort LastAccessDate;

            // union
            private ushort _unionField;
            // EA-Index (used by OS/2 and NT) in FAT12 and FAT16
            public ushort EAIndex
            {
                get => _unionField;
                set => _unionField = value;
            }
            // High 2 bytes of first cluster number in FAT32
            public ushort StartClusterHigh
            {
                get => _unionField;
                set => _unionField = value;
            }

            /// File modification time. The following bitmask encodes time information:
            ///		15-11 	Hours (0-23)
            ///		10-5 	Minutes (0-59)
            ///		4-0 	Seconds/2 (0-29)
            public ushort ModifyTime;

            /// File modification date. The following bitmask encodes the information:
            ///		15-9 	Year (0 = 1980, 127 = 2107)
            ///		8-5 	Month (1 = January, 12 = December)
            ///		4-0 	Day (1 - 31)
            public ushort ModifyDate;

            /// The low 16 bits of the starting cluster for the file for FAT32, the starting
            /// cluster for the file in FAT12/16. Entries with the Volume Label flag,
            /// subdirectory ".." pointing to root, and empty files with size 0 should have
            /// first cluster 0.
            public ushort StartClusterLow;

            /// The size of the file.
            public uint FileSize;
        }

        /// Represents a long file name directory entry.
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        public struct FatLfnDirectoryEntry
        {
            /// Sequence identifier. The last entry has bit 0x40 set.
            public byte Sequence;

            /// Unicode characters of name.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public char[] Name1;

            /// Attributes: always 0x0F.
            public byte Attributes;
            public byte Reserved;

            /// Checksum for DOS file name.
            public byte Checksum;

            /// Second part of name.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public char[] Name2;

            /// Reserved. Set to 0.
            public ushort Reserved2;

            /// Third part of name.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public char[] Name3;
        }

        /// The collection of Fat Directory entries.
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct FatDirectoryEntry
        {
            // union
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            private byte[] _entryData;

            public Fat8Dot3DirectoryEntry Short
            {
                get
                {
                    //var span = new Span<byte>(_entryData);
                    //return MemoryMarshal.AsRef<Fat8Dot3DirectoryEntry>(span);
                    //var span = new ReadOnlySpan<byte>(_entryData);
                    //return MemoryMarshal.Read<Fat8Dot3DirectoryEntry>(span);
                    return MarshalHelper.BytesToStruct<Fat8Dot3DirectoryEntry>(ref _entryData);
                }
            }

            public FatLfnDirectoryEntry LongFileName
            {
                get
                {
                    //var span = new Span<byte>(_entryData);
                    //return MemoryMarshal.AsRef<FatLfnDirectoryEntry>(span);
                    //var span = new ReadOnlySpan<byte>(_entryData);
                    //return MemoryMarshal.Read<FatLfnDirectoryEntry>(span);
                    return MarshalHelper.BytesToStruct<FatLfnDirectoryEntry>(ref _entryData);
                }
            }
        }
    }
}
