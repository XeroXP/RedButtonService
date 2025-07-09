using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedButtonService.Models
{
    public class ServiceSettings
    {
        public TelegramSettings Telegram { get; set; }
        public USBTriggerSettings USBTrigger { get; set; }
        public UserLogonTriggerSettings UserLogonTrigger { get; set; }
        public EraserSettings Eraser { get; set; }
    }
}
