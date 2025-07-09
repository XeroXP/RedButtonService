using Eraser.Util.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Eraser.Util
{
    /// <summary>
    /// Represents an abstract API to interface with FAT file systems
    /// </summary>
    public abstract class FatApi : IDisposable
    {
        protected Stream VolumeStream { get; }
        protected Fat.FatBootSector BootSector { get; }
        protected byte[] Fat { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// 
        /// <param name="info">The volume to create the FAT API for. The volume
        /// handle created has read access only.</param>
        protected FatApi(VolumeInfo info)
        {
            BootSector = new Fat.FatBootSector();
            Fat = null;

            //Open the handle to the drive
            VolumeStream = info.Open(FileAccess.Read);

            //Then read the boot sector for information
            int bootSectorSize = Marshal.SizeOf<Fat.FatBootSector>();
            byte[] bootSectorBytes = new byte[bootSectorSize];
            VolumeStream.Seek(0, SeekOrigin.Begin);
            int bytesRead = VolumeStream.Read(bootSectorBytes, 0, bootSectorSize);
            if (bytesRead != bootSectorSize)
            {
                throw new IOException($"Expected {bootSectorSize} bytes, got {bytesRead} bytes");
            }
            BootSector = MarshalHelper.BytesToStruct<Fat.FatBootSector>(ref bootSectorBytes);

            //Then load the FAT
            LoadFat();
        }

        protected FatApi(Stream stream)
        {
            BootSector = new Fat.FatBootSector();
            Fat = null;

            //Open the handle to the drive
            VolumeStream = stream;

            //Then read the boot sector for information
            int bootSectorSize = Marshal.SizeOf<Fat.FatBootSector>();
            byte[] bootSectorBytes = new byte[bootSectorSize];
            VolumeStream.Seek(0, SeekOrigin.Begin);
            int bytesRead = VolumeStream.Read(bootSectorBytes, 0, bootSectorSize);
            BootSector = MarshalHelper.BytesToStruct<Fat.FatBootSector>(ref bootSectorBytes);

            //Then load the FAT
            LoadFat();
        }

        public abstract void LoadFat();

        public FatDirectoryBase LoadDirectory(string directory)
        {
            //Return the root directory if nothing is specified
            if (string.IsNullOrEmpty(directory))
                return LoadDirectory(DirectoryToCluster(directory), string.Empty, null);

            char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            int lastIndex = directory.LastIndexOfAny(separators);
            return LoadDirectory(DirectoryToCluster(directory),
                directory.Substring(lastIndex + 1),
                LoadDirectory(directory.Substring(0, lastIndex)));
        }

        public abstract FatDirectoryBase LoadDirectory(uint cluster, string name, FatDirectoryBase parent);

        internal virtual ulong SectorToOffset(ulong sector) =>
            sector * BootSector.BytesPerSector;

        internal uint SectorSizeToSize(uint size) =>
            size * BootSector.BytesPerSector;

        internal uint ClusterSizeToSize(uint size) =>
            (uint)(size * (BootSector.BytesPerSector * BootSector.SectorsPerCluster));

        internal abstract bool IsClusterAllocated(uint cluster);
        internal abstract uint GetNextCluster(uint cluster);
        internal abstract uint FileSize(uint cluster);

        internal byte[] GetFileContents(uint cluster)
        {
            if (!IsClusterAllocated(cluster))
                throw new ArgumentException(S._("The specified cluster is not used."));

            byte[] result = new byte[FileSize(cluster)];
            int clusterSize = (int)ClusterSizeToSize(1);
            int offset = 0;

            do
            {
                VolumeStream.Position = ClusterToOffset(cluster);
                VolumeStream.Read(result, offset, clusterSize);
                offset += clusterSize;
            } while ((cluster = GetNextCluster(cluster)) != 0xFFFFFFFF);

            return result;
        }

        internal void SetFileContents(byte[] buffer, uint cluster)
        {
            if (!IsClusterAllocated(cluster))
                throw new ArgumentException(S._("The specified cluster is not used."));

            if ((uint)buffer.Length != FileSize(cluster))
                throw new ArgumentException(S._("The provided file contents will not fit in the allocated file."));

            int clusterSize = (int)ClusterSizeToSize(1);
            for (int i = 0; i < buffer.Length; i += clusterSize)
            {
                VolumeStream.Seek(ClusterToOffset(cluster), SeekOrigin.Begin);
                VolumeStream.Write(buffer, i, clusterSize);
                cluster = GetNextCluster(cluster);
            }
        }

        internal abstract long ClusterToOffset(uint cluster);
        internal abstract uint DirectoryToCluster(string path);

        public void Dispose()
        {
            VolumeStream?.Dispose();
            Fat = null;
        }

        ~FatApi() => Dispose();
    }

    public enum FatDirectoryEntryType { File, Directory }

    public class FatDirectoryEntry
    {
        public string Name { get; }
        public string FullName => Parent == null ? Name : $"{Parent.FullName}{Path.DirectorySeparatorChar}{Name}";
        public FatDirectoryBase Parent { get; }
        public FatDirectoryEntryType EntryType { get; }
        public uint Cluster { get; }

        internal FatDirectoryEntry(string name, FatDirectoryBase parent,
            FatDirectoryEntryType type, uint cluster)
        {
            Name = name;
            Parent = parent;
            EntryType = type;
            Cluster = cluster;
        }
    }

    public abstract unsafe class FatDirectoryBase : FatDirectoryEntry
    {
        protected FatDirectoryBase(string name, FatDirectoryBase parent, uint cluster, FatApi api = null)
            : base(name, parent, FatDirectoryEntryType.Directory, cluster)
        {
            Api = api;
            Entries = new Dictionary<string, FatDirectoryEntry>();
            ReadDirectory();
        }

        public void ClearDeletedEntries()
        {
            List<Fat.FatDirectoryEntry> validEntries = new List<Fat.FatDirectoryEntry>();
            int directorySize = Directory.Length;

            //Parse the directory structures
            for (int index = 0; index < directorySize; index++)
            {
                //Check if we have checked the last valid entry
                if (Directory[index].Short.Name[0] == 0x00) break;
                //Skip deleted entries.
                if (Directory[index].Short.Name[0] == 0xE5) continue;

                if (Directory[index].Short.Attributes == 0x0F)
                {
                    int longFileNameBeginIndex = index;
                    byte sequence = 0;
                    while (index < directorySize && Directory[index].Short.Attributes == 0x0F)
                    {
                        if (Directory[index].Short.Name[0] == 0xE5)
                        {
                            index++;
                            continue;
                        }

                        bool isFirstEntry = (Directory[index].LongFileName.Sequence & 0x40) != 0;
                        if (!isFirstEntry && index != 0) //Second entry onwards
                        {
                            //Check that the checksum of the file name is the same as the previous
                            //long file name entry, to ensure no corruption has taken place
                            if (Directory[index-1].LongFileName.Checksum != Directory[index].LongFileName.Checksum)
                            {
                                index++;
                                continue;
                            }

                            //Check that the sequence is one less than the previous one.
                            if (sequence != (Directory[index].LongFileName.Sequence + 1))
                                throw new ArgumentException(S._("Invalid directory entry."));
                        }

                        sequence = (byte)(Directory[index].LongFileName.Sequence & ~0x40);
                        index++;
                    }

                    //Checksum the string
                    byte sum = 0;
                    fixed (byte* namePtr = Directory[index].Short.Name)
                    {
                        for (int j = 0; j < 11; j++)
                            sum = (byte)((sum << 7) | (sum >> 1) + namePtr[j]);
                    }

                    if (sum == Directory[index - 1].LongFileName.Checksum)
                    {
                        //The previous few entries contained the correct file name. Save these entries
                        for (int j = longFileNameBeginIndex; j <= index; j++)
                            validEntries.Add(Directory[j]);
                    }
                    else
                    {
                        index--;
                    }
                }
                validEntries.Add(Directory[index]);
            }

            //validEntries now contains the compacted list of directory entries. Zero
            //the memory used.
            Array.Clear(Directory, 0, Directory.Length);

            //Copy the memory back if we have any valid entries. The root directory can
            //be empty (no . and .. entries)
            for (int i = 0; i < validEntries.Count; i++)
                Directory[i] = validEntries[i];

            //Write the entries to disk
            WriteDirectory();
        }

        public Dictionary<string, FatDirectoryEntry> Items => Entries;

        protected abstract void ReadDirectory();
        protected abstract void WriteDirectory();
        protected abstract uint GetStartCluster(ref Fat.FatDirectoryEntry directory);

        protected void ParseDirectory()
        {
            //Clear the list of entries
            Entries.Clear();
            int directorySize = Directory.Length;

            //Parse the directory structures
            for (int index = 0; index < directorySize; index++)
            {
                //Check if we have checked the last valid entry
                if (Directory[index].Short.Name[0] == 0x00) break;
                //Skip deleted entries.
                if (Directory[index].Short.Name[0] == 0xE5) continue;

                if (Directory[index].Short.Attributes == 0x0F)
                {
                    string longFileName = "";
                    byte sequence = 0;
                    while (index < directorySize && Directory[index].Short.Attributes == 0x0F)
                    {
                        if (Directory[index].Short.Name[0] == 0xE5)
                        {
                            index++;
                            continue;
                        }

                        bool isFirstEntry = (Directory[index].LongFileName.Sequence & 0x40) != 0;
                        if (!isFirstEntry && index != 0) //Second entry onwards
                        {
                            //Check that the checksum of the file name is the same as the previous
                            //long file name entry, to ensure no corruption has taken place
                            if (Directory[index - 1].LongFileName.Checksum != Directory[index].LongFileName.Checksum)
                            {
                                index++;
                                continue;
                            }

                            //Check that the sequence is one less than the previous one.
                            if (sequence != (Directory[index].LongFileName.Sequence + 1))
                                throw new ArgumentException(S._("Invalid directory entry."));
                        }

                        sequence = (byte)(Directory[index].LongFileName.Sequence & ~0x40);
                        longFileName = new string(Directory[index].LongFileName.Name1) +
                            new string(Directory[index].LongFileName.Name2) +
                            new string(Directory[index].LongFileName.Name3) + longFileName;
                        index++;
                    }

                    //Checksum the string
                    byte sum = 0;
                    fixed (byte* namePtr = Directory[index].Short.Name)
                    {
                        for (int j = 0; j < 11; j++)
                            sum = (byte)((sum << 7) | (sum >> 1) + namePtr[j]);
                    }

                    if (sum == Directory[index - 1].LongFileName.Checksum)
                    {
                        //fileName contains the correct full long file name, strip the file name of the
                        //invalid characters.
                        string fileName = longFileName.TrimEnd('\0');
                        uint clusterI = GetStartCluster(ref Directory[index]);
                        var entryTypeI = (Directory[index].Short.Attributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0
                            ? FatDirectoryEntryType.Directory
                            : FatDirectoryEntryType.File;

                        Entries[fileName] = new FatDirectoryEntry(
                            fileName, this, entryTypeI, clusterI);
                    }
                    else
                    {
                        index--;
                        continue;
                    }
                }

                //Skip the dot directories.
                if (Directory[index].Short.Name[0] == '.') continue;

                //Substitute 0x05 with 0xE5
                if (Directory[index].Short.Name[0] == 0x05)
                    Directory[index].Short.Name[0] = (byte)0xE5;

                //Then read the 8.3 entry for the file details
                string name = Encoding.ASCII.GetString(Directory[index].Short.Name).Trim();
                string ext = Encoding.ASCII.GetString(Directory[index].Short.Extension).Trim();
                //If the extension is blank, don't care about it
                string shortFileName = string.IsNullOrEmpty(ext) ? name : $"{name}.{ext}";

                uint cluster = GetStartCluster(ref Directory[index]);
                var entryType = (Directory[index].Short.Attributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0
                    ? FatDirectoryEntryType.Directory
                    : FatDirectoryEntryType.File;

                Entries[shortFileName] = new FatDirectoryEntry(
                    shortFileName, this, entryType, cluster);
            }
        }

        protected Fat.FatDirectoryEntry[] Directory;
        private Dictionary<string, FatDirectoryEntry> Entries;
        protected FatApi Api;
    }

    public abstract class FatDirectory : FatDirectoryBase
    {

        protected FatDirectory(string name, FatDirectoryBase parent, uint cluster, FatApi api)
            : base(name, parent, cluster, api)
        { }

        protected override void ReadDirectory()
        {
            byte[] dir = Api.GetFileContents(Cluster);
            int entryCount = dir.Length / Marshal.SizeOf<Fat.FatDirectoryEntry>();
            Directory = new Fat.FatDirectoryEntry[entryCount];

            Directory = MarshalHelper.BytesToStructs<Fat.FatDirectoryEntry>(ref dir, (uint)entryCount);
            /*ReadOnlySpan<byte> byteSpan = dir.AsSpan();
            ReadOnlySpan<Fat.FatDirectoryEntry> entrySpan = MemoryMarshal.Cast<byte, Fat.FatDirectoryEntry>(byteSpan);
            entrySpan.CopyTo(Directory);*/

            ParseDirectory();
        }

        protected override void WriteDirectory()
        {
            int size = Directory.Length * Marshal.SizeOf<Fat.FatDirectoryEntry>();
            byte[] buffer = new byte[size];

            /*Span<byte> bufferSpan = buffer.AsSpan();
            Span<Fat.FatDirectoryEntry> entrySpan = MemoryMarshal.Cast<byte, Fat.FatDirectoryEntry>(bufferSpan);
            Directory.CopyTo(entrySpan);*/
            buffer = MarshalHelper.StructsToBytes(Directory);

            Api.SetFileContents(buffer, Cluster);
        }
    }
}
