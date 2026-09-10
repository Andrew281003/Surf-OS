using System;

namespace SurfOS.Shell.Commands
{
    internal static class CommandHelpers
    {
        public static string RequireArgument(ParsedCommand command, int index, string usage)
        {
            if (command.Arguments.Count <= index)
            {
                throw new InvalidOperationException("Usage: " + usage);
            }
            return command.Arguments[index];
        }

        public static void AuthorizeWrite(CommandContext context, ParsedCommand command, string path, string operation)
        {
            if (context.FileSystem.IsProtectedPath(path))
            {
                context.Permissions.AuthorizeProtectedOperation(command.HasFlag("--force"), operation);
            }
        }
    }
}
