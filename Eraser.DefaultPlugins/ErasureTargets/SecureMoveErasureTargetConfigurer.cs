/* 
 * $Id: SecureMoveErasureTargetConfigurer.cs 2993 2021-09-25 17:23:27Z gtrant $
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
using System;
using System.Text.RegularExpressions;

namespace Eraser.DefaultPlugins
{
	partial class SecureMoveErasureTargetConfigurer : IErasureTargetConfigurer
	{
        string fromPath = string.Empty;
        string toPath = string.Empty;

        #region IConfigurer<ErasureTarget> Members

        public void LoadFrom(IErasureTarget target)
        {
            SecureMoveErasureTarget secureMove = target as SecureMoveErasureTarget;
            if (secureMove == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            fromPath = secureMove.Path;
            toPath = secureMove.Destination;
        }

        public bool SaveTo(IErasureTarget target)
        {
            SecureMoveErasureTarget secureMove = target as SecureMoveErasureTarget;
            if (secureMove == null)
                throw new ArgumentException("The provided erasure target type is not " +
                    "supported by this configurer.");

            secureMove.Path = fromPath;
            secureMove.Destination = toPath;
            return true;
        }

        #endregion

        #region ICliConfigurer<ErasureTarget> Members

        public string Help()
        {
            return S._(@"move                Securely moves a file/directory to a new location
  arguments: move=<source>|<destination>");
        }

        public bool ProcessArgument(string argument)
        {
            //The secure move source and target, which are separated by a pipe.
            Regex regex = new Regex("^(?:move=)?(?<source>.*)\\|(?<target>.*)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = regex.Match(argument);

            if (match.Groups["source"].Success && match.Groups["target"].Success)
            {
                //Get the source and destination paths
                fromPath = match.Groups["source"].Value;
                toPath = match.Groups["target"].Value;
                return true;
            }

            return false;
        }

        #endregion
    }
}
