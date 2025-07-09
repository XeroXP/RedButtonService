using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonService.Models
{
    public class EraserSettings
    {
        public List<EraseEntry> ToErase { get; set; }
        public int? MaxTasks { get; set; }
        public int? TimeStatusSendMinutes { get; set; }
    }
}
