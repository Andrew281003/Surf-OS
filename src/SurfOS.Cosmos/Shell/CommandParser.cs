using System;
using System.Collections.Generic;

namespace SurfOS.Shell
{
    public sealed class CommandParser
    {
        public ParsedCommand Parse(string input)
        {
            List<string> tokens = Tokenize(input ?? string.Empty);
            if (tokens.Count == 0)
            {
                return new ParsedCommand(string.Empty, new List<string>(), new List<string>());
            }

            List<string> arguments = new List<string>();
            List<string> flags = new List<string>();
            for (int index = 1; index < tokens.Count; index++)
            {
                if (tokens[index].StartsWith("--") || (tokens[index].StartsWith("-") && tokens[index].Length > 1))
                {
                    flags.Add(tokens[index].ToLower());
                }
                else
                {
                    arguments.Add(tokens[index]);
                }
            }
            return new ParsedCommand(tokens[0].ToLower(), arguments, flags);
        }

        private static List<string> Tokenize(string input)
        {
            List<string> tokens = new List<string>();
            string current = string.Empty;
            char quote = '\0';
            for (int index = 0; index < input.Length; index++)
            {
                char value = input[index];
                if (quote != '\0')
                {
                    if (value == quote) { quote = '\0'; }
                    else { current += value; }
                    continue;
                }
                if (value == '\'' || value == '"')
                {
                    quote = value;
                }
                else if (char.IsWhiteSpace(value))
                {
                    Add(tokens, ref current);
                }
                else
                {
                    current += value;
                }
            }
            if (quote != '\0') { throw new InvalidOperationException("Unclosed quote."); }
            Add(tokens, ref current);
            return tokens;
        }

        private static void Add(List<string> tokens, ref string current)
        {
            if (current.Length == 0) { return; }
            tokens.Add(current);
            current = string.Empty;
        }
    }
}
