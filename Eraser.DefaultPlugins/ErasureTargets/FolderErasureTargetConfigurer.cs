/* 
 * $Id: FolderErasureTargetConfigurer.cs 2993 2021-09-25 17:23:27Z gtrant $
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

using System;
using System.Text.RegularExpressions;
using Eraser.Util;
using Eraser.Plugins.ExtensionPoints;

namespace Eraser.DefaultPlugins
{
	partial class FolderErasureTargetConfigurer : IErasureTargetConfigurer
	{
        private class FolderItem
        {
            public string Path = string.Empty;
            public string IncludeMask = string.Empty;
            public string ExcludeMask = string.Empty;
            public bool DeleteIfEmpty = true;
        }

        FolderItem folderItem = new FolderItem();

        #region IConfigurer<ErasureTarget> Members

        public void LoadFrom(IErasureTarget target)
        {
            FolderErasureTarget folder = target as FolderErasureTarget;
            if (folder == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            folderItem.Path = folder.Path;
            folderItem.IncludeMask = folder.IncludeMask;
            folderItem.ExcludeMask = folder.ExcludeMask;
            folderItem.DeleteIfEmpty = folder.DeleteIfEmpty;
        }

        public bool SaveTo(IErasureTarget target)
        {
            FolderErasureTarget folder = target as FolderErasureTarget;
            if (folder == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            if (folderItem.Path.Length == 0)
            {
                return false;
            }

            folder.Path = folderItem.Path;
            folder.IncludeMask = folderItem.IncludeMask;
            folder.ExcludeMask = folderItem.ExcludeMask;
            folder.DeleteIfEmpty = folderItem.DeleteIfEmpty;
            return true;
        }

        #endregion

        #region ICliConfigurer<ErasureTarget> Members

        public string Help()
        {
            return S._(@"dir                 Erases files and folders in the directory
  arguments: dir=<directory>[,-excludeMask][,+includeMask][,deleteIfEmpty[=true|false]]
    excludeMask     A wildcard expression for files and folders to
                    exclude.
    includeMask     A wildcard expression for files and folders to
                    include.
                    The include mask is applied before the exclude mask.
    deleteIfEmpty   Deletes the folder at the end of the erasure if it is
                    empty. If this parameter is not specified, it defaults
                    to true.");
        }

        public bool ProcessArgument(string argument)
        {
            //The directory target, taking a list of + and - wildcard expressions.
            Regex regex = new Regex("dir=(?<directoryName>.*)(?<directoryParams>(?<directoryExcludeMask>,-[^,]+)|(?<directoryIncludeMask>,\\+[^,]+)|(?<directoryDeleteIfEmpty>,deleteIfEmpty(=(?<directoryDeleteIfEmptyValue>true|false))?))*",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            Match match = regex.Match(argument);

            string[] trueValues = new string[] { "yes", "true" };
            if (match.Groups["directoryName"].Success)
            {
                folderItem.Path = match.Groups["directoryName"].Value;
                if (!match.Groups["directoryDeleteIfEmpty"].Success)
                    folderItem.DeleteIfEmpty = true;
                else if (!match.Groups["directoryDeleteIfEmptyValue"].Success)
                    folderItem.DeleteIfEmpty = true;
                else
                    folderItem.DeleteIfEmpty =
                        trueValues.Contains(match.Groups["directoryDeleteIfEmptyValue"].Value);

                if (match.Groups["directoryExcludeMask"].Success)
                    folderItem.ExcludeMask += match.Groups["directoryExcludeMask"].Value.Remove(0, 2) + ' ';
                if (match.Groups["directoryIncludeMask"].Success)
                    folderItem.IncludeMask += match.Groups["directoryIncludeMask"].Value.Remove(0, 2) + ' ';

                return true;
            }

            try
            {
                if (Directory.Exists(argument))
                {
                    folderItem.Path = argument;
                    folderItem.DeleteIfEmpty = false;
                    folderItem.IncludeMask = folderItem.ExcludeMask = string.Empty;
                    return true;
                }
            }
            catch (NotSupportedException)
            {
            }

            return false;
        }

        #endregion
    }
}
