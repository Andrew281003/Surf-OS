using System.Collections.Generic;

namespace SurfOS.Shell
{
    public sealed class ParsedCommand
    {
        public ParsedCommand(string name, List<string> arguments, List<string> flags)
        {
            Name = name;
            Arguments = arguments;
            Flags = flags;
        }

        public string Name { get; private set; }
        public List<string> Arguments { get; private set; }
        public List<string> Flags { get; private set; }

        public bool HasFlag(string flag)
        {
            for (int index = 0; index < Flags.Count; index++)
            {
                if (Flags[index] == flag) { return true; }
            }
            return false;
        }
    }
}
