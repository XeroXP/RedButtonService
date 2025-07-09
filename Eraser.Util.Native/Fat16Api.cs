using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Eraser.Util
{
    public class Fat16Api : Fat12Or16Api
    {
        private ushort[] fat16;

        public Fat16Api(VolumeInfo info) : base(info)
        {
            //Sanity checks: check that this volume is FAT16!
            if (IsFat12() || info.VolumeFormat == "FAT12")
                throw new ArgumentException(S._("The volume provided is not a FAT16 volume."));
        }

        public Fat16Api(VolumeInfo info, Stream stream) : base(info, stream)
        {
            //Sanity checks: check that this volume is FAT16!
            if (IsFat12() || info.VolumeFormat == "FAT12")
                throw new ArgumentException(S._("The volume provided is not a FAT16 volume."));
        }

        public override void LoadFat()
        {
            base.LoadFat();

            // Преобразуем байтовый массив FAT в массив 16-битных значений
            int fatSize = Fat.Length / 2;
            fat16 = new ushort[fatSize];

            for (int i = 0; i < fatSize; i++)
            {
                fat16[i] = (ushort)(Fat[i * 2] | (Fat[i * 2 + 1] << 8));
            }
        }

        internal override bool IsClusterAllocated(uint cluster)
        {
            if (
                fat16[cluster] <= 0x0001 ||
                (fat16[cluster] >= 0xFFF0 && fat16[cluster] <= 0xFFF6) ||
                fat16[cluster] == 0xFFF7
            )
                return false;

            return true;
        }

        internal override uint GetNextCluster(uint cluster)
        {
            if (fat16[cluster] <= 0x0001 || (fat16[cluster] >= 0xFFF0 && fat16[cluster] <= 0xFFF6))
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
            else if (fat16[cluster] == 0xFFF7)
                throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
            else if (fat16[cluster] >= 0xFFF8)
                return 0xFFFFFFFF;
            else
                return fat16[cluster];
        }

        internal override uint FileSize(uint cluster)
        {
            uint result = 1;
            while (true)
            {
                if (fat16[cluster] <= 0x0001 || (fat16[cluster] >= 0xFFF0 && fat16[cluster] <= 0xFFF6))
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked free."));
                else if (fat16[cluster] == 0xFFF7)
                    throw new ArgumentException(S._("Invalid FAT cluster: cluster is marked bad."));
                else if (fat16[cluster] >= 0xFFF8)
                    return ClusterSizeToSize(result);
                else
                    cluster = fat16[cluster];
                result++;
            }
        }
    }
}
