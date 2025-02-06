using System.Linq;
using UnityEngine;

namespace Timberborn.TimberLisp
{
    public class MyLogger : CSLisp.Core.ILogger
    {
        string name;
        public MyLogger(string name)
        {
            this.name = name;
        }
        bool CSLisp.Core.ILogger.EnableParsingLogging => false;

        bool CSLisp.Core.ILogger.EnableInstructionLogging => false;

        bool CSLisp.Core.ILogger.EnableStackLogging => false;

        public void Log (params object[] args)
        {
            var strings = args.Select(obj => (obj == null) ? "null" : obj.ToString());
            var message = string.Join(" ", strings);
            Debug.Log("[" + name + "] " + message);
        }
    }}
