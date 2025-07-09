using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonService.Models
{
    public class TGMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="task">The task being referred to by this event.</param>
        public TGMessageEventArgs(string message)
        {
            Message = message;
        }

        /// <summary>
        /// The executing task.
        /// </summary>
        public string Message { get; private set; }
    }
}
