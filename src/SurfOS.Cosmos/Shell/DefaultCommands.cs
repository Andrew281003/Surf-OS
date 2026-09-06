using SurfOS.Shell.Commands;

namespace SurfOS.Shell
{
    public static class DefaultCommands
    {
        public static CommandRegistry Create(CommandContext context)
        {
            CommandRegistry registry = new CommandRegistry();
            registry.Register(new HelpCommand(registry));
            registry.Register(new PwdCommand(context));
            registry.Register(new ListCommand(context));
            registry.Register(new ChangeDirectoryCommand(context));
            registry.Register(new MakeDirectoryCommand(context));
            registry.Register(new TouchCommand(context));
            registry.Register(new CatCommand(context));
            registry.Register(new EchoCommand(context));
            registry.Register(new CopyCommand(context));
            registry.Register(new MoveCommand(context));
            registry.Register(new RemoveCommand(context));
            registry.Register(new ClearCommand());
            registry.Register(new InfoCommand(context));
            registry.Register(new WhoAmICommand(context));
            registry.Register(new LoginCommand(context));
            registry.Register(new LogoutCommand(context));
            registry.Register(new RebootCommand(context));
            registry.Register(new ShutdownCommand(context));
            return registry;
        }
    }
}
