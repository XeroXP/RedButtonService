/* 
 * $Id: Settings.cs 2993 2021-09-25 17:23:27Z gtrant $
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
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Eraser.Plugins
{
	public class Settings
	{
		/// <summary>
		/// The default file erasure method. This is a GUID since methods are
		/// implemented through plugins and plugins may not be loaded and missing
		/// references may follow.
		/// </summary>
		public Guid DefaultFileErasureMethod
		{
			get
			{
				return new Guid("{1407FC4E-FEFF-4375-B4FB-D7EFBB7E9922}");
			}
		}

		/// <summary>
		/// The default drive erasure method. This is a GUID since methods are
		/// implemented through plugins and plugins may not be loaded and missing
		/// references may follow.
		/// </summary>
		public Guid DefaultDriveErasureMethod
		{
			get
			{
				return new Guid("{BF8BA267-231A-4085-9BF9-204DE65A6641}");
			}
		}

		/// <summary>
		/// The PRNG used. This is a GUID since PRNGs are implemented through
		/// plugins and plugins may not be loaded and missing references may follow.
		/// </summary>
		public Guid ActivePrng
		{
			get
			{
				return new Guid("{6BF35B8E-F37F-476e-B6B2-9994A92C3B0C}");
			}
		}

		/// <summary>
		/// Whether files which are locked when being erased should be forcibly
		/// unlocked for erasure.
		/// </summary>
		public bool ForceUnlockLockedFiles
		{
			get
			{
				return true;
			}
		}
	}
}
