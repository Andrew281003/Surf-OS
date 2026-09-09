using System;

namespace SurfOS.Shell
{
    public sealed class SurfShell
    {
        private readonly CommandContext _context;
        private readonly CommandRegistry _commands;
        private readonly CommandParser _parser;

        public SurfShell(CommandContext context, CommandRegistry commands, CommandParser parser)
        {
            _context = context;
            _commands = commands;
            _parser = parser;
        }

        public void RunOneCommand()
        {
            if (!_context.Sessions.IsAuthenticated)
            {
                _context.Authentication.LoginUntilSuccessful();
                _context.FileSystem.SetCurrentDirectory(_context.Sessions.CurrentUser.HomeDirectory);
                Console.WriteLine("Welcome back to SurfOS.");
            }

            string path = _context.FileSystem.ToDisplayPath(_context.FileSystem.CurrentDirectory);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(_context.Sessions.CurrentUser.Username + "@" + _context.Configuration.DeviceName + ":" + path + "> ");
            Console.ResetColor();
            string input = Console.ReadLine();
            try
            {
                ParsedCommand parsed = _parser.Parse(input);
                if (parsed.Name.Length == 0) { return; }
                ICommand command = _commands.Find(parsed.Name);
                if (command == null)
                {
                    Console.WriteLine("Command not found: " + parsed.Name + ". Run 'help'.");
                    return;
                }
                command.Execute(parsed);
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: " + exception.Message);
                Console.ResetColor();
                _context.Logger.Warning("Shell command failed: " + exception.Message);
            }
        }
    }
}
