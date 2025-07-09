using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Eraser.Manager.RIPEMD
{
    [System.Runtime.InteropServices.ComVisible(true)]
    public abstract class RIPEMD160 : HashAlgorithm
    {
        //
        // public constructors
        //

        protected RIPEMD160()
        {
            HashSizeValue = 160;
        }


    }
}
