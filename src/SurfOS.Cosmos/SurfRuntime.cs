using SurfOS.Shell;

namespace SurfOS.Runtime
{
    public sealed class SurfRuntime
    {
        private readonly SurfShell _shell;

        public SurfRuntime(SurfShell shell)
        {
            _shell = shell;
        }

        public void Tick()
        {
            _shell.RunOneCommand();
        }
    }
}
