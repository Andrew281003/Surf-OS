using System;
using SurfOS.Boot;
using SurfOS.Runtime;
using Sys = Cosmos.Kernel.System;

namespace SurfOS
{
    /// <summary>Cosmos owns the hardware loop; SurfOS owns the runtime behind this boundary.</summary>
    public sealed class Kernel : Sys.Kernel
    {
        private SurfRuntime _runtime;

        protected override void BeforeRun()
        {
            try
            {
                _runtime = new BootCoordinator().Boot();
            }
            catch (Exception exception)
            {
                KernelPanic.Show("SurfOS could not finish booting.", exception);
            }
        }

        protected override void Run()
        {
            if (_runtime == null)
            {
                Sys.Power.Halt();
                return;
            }

            try
            {
                _runtime.Tick();
            }
            catch (Exception exception)
            {
                KernelPanic.Show("An unhandled kernel runtime error occurred.", exception);
            }
        }
    }
}
