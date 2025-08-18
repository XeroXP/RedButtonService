using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonService.Models
{
    public class EraseEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="task">The task being referred to by this event.</param>
        public EraseEventArgs(string note)
        {
            Note = note;
        }

        /// <summary>
        /// The executing task.
        /// </summary>
        public string Note { get; private set; }
    }
}
