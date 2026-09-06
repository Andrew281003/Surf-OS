using SurfOS.Logging;
using Sys = Cosmos.System;

namespace SurfOS.SystemServices
{
    public interface IPowerService
    {
        void Reboot();
        void Shutdown();
    }

    public sealed class PowerService : IPowerService
    {
        private readonly IKernelLogger _logger;

        public PowerService(IKernelLogger logger)
        {
            _logger = logger;
        }

        public void Reboot()
        {
            _logger.Info("Restart requested.");
            Sys.Power.Reboot();
        }

        public void Shutdown()
        {
            _logger.Info("Shutdown requested.");
            Sys.Power.Shutdown();
        }
    }
}
