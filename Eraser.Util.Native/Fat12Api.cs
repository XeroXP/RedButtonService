using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Eraser.Util
{
    public class Fat12Api : Fat12Or16Api
    {
        public Fat12Api(VolumeInfo info) : base(info)
        {
            //Sanity checks: check that this volume is FAT16!
            if (!IsFat12() || info.VolumeFormat == "FAT16")
                throw new ArgumentException(S._("The volume provided is not a FAT12 volume."));
        }

        public Fat12Api(VolumeInfo info, Stream stream) : base(info, stream)
        {
            //Sanity checks: check that this volume is FAT16!
            if (!IsFat12() || info.VolumeFormat == "FAT16")
                throw new ArgumentException(S._("The volume provided is not a FAT12 volume."));
        }

        internal override bool IsClusterAllocated(uint cluster)
        {
            uint nextCluster = GetFatValue(cluster);

            if (
                nextCluster <= 0x001 ||
                (nextCluster >= 0xFF0 && nextCluster <= 0xFF6) ||
                nextCluster == 0xFF7
            )
                return false;

            return true;
        }

        internal override uint GetNextCluster(uint cluster)
        {
            uint nextCluster = GetFatValue(cluster);
            if (nextCluster <= 0x001 || (nextCluster >= 0xFF0 && nextCluster <= 0xFF6))
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
            else if (nextCluster == 0xFF7)
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
            else if (nextCluster >= 0xFF8)
                return 0xFFFFFFFF;
            else
                return nextCluster;
        }

        internal override uint FileSize(uint cluster)
        {
            uint result = 1;
            while (true)
            {
                uint nextCluster = GetFatValue(cluster);
                if (nextCluster <= 0x001 || (nextCluster >= 0xFFF0 && nextCluster <= 0xFF6))
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
                else if (nextCluster == 0xFF7)
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
                else if (nextCluster >= 0xFF8)
                    return ClusterSizeToSize(result);
                else
                    cluster = nextCluster;
                result++;
            }
        }

        private uint GetFatValue(uint cluster)
        {
            //Get the pointer to the FAT entry. Round the cluster value down to the nearest
            //even number (since 2 clusters share 3 bytes)
            int fatIndex = (int)((cluster & ~1) / 2) * 3;
            byte[] fatBytes = new byte[3];
            Buffer.BlockCopy(Fat, fatIndex, fatBytes, 0, 3);
            uint fatValue = (uint)(fatBytes[0] | (fatBytes[1] << 8) | (fatBytes[2] << 16));

            //Get the correct half of the 24 bits. If the cluster is odd we take the 12 least significant bits
            if ((cluster & 1) != 0)
                fatValue >>= 12;
            else
                fatValue &= 0xFFF;

            //Return the result.
            return fatValue;
        }
    }
}
