
using Timberborn.TickSystem;

namespace Timberborn.TimberLisp
{
    class REPL : ITickableSingleton
    {
        public void Tick () => NREPLServerInstance.nreplServer.HandleMessages();
    }
}
