using Timberborn.ModManagerScene;
using System.Threading;

namespace Timberborn.TimberLisp
{
    public class Plugin : IModStarter
    {
        public void StartMod()
        {
            var server = new NreplServer();
            Thread t = new Thread(server.Initialize);
            t.IsBackground = true;
            t.Start();
        }
    }
}
