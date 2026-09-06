using SurfOS.Config;
using SurfOS.Logging;
using SurfOS.Security;
using SurfOS.Storage;
using SurfOS.SystemServices;
using SurfOS.Users;

namespace SurfOS.Shell
{
    public sealed class CommandContext
    {
        public CommandContext(
            IFileSystemService fileSystem,
            IKernelLogger logger,
            SystemConfiguration configuration,
            UserService users,
            SessionService sessions,
            AuthenticationConsole authentication,
            PermissionManager permissions,
            IPowerService power)
        {
            FileSystem = fileSystem;
            Logger = logger;
            Configuration = configuration;
            Users = users;
            Sessions = sessions;
            Authentication = authentication;
            Permissions = permissions;
            Power = power;
        }

        public IFileSystemService FileSystem { get; private set; }
        public IKernelLogger Logger { get; private set; }
        public SystemConfiguration Configuration { get; private set; }
        public UserService Users { get; private set; }
        public SessionService Sessions { get; private set; }
        public AuthenticationConsole Authentication { get; private set; }
        public PermissionManager Permissions { get; private set; }
        public IPowerService Power { get; private set; }
    }
}
