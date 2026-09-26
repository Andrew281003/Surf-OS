using System;
using SurfOS.Config;
using SurfOS.Installer;
using SurfOS.Logging;
using SurfOS.Runtime;
using SurfOS.Security;
using SurfOS.Shell;
using SurfOS.Storage;
using SurfOS.SystemServices;
using SurfOS.Users;

namespace SurfOS.Boot
{
    public sealed class BootCoordinator
    {
        public SurfRuntime Boot()
        {
            BufferedKernelLogger logger = new BufferedKernelLogger();
            BootReporter reporter = new BootReporter(logger);
            reporter.Heading();
            reporter.Ok("Initializing kernel");
            reporter.Ok("Keyboard and text display initialized by Cosmos");

            CosmosFileSystem fileSystem = new CosmosFileSystem();
            if (!fileSystem.Initialize())
            {
                reporter.Fail("Storage initialization failed: " + fileSystem.LastError);
                throw new InvalidOperationException(fileSystem.LastError);
            }

            reporter.Ok("Storage initialized");
            new StorageInstaller(fileSystem).EnsureMountedVolume();
            reporter.Ok("Filesystem mounted at " + fileSystem.DriveRoot);
            fileSystem.EnsureSystemLayout();
            logger.Attach(fileSystem, "/Logs/kernel.log");
            reporter.Ok("Kernel logger started");

            ConfigurationService configuration = new ConfigurationService(fileSystem, logger);
            UserService users = new UserService(fileSystem, new PasswordHasher(), logger);
            FirstBootInstaller installer = new FirstBootInstaller(fileSystem, configuration, users);
            if (!configuration.IsInstalled())
            {
                installer.Run();
            }

            SystemConfiguration settings = configuration.Load();
            reporter.Ok("System configuration loaded");
            users.Load();
            reporter.Ok("User services initialized");

            SessionService sessions = new SessionService(users);
            AuthenticationConsole authentication = new AuthenticationConsole(sessions, users);
            authentication.LoginUntilSuccessful();

            PermissionManager permissions = new PermissionManager(sessions, authentication);
            PowerService power = new PowerService(logger);
            CommandContext context = new CommandContext(
                fileSystem,
                logger,
                settings,
                users,
                sessions,
                authentication,
                permissions,
                power);
            CommandRegistry commands = DefaultCommands.Create(context);
            SurfShell shell = new SurfShell(context, commands, new CommandParser());

            reporter.Ok("SurfOS ready");
            Console.WriteLine();
            Console.WriteLine("Welcome to SurfOS");
            Console.WriteLine();
            return new SurfRuntime(shell);
        }
    }
}
