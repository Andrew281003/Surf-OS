using System;
using System.Collections.Generic;

namespace SurfOS.Shell
{
    public sealed class CommandRegistry
    {
        private readonly List<ICommand> _commands = new List<ICommand>();

        public List<ICommand> Commands { get { return _commands; } }

        public void Register(ICommand command)
        {
            if (Find(command.Name) != null)
            {
                throw new InvalidOperationException("Duplicate command: " + command.Name);
            }
            _commands.Add(command);
        }

        public ICommand Find(string name)
        {
            for (int index = 0; index < _commands.Count; index++)
            {
                if (_commands[index].Name == name) { return _commands[index]; }
            }
            return null;
        }
    }
}
