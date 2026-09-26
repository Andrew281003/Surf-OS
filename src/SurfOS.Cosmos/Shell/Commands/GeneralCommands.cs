using System;
using SurfOS.Users;

namespace SurfOS.Shell.Commands
{
    public sealed class HelpCommand : ICommand
    {
        private readonly CommandRegistry _registry;
        public HelpCommand(CommandRegistry registry) { _registry = registry; }
        public string Name { get { return "help"; } }
        public string Usage { get { return "help [command]"; } }
        public string Description { get { return "List commands or show command help."; } }
        public void Execute(ParsedCommand command)
        {
            if (command.Arguments.Count > 0)
            {
                ICommand match = _registry.Find(command.Arguments[0].ToLower());
                if (match == null) { throw new InvalidOperationException("Unknown command."); }
                Console.WriteLine(match.Usage + " - " + match.Description);
                return;
            }
            Console.WriteLine("SurfOS commands:");
            for (int index = 0; index < _registry.Commands.Count; index++)
            {
                ICommand item = _registry.Commands[index];
                Console.WriteLine("  " + item.Name.PadRight(10) + item.Description);
            }
            Console.WriteLine("Protected operations require administrator rights, --force, and password authentication.");
        }
    }

    public sealed class ClearCommand : ICommand
    {
        public string Name { get { return "clear"; } }
        public string Usage { get { return "clear"; } }
        public string Description { get { return "Clear the text display."; } }
        public void Execute(ParsedCommand command) { Console.Clear(); }
    }

    public sealed class InfoCommand : ICommand
    {
        private readonly CommandContext _context;
        public InfoCommand(CommandContext context) { _context = context; }
        public string Name { get { return "info"; } }
        public string Usage { get { return "info"; } }
        public string Description { get { return "Show kernel and system information."; } }
        public void Execute(ParsedCommand command)
        {
            Console.WriteLine("SurfOS Kernel 0.1 (Cosmos)");
            Console.WriteLine("Device: " + _context.Configuration.DeviceName);
            Console.WriteLine("Filesystem: " + _context.FileSystem.DriveRoot);
            Console.WriteLine("Language: " + _context.Configuration.Language);
            Console.WriteLine("Environment: terminal milestone");
        }
    }

    public sealed class WhoAmICommand : ICommand
    {
        private readonly CommandContext _context;
        public WhoAmICommand(CommandContext context) { _context = context; }
        public string Name { get { return "whoami"; } }
        public string Usage { get { return "whoami"; } }
        public string Description { get { return "Show the current account and role."; } }
        public void Execute(ParsedCommand command)
        {
            UserAccount account = _context.Sessions.CurrentUser;
            AvatarRenderer.Draw(account.AvatarId, AvatarSize.Large, account.ProfileColor, 0, false);
            Console.WriteLine("Username: " + account.Username);
            Console.WriteLine("Role: " + (account.IsAdministrator ? "administrator" : "user"));
            Console.WriteLine("ID: " + account.Id);
            Console.WriteLine("Home: " + account.HomeDirectory);
        }
    }

    public sealed class ProfileCommand : ICommand
    {
        private readonly CommandContext _context;
        public ProfileCommand(CommandContext context) { _context = context; }
        public string Name { get { return "profile"; } }
        public string Usage { get { return "profile"; } }
        public string Description { get { return "Choose your avatar and profile color."; } }
        public void Execute(ParsedCommand command)
        {
            UserAccount account = _context.Sessions.CurrentUser;
            string id;
            ConsoleColor color;
            if (AvatarSelectionConsole.Choose(account.AvatarId, account.ProfileColor, out id, out color))
            {
                _context.Users.SetProfile(account, id, color);
                Console.WriteLine("Profile saved.");
            }
        }
    }
}
