using System;

namespace SurfOS.Shell.Commands
{
    public sealed class LoginCommand : ICommand
    {
        private readonly CommandContext _context;
        public LoginCommand(CommandContext context) { _context = context; }
        public string Name { get { return "login"; } }
        public string Usage { get { return "login"; } }
        public string Description { get { return "Authenticate as another user."; } }
        public void Execute(ParsedCommand command)
        {
            _context.Sessions.Logout();
            _context.Authentication.LoginUntilSuccessful();
            _context.FileSystem.SetCurrentDirectory(_context.Sessions.CurrentUser.HomeDirectory);
        }
    }

    public sealed class LogoutCommand : ICommand
    {
        private readonly CommandContext _context;
        public LogoutCommand(CommandContext context) { _context = context; }
        public string Name { get { return "logout"; } }
        public string Usage { get { return "logout"; } }
        public string Description { get { return "End the current session."; } }
        public void Execute(ParsedCommand command) { _context.Sessions.Logout(); Console.Clear(); }
    }

    public sealed class RebootCommand : ICommand
    {
        private readonly CommandContext _context;
        public RebootCommand(CommandContext context) { _context = context; }
        public string Name { get { return "reboot"; } }
        public string Usage { get { return "reboot --force"; } }
        public string Description { get { return "Authenticate and restart the machine."; } }
        public void Execute(ParsedCommand command)
        {
            _context.Permissions.AuthorizeProtectedOperation(command.HasFlag("--force"), "restarting the machine");
            _context.Power.Reboot();
        }
    }

    public sealed class ShutdownCommand : ICommand
    {
        private readonly CommandContext _context;
        public ShutdownCommand(CommandContext context) { _context = context; }
        public string Name { get { return "shutdown"; } }
        public string Usage { get { return "shutdown --force"; } }
        public string Description { get { return "Authenticate and power off the machine."; } }
        public void Execute(ParsedCommand command)
        {
            _context.Permissions.AuthorizeProtectedOperation(command.HasFlag("--force"), "shutting down the machine");
            _context.Power.Shutdown();
        }
    }
}
