using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Eraser.Util
{
    public abstract unsafe class Fat12Or16Api : FatApi
    {
        protected Fat12Or16Api(VolumeInfo info) : base(info) {
            //Sanity checks: check that this volume is FAT12 or FAT16!
            if (info.VolumeFormat != "FAT12" && info.VolumeFormat != "FAT16")
			    throw new ArgumentException(S._("The volume provided is not a FAT12 or FAT16 volume."));
        }
        protected Fat12Or16Api(VolumeInfo info, Stream stream) : base(stream) {
            //Sanity checks: check that this volume is FAT12 or FAT16!
            if (info.VolumeFormat != "FAT12" && info.VolumeFormat != "FAT16")
			    throw new ArgumentException(S._("The volume provided is not a FAT12 or FAT16 volume."));
        }

        public override void LoadFat()
        {
            uint fatSize = SectorSizeToSize(BootSector.SectorsPerFat);
            Fat = new byte[fatSize];

            //Seek to the FAT
            VolumeStream.Seek((long)SectorToOffset(BootSector.ReservedSectorCount), SeekOrigin.Begin);
            //Read the FAT
            VolumeStream.Read(Fat, 0, (int)fatSize);
        }

        public override FatDirectoryBase LoadDirectory(uint cluster, string name, FatDirectoryBase parent)
        {
            //Return the root directory if we get cluster 0, name is blank and the parent is null
            if (cluster == 0 && string.IsNullOrEmpty(name) && parent == null)
                return new RootDirectory(this);

            return new Directory(name, parent, cluster, this);
        }

        internal override long ClusterToOffset(uint cluster)
        {
            ulong sector = BootSector.ReservedSectorCount +                                             //Reserved area
                (ulong)BootSector.FatCount * BootSector.SectorsPerFat +                                 //FAT area
                (ulong)(BootSector.RootDirectoryEntryCount * Marshal.SizeOf<Fat.FatDirectoryEntry>() /  //Root directory area
                    BootSector.BytesPerSector) +
                ((ulong)cluster - 2) * BootSector.SectorsPerCluster;

            return (long)SectorToOffset(sector);
        }

        internal override uint DirectoryToCluster(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (path[0] != '\\')
                    throw new ArgumentException(S._("The path provided is not volume relative. Volume relative paths must begin with a backslash."));

                path = path.Substring(1);
            }

            //Chop the path into it's constituent directory components
            string[] components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            //Traverse the directories until we get the cluster we want.
            uint cluster = 0;
            FatDirectoryBase parentDir = null;
            foreach (string component in components)
            {
                if (string.IsNullOrEmpty(component)) break;

                parentDir = LoadDirectory(cluster,
                    parentDir == null ? string.Empty : parentDir.Name,
                    parentDir);

                cluster = parentDir.Items[component].Cluster;
            }

            return cluster;
        }

        protected bool IsFat12()
        {
            ulong numberOfSectors = BootSector.SectorCount16 == 0 ?
                BootSector.SectorCount32 : BootSector.SectorCount16;

            ulong availableSectors = numberOfSectors - (
                BootSector.ReservedSectorCount +                                                        //Reserved area
                (ulong)BootSector.FatCount * BootSector.SectorsPerFat +                                 //FAT area
                (ulong)(BootSector.RootDirectoryEntryCount * Marshal.SizeOf<Fat.FatDirectoryEntry>() /  //Root directory area
                    BootSector.BytesPerSector)
                );

            ulong numberOfClusters = availableSectors / BootSector.SectorsPerCluster;

            return numberOfClusters <= 0xFF0;
        }

        ~Fat12Or16Api() => this.Dispose();

        private class RootDirectory : FatDirectoryBase
        {
            private Fat12Or16Api Api;

            public RootDirectory(Fat12Or16Api api)
                : base(string.Empty, null, 0)
            {
                Api = api;
            }

            protected override void ReadDirectory()
            {
                //Calculate the starting sector of the root directory
                ulong startPos = Api.SectorToOffset(Api.BootSector.ReservedSectorCount +
                    (ulong)Api.BootSector.FatCount * Api.BootSector.SectorsPerFat);
                int directoryLength = Api.BootSector.RootDirectoryEntryCount *
                    Marshal.SizeOf<Fat.FatDirectoryEntry>();

                byte[] buffer = new byte[directoryLength];
                Api.VolumeStream.Seek((long)startPos, SeekOrigin.Begin);
                Api.VolumeStream.Read(buffer, 0, directoryLength);

                uint directorySize = Api.BootSector.RootDirectoryEntryCount;
                Directory = new Fat.FatDirectoryEntry[directorySize];

                /*ReadOnlySpan<byte> byteSpan = buffer.AsSpan();
                ReadOnlySpan<Fat.FatDirectoryEntry> entrySpan = MemoryMarshal.Cast<byte, Fat.FatDirectoryEntry>(byteSpan);
                entrySpan.CopyTo(Directory);*/
                Directory = MarshalHelper.BytesToStructs<Fat.FatDirectoryEntry>(ref buffer, directorySize);

                ParseDirectory();
            }

            protected override void WriteDirectory()
            {
                //Calculate the starting sector of the root directory
                ulong startPos = Api.SectorToOffset(Api.BootSector.ReservedSectorCount +
                    (ulong)Api.BootSector.FatCount * Api.BootSector.SectorsPerFat);
                int directoryLength = Api.BootSector.RootDirectoryEntryCount *
                    Marshal.SizeOf<Fat.FatDirectoryEntry>();

                byte[] buffer = new byte[directoryLength];

                /*Span<byte> bufferSpan = buffer.AsSpan();
                Span<Fat.FatDirectoryEntry> entrySpan = MemoryMarshal.Cast<byte, Fat.FatDirectoryEntry>(bufferSpan);
                Directory.CopyTo(entrySpan);*/
                buffer = MarshalHelper.StructsToBytes(Directory);

                Api.VolumeStream.Seek((long)startPos, SeekOrigin.Begin);
                Api.VolumeStream.Write(buffer, 0, directoryLength);
            }

            protected override uint GetStartCluster(ref Fat.FatDirectoryEntry directory)
            {
                if (directory.Short.Attributes == 0x0F)
                    throw new ArgumentException(S._("The provided directory is a long file name."));

                return directory.Short.StartClusterLow;
            }
        }

        private class Directory : FatDirectory
        {
            public Directory(string name, FatDirectoryBase parent, uint cluster, Fat12Or16Api api)
                : base(name, parent, cluster, api)
            {
            }

            protected override uint GetStartCluster(ref Fat.FatDirectoryEntry directory)
            {
                if (directory.Short.Attributes == 0x0F)
                    throw new ArgumentException(S._("The provided directory is a long file name."));

                return directory.Short.StartClusterLow;
            }
        }
    }
}
