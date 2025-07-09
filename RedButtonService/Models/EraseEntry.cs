using Eraser.Util;

namespace RedButtonService.Models
{
    public class EraseEntry
    {
        public enum EraseEntryType
        {
            File,
            Dir,
            RecycleBin,
            Unused,
            Drive
        }

        public EraseEntryType Type { get; set; }
        public string File { get; set; }
        public string Dir { get; set; }
        public string Drive { get; set; }
        public string VolumeId { get; set; }

        public List<string> GetCmd()
        {
            switch (Type)
            {
                case EraseEntryType.File:
                    if (string.IsNullOrEmpty(File)) return null;
                    return [$"file={File}"];
                case EraseEntryType.Dir:
                    if (string.IsNullOrEmpty(Dir)) return null;
                    return [$"dir={Dir}"];
                case EraseEntryType.RecycleBin:
                    return ["recyclebin"];
                case EraseEntryType.Unused:
                    var drives = GetDrives();
                    if (drives == null || drives.Length == 0) return null;
                    List<string> result = new List<string>();
                    foreach (var drive in drives)
                    {
                        result.Add($"unused={drive}");
                    }
                    return result;
                case EraseEntryType.Drive:
                    var volumeId = GetVolumeId();
                    if (string.IsNullOrEmpty(volumeId)) return null;
                    return [$"drive={volumeId}"];
                default:
                    return null;
            }
        }

        public string[] GetPaths()
        {
            switch (Type)
            {
                case EraseEntryType.File:
                    return new[] { File };
                case EraseEntryType.Dir:
                    return new[] { Dir };
                case EraseEntryType.RecycleBin:
                    return null;
                case EraseEntryType.Unused:
                    return GetDrives();
                case EraseEntryType.Drive:
                    return GetDrives();
                default:
                    return null;
            }
        }

        private string[] GetDrives()
        {
            if (!string.IsNullOrEmpty(Drive)) return new[] { Drive };

            if (!string.IsNullOrEmpty(VolumeId))
            {
                foreach (var volume in VolumeInfo.Volumes)
                {
                    if (VolumeId == volume.VolumeId)
                    {
                        return volume.MountPoints.Select(x => x.FullName).ToArray();
                    }
                }
            }

            return null;
        }

        private string GetVolumeId()
        {
            if (!string.IsNullOrEmpty(VolumeId)) return VolumeId;

            if (!string.IsNullOrEmpty(Drive))
            {
                foreach (var volume in VolumeInfo.Volumes)
                {
                    foreach (var mountPoint in volume.MountPoints)
                    {
                        if (mountPoint.FullName == Drive)
                            return volume.VolumeId;
                    }
                }
            }

            return null;
        }
    }
}
