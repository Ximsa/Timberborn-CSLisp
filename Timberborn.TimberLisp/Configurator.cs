using Bindito.Core;

namespace Timberborn.TimberLisp
{
    [Context("Game")]
    [Context("MapEditor")]
    [Context("MainMenu")]
    class TimberLispConfigurator : Configurator
    {

        public override void Configure () => Bind<REPL>().AsSingleton();
    }
}
