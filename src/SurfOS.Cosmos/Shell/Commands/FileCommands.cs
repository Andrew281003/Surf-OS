using System;
using System.Collections.Generic;
using SurfOS.Storage;

namespace SurfOS.Shell.Commands
{
    public sealed class PwdCommand : ICommand
    {
        private readonly CommandContext _context;
        public PwdCommand(CommandContext context) { _context = context; }
        public string Name { get { return "pwd"; } }
        public string Usage { get { return "pwd"; } }
        public string Description { get { return "Print the current directory."; } }
        public void Execute(ParsedCommand command) { Console.WriteLine(_context.FileSystem.ToDisplayPath(_context.FileSystem.CurrentDirectory)); }
    }

    public sealed class ListCommand : ICommand
    {
        private readonly CommandContext _context;
        public ListCommand(CommandContext context) { _context = context; }
        public string Name { get { return "ls"; } }
        public string Usage { get { return "ls [path]"; } }
        public string Description { get { return "List a directory."; } }
        public void Execute(ParsedCommand command)
        {
            string path = command.Arguments.Count == 0 ? "." : command.Arguments[0];
            List<FileSystemEntry> entries = _context.FileSystem.List(path);
            for (int index = 0; index < entries.Count; index++)
            {
                FileSystemEntry entry = entries[index];
                Console.WriteLine((entry.IsDirectory ? "<DIR>  " : "       ") + entry.Name +
                                  (entry.IsDirectory ? string.Empty : "  " + entry.Size + " bytes"));
            }
        }
    }

    public sealed class ChangeDirectoryCommand : ICommand
    {
        private readonly CommandContext _context;
        public ChangeDirectoryCommand(CommandContext context) { _context = context; }
        public string Name { get { return "cd"; } }
        public string Usage { get { return "cd <path>"; } }
        public string Description { get { return "Change the current directory."; } }
        public void Execute(ParsedCommand command) { _context.FileSystem.SetCurrentDirectory(CommandHelpers.RequireArgument(command, 0, Usage)); }
    }

    public sealed class MakeDirectoryCommand : ICommand
    {
        private readonly CommandContext _context;
        public MakeDirectoryCommand(CommandContext context) { _context = context; }
        public string Name { get { return "mkdir"; } }
        public string Usage { get { return "mkdir <path> [--force]"; } }
        public string Description { get { return "Create a directory."; } }
        public void Execute(ParsedCommand command)
        {
            string path = CommandHelpers.RequireArgument(command, 0, Usage);
            CommandHelpers.AuthorizeWrite(_context, command, path, "creating a protected directory");
            _context.FileSystem.CreateDirectory(path);
        }
    }

    public sealed class TouchCommand : ICommand
    {
        private readonly CommandContext _context;
        public TouchCommand(CommandContext context) { _context = context; }
        public string Name { get { return "touch"; } }
        public string Usage { get { return "touch <path> [--force]"; } }
        public string Description { get { return "Create an empty file."; } }
        public void Execute(ParsedCommand command)
        {
            string path = CommandHelpers.RequireArgument(command, 0, Usage);
            CommandHelpers.AuthorizeWrite(_context, command, path, "writing a protected file");
            _context.FileSystem.CreateFile(path);
        }
    }

    public sealed class CatCommand : ICommand
    {
        private readonly CommandContext _context;
        public CatCommand(CommandContext context) { _context = context; }
        public string Name { get { return "cat"; } }
        public string Usage { get { return "cat <file>"; } }
        public string Description { get { return "Print a text file."; } }
        public void Execute(ParsedCommand command) { Console.WriteLine(_context.FileSystem.ReadAllText(CommandHelpers.RequireArgument(command, 0, Usage))); }
    }

    public sealed class EchoCommand : ICommand
    {
        private readonly CommandContext _context;
        public EchoCommand(CommandContext context) { _context = context; }
        public string Name { get { return "echo"; } }
        public string Usage { get { return "echo <text> [> <file>] [--force]"; } }
        public string Description { get { return "Print text or write it to a file."; } }
        public void Execute(ParsedCommand command)
        {
            if (command.Arguments.Count == 0) { Console.WriteLine(); return; }
            int redirect = -1;
            for (int index = 0; index < command.Arguments.Count; index++)
            {
                if (command.Arguments[index] == ">") { redirect = index; break; }
            }
            string text = Join(command.Arguments, redirect < 0 ? command.Arguments.Count : redirect);
            if (redirect < 0) { Console.WriteLine(text); return; }
            if (redirect + 1 >= command.Arguments.Count) { throw new InvalidOperationException("A destination file is required after >."); }
            string path = command.Arguments[redirect + 1];
            CommandHelpers.AuthorizeWrite(_context, command, path, "writing a protected file");
            _context.FileSystem.WriteAllText(path, text + "\r\n");
        }

        private static string Join(List<string> values, int count)
        {
            string result = string.Empty;
            for (int index = 0; index < count; index++)
            {
                if (index > 0) { result += " "; }
                result += values[index];
            }
            return result;
        }
    }

    public sealed class CopyCommand : ICommand
    {
        private readonly CommandContext _context;
        public CopyCommand(CommandContext context) { _context = context; }
        public string Name { get { return "cp"; } }
        public string Usage { get { return "cp <source> <destination> [--force]"; } }
        public string Description { get { return "Copy a file."; } }
        public void Execute(ParsedCommand command)
        {
            string source = CommandHelpers.RequireArgument(command, 0, Usage);
            string destination = CommandHelpers.RequireArgument(command, 1, Usage);
            CommandHelpers.AuthorizeWrite(_context, command, destination, "copying to a protected path");
            _context.FileSystem.Copy(source, destination, command.HasFlag("--force"));
        }
    }

    public sealed class MoveCommand : ICommand
    {
        private readonly CommandContext _context;
        public MoveCommand(CommandContext context) { _context = context; }
        public string Name { get { return "mv"; } }
        public string Usage { get { return "mv <source> <destination> [--force]"; } }
        public string Description { get { return "Move a file or directory."; } }
        public void Execute(ParsedCommand command)
        {
            string source = CommandHelpers.RequireArgument(command, 0, Usage);
            string destination = CommandHelpers.RequireArgument(command, 1, Usage);
            if (_context.FileSystem.IsProtectedPath(source) || _context.FileSystem.IsProtectedPath(destination))
            {
                _context.Permissions.AuthorizeProtectedOperation(command.HasFlag("--force"), "moving protected data");
            }
            _context.FileSystem.Move(source, destination, command.HasFlag("--force"));
        }
    }

    public sealed class RemoveCommand : ICommand
    {
        private readonly CommandContext _context;
        public RemoveCommand(CommandContext context) { _context = context; }
        public string Name { get { return "rm"; } }
        public string Usage { get { return "rm <path> [-r] [--force]"; } }
        public string Description { get { return "Remove a file or directory."; } }
        public void Execute(ParsedCommand command)
        {
            string path = CommandHelpers.RequireArgument(command, 0, Usage);
            if (_context.FileSystem.ToDisplayPath(path) == "/")
            {
                throw new InvalidOperationException("The mounted root cannot be removed.");
            }
            CommandHelpers.AuthorizeWrite(_context, command, path, "removing protected data");
            _context.FileSystem.Delete(path, command.HasFlag("-r") || command.HasFlag("--recursive"));
        }
    }
}
