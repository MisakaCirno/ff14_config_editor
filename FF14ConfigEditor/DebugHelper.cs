using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF14ConfigEditor
{
    public static class DebugHelper
    {
        public static bool LogToConsole { get; set; } = false;
        public static bool LogToDebug { get; set; } = false;

        public static void Log(string message)
        {
            if (LogToConsole)
            {
                Console.WriteLine(message);
            }

            if (LogToDebug)
            {
                Debug.WriteLine(message);
            }
        }
    }
}
