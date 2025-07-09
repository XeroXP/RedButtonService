/* 
 * $Id: DriveErasureTargetConfigurer.cs 2993 2021-09-25 17:23:27Z gtrant $
 * Copyright 2008-2021 The Eraser Project
 * Original Author: Joel Low <lowjoel@users.sourceforge.net>
 * Modified By:
 * 
 * This file is part of Eraser.
 * 
 * Eraser is free software: you can redistribute it and/or modify it under the
 * terms of the GNU General Public License as published by the Free Software
 * Foundation, either version 3 of the License, or (at your option) any later
 * version.
 * 
 * Eraser is distributed in the hope that it will be useful, but WITHOUT ANY
 * WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR
 * A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * 
 * A copy of the GNU General Public License can be found at
 * <http://www.gnu.org/licenses/>.
 */

using Eraser.Plugins.ExtensionPoints;
using Eraser.Util;
using Eraser.Util.ExtensionMethods;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Eraser.DefaultPlugins
{
	partial class DriveErasureTargetConfigurer : IErasureTargetConfigurer
	{
        /// <summary>
		/// Represents an item in the list of drives.
		/// </summary>
		private class PartitionItem
        {
            public override string ToString()
            {
                if (!string.IsNullOrEmpty(Cache))
                    return Cache;

                if (PhysicalDrive != null)
                {
                    try
                    {
                        Cache = S._("Hard disk {0} ({1})", PhysicalDrive.Index,
                            new FileSize(PhysicalDrive.Size));
                    }
                    catch (IOException)
                    {
                        Cache = S._("Hard disk {0}", PhysicalDrive.Index);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Cache = S._("Hard disk {0}", PhysicalDrive.Index);
                    }
                }
                else if (Volume != null)
                {
                    try
                    {
                        if (Volume.IsMounted)
                            Cache = Volume.MountPoints[0].GetDescription();
                        else if (Volume.PhysicalDrive != null)
                            Cache = S._("Partition {0} ({1})",
                                Volume.PhysicalDrive.Volumes.IndexOf(Volume) + 1,
                                new FileSize(Volume.TotalSize));
                        else
                            Cache = S._("Partition ({0})", new FileSize(Volume.TotalSize));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (Volume.PhysicalDrive != null)
                            Cache = S._("Partition {0}",
                                Volume.PhysicalDrive.Volumes.IndexOf(Volume) + 1);
                        else
                            Cache = S._("Partition");
                    }
                }
                else
                    throw new InvalidOperationException();

                return Cache;
            }

            /// <summary>
            /// Stores the display text for rapid access.
            /// </summary>
            private string Cache;

            /// <summary>
            /// The Physical drive this partition refers to.
            /// </summary>
            public PhysicalDriveInfo PhysicalDrive;

            /// <summary>
            /// The volume this partition refers to.
            /// </summary>
            public VolumeInfo Volume;
        }

        List<PartitionItem> partitionCmbItems = new List<PartitionItem>();
        PartitionItem partitionCmbSelectedItem = null;

        public DriveErasureTargetConfigurer()
        {
            //Populate the drives list
            List<VolumeInfo> volumes = new List<VolumeInfo>();
            foreach (PhysicalDriveInfo drive in PhysicalDriveInfo.Drives)
            {
                PartitionItem item = new PartitionItem();
                item.PhysicalDrive = drive;
                partitionCmbItems.Add(item);

                foreach (VolumeInfo volume in drive.Volumes)
                {
                    item = new PartitionItem();
                    item.Volume = volume;

                    partitionCmbItems.Add(item);
                    volumes.Add(volume);
                }
            }

            //And then add volumes which aren't accounted for (notably, Dynamic volumes)
            foreach (VolumeInfo volume in VolumeInfo.Volumes)
            {
                if (volumes.IndexOf(volume) == -1 && volume.VolumeType == DriveType.Fixed)
                {
                    PartitionItem item = new PartitionItem();
                    item.Volume = volume;

                    partitionCmbItems.Insert(0, item);
                    volumes.Add(volume);
                }
            }

            if (partitionCmbItems.Count != 0)
                partitionCmbSelectedItem = partitionCmbItems[0];
        }

        #region IConfigurer<ErasureTarget> Members

        public void LoadFrom(IErasureTarget target)
        {
            DriveErasureTarget partition = target as DriveErasureTarget;
            if (partition == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            foreach (PartitionItem item in partitionCmbItems)
                if ((item.PhysicalDrive != null &&
                        item.PhysicalDrive.Equals(partition.PhysicalDrive)) ||
                    (item.Volume != null && item.Volume.Equals(partition.Volume)))
                {
                    partitionCmbSelectedItem = item;
                    break;
                }
        }

        public bool SaveTo(IErasureTarget target)
        {
            DriveErasureTarget partition = target as DriveErasureTarget;
            if (partition == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            PartitionItem item = partitionCmbSelectedItem;

            //Make sure we don't set both Volume and PhysicalDrive
            partition.PhysicalDrive = null;

            //Then set the proper values.
            partition.Volume = item.Volume;
            partition.PhysicalDrive = item.PhysicalDrive;
            return true;
        }

        #endregion

        #region ICliConfigurer<ErasureTarget> Members

        public string Help()
        {
            return S._(@"drive               Erases partitions, volumes or drives
  arguments:
    drive=\Device\Harddisk<index>
    drive=\\.\PhysicalDrive<index>
    drive=\\?\Volume<guid>");
        }

        public bool ProcessArgument(string argument)
        {
            //The hard disk index
            Regex hardDiskRegex = new Regex("^(drive=)?\\\\Device\\\\Harddisk(?<disk>[\\d]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

            //PhysicalDrive index
            Regex physicalDriveIndex = new Regex("^(drive=)?\\\\\\\\\\.\\\\PhysicalDrive(?<disk>[\\d]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

            //The volume GUID
            Regex volumeRegex = new Regex("^(drive=)?\\\\\\\\\\?\\\\Volume\\{(?<guid>([0-9a-f-]+))\\}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

            //Try to get the hard disk index.
            Match match = hardDiskRegex.Match(argument);
            if (!match.Groups["disk"].Success)
                match = physicalDriveIndex.Match(argument);
            if (match.Groups["disk"].Success)
            {
                //Get the index of the disk.
                int index = Convert.ToInt32(match.Groups["disk"].Value);

                //Create a physical drive info object for the target disk
                PhysicalDriveInfo target = new PhysicalDriveInfo(index);

                //Select it in the GUI.
                foreach (PartitionItem item in partitionCmbItems)
                    if (item.PhysicalDrive != null && item.PhysicalDrive.Equals(target))
                        partitionCmbSelectedItem = item;

                return true;
            }

            //Try to get the volume GUID
            match = volumeRegex.Match(argument);
            if (match.Groups["guid"].Success)
            {
                //Find the volume GUID
                Guid guid = new Guid(match.Groups["guid"].Value);

                //Create a volume info object for the target volume
                VolumeInfo target = new VolumeInfo(string.Format(CultureInfo.InvariantCulture,
                    "\\\\?\\Volume{{{0}}}\\", guid));

                //Select it in the GUI.
                foreach (PartitionItem item in partitionCmbItems)
                    if (item.Volume != null && item.Volume.Equals(target))
                        partitionCmbSelectedItem = item;

                return true;
            }

            return false;
        }

        #endregion
    }
}
