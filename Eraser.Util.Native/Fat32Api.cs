using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Eraser.Util
{
    public unsafe class Fat32Api : FatApi
    {
        public Fat32Api(VolumeInfo info) : base(info)
        {
            //Sanity checks: check that this volume is FAT32!
            if (info.VolumeFormat != "FAT32")
                throw new ArgumentException(S._("The volume provided is not a FAT32 volume."));
        }

        public Fat32Api(VolumeInfo info, Stream stream) : base(stream)
        {
            //Sanity checks: check that this volume is FAT32!
            if (info.VolumeFormat != "FAT32")
                throw new ArgumentException(S._("The volume provided is not a FAT32 volume."));
        }

        public override void LoadFat()
        {
            uint fatSize = SectorSizeToSize(BootSector.Fat32ParameterBlock.SectorsPerFat);
            Fat = new byte[fatSize];

            //Seek to the FAT
            VolumeStream.Seek((long)SectorToOffset(BootSector.ReservedSectorCount), SeekOrigin.Begin);
            //Read the FAT
            VolumeStream.Read(Fat, 0, (int)fatSize);
        }

        public override FatDirectoryBase LoadDirectory(uint cluster, string name, FatDirectoryBase parent)
        {
            return new Directory(name, parent, cluster, this);
        }

        internal override long ClusterToOffset(uint cluster)
        {
            ulong sector = BootSector.ReservedSectorCount +                                     //Reserved area
                (ulong)BootSector.FatCount * BootSector.Fat32ParameterBlock.SectorsPerFat +     //FAT area
                ((ulong)cluster - 2) * BootSector.SectorsPerCluster;

            return (long)SectorToOffset(sector);
        }

        internal override bool IsClusterAllocated(uint cluster)
        {
            uint fatVal = GetFatValue(cluster);
            if (
                fatVal <= 0x00000001 ||
                (fatVal >= 0x0FFFFFF0 && fatVal <= 0x0FFFFFF6) ||
                fatVal == 0x0FFFFFF7
            )
                return false;

            return true;
        }

        internal override uint GetNextCluster(uint cluster)
        {
            uint fatVal = GetFatValue(cluster);
            if (fatVal <= 0x00000001 || (fatVal >= 0x0FFFFFF0 && fatVal <= 0x0FFFFFF6))
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
            else if (fatVal == 0x0FFFFFF7)
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
            else if (fatVal >= 0x0FFFFFF8)
                return 0xFFFFFFFF;
            else
                return fatVal;
        }

        internal override uint FileSize(uint cluster)
        {
            uint result = 1;
            while (true)
            {
                uint nextCluster = GetFatValue(cluster);
                if (nextCluster <= 0x00000001 || (nextCluster >= 0x0FFFFFF0 && nextCluster <= 0x0FFFFFF6))
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
                else if (nextCluster == 0x0FFFFFF7)
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
                else if (nextCluster >= 0x0FFFFFF8)
                    return ClusterSizeToSize(result);
                else
                    cluster = nextCluster;
                result++;
            }
        }

        internal override uint DirectoryToCluster(string path)
        {
            //The path must start with a backslash as it must be volume-relative.
            if (!string.IsNullOrEmpty(path))
            {
                if (path[0] != '\\')
                    throw new ArgumentException(S._("The path provided is not volume relative. Volume relative paths must begin with a backslash."));
                path = path.Substring(1);
            }

            //Chop the path into it's constituent directory components
            string[] components = path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }
            );

            //Traverse the directories until we get the cluster we want.
            uint cluster = BootSector.Fat32ParameterBlock.RootDirectoryCluster;
            FatDirectoryBase parentDir = null;
            foreach (string component in components)
            {
                if (string.IsNullOrEmpty(component))
                    break;

                parentDir = LoadDirectory(
                    cluster,
                    parentDir == null ? string.Empty : parentDir.Name,
                    parentDir
                );

                cluster = parentDir.Items[component].Cluster;
            }

            return cluster;
        }

        private uint GetFatValue(uint cluster)
        {
            int offset = (int)cluster * 4;
            if (offset + 4 > Fat.Length)
                return 0xFFFFFFFF;

            return BitConverter.ToUInt32(Fat, offset) & 0x0FFFFFFF;
        }

        ~Fat32Api() => Dispose();

        private class Directory : FatDirectory
        {
            public Directory(string name, FatDirectoryBase parent, uint cluster, Fat32Api api)
                : base(name, parent, cluster, api) { }

            protected override uint GetStartCluster(ref Fat.FatDirectoryEntry directory)
            {
                if (directory.Short.Attributes == 0x0F)
                    throw new ArgumentException("The provided directory is a long file name.");

                return directory.Short.StartClusterLow | ((uint)directory.Short.StartClusterHigh << 16);
            }
        }
    }
}
