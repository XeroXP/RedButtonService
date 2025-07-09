using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonService.Models
{
    public class EraseBlockEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="task">The task being referred to by this event.</param>
        public EraseBlockEventArgs(bool block)
        {
            Block = block;
        }

        /// <summary>
        /// The executing task.
        /// </summary>
        public bool Block { get; private set; }
    }
}
