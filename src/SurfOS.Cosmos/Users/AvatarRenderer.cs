using System;

namespace SurfOS.Users
{
    public static class ProfileColors
    {
        public static readonly ConsoleColor[] Choices = { ConsoleColor.DarkBlue, ConsoleColor.DarkRed, ConsoleColor.DarkGreen, ConsoleColor.DarkMagenta, ConsoleColor.DarkCyan, ConsoleColor.Blue, ConsoleColor.Red, ConsoleColor.Magenta };
        public const ConsoleColor Default = ConsoleColor.DarkCyan;
        public static ConsoleColor Normalize(string value)
        {
            ConsoleColor parsed;
            if (Enum.TryParse(value, true, out parsed))
                for (int i = 0; i < Choices.Length; i++) if (Choices[i] == parsed) return parsed;
            return Default;
        }
        public static ConsoleColor Display(ConsoleColor selected)
        {
            // Use the bright counterpart when the terminal uses a dark background.
            if (Console.BackgroundColor == ConsoleColor.Black || Console.BackgroundColor == ConsoleColor.DarkBlue)
            {
                switch (selected)
                {
                    case ConsoleColor.DarkBlue: return ConsoleColor.Blue;
                    case ConsoleColor.DarkRed: return ConsoleColor.Red;
                    case ConsoleColor.DarkGreen: return ConsoleColor.Green;
                    case ConsoleColor.DarkMagenta: return ConsoleColor.Magenta;
                    case ConsoleColor.DarkCyan: return ConsoleColor.Cyan;
                }
            }
            return selected;
        }
    }

    public static class AvatarRenderer
    {
        public static string[] Layout(string id, AvatarSize size, int width, int left, bool center)
        {
            string[] source = AvatarLibrary.Get(id).Lines(size);
            string[] result = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                int x = center ? Math.Max(0, (width - source[i].Length) / 2) : Math.Max(0, left);
                result[i] = width <= x ? string.Empty : new string(' ', x) + source[i].Substring(0, Math.Min(source[i].Length, width - x));
            }
            return result;
        }

        public static void Draw(string id, AvatarSize size, ConsoleColor color, int left, bool center)
        {
            int width = Math.Max(1, Console.WindowWidth - 1);
            string[] lines = Layout(id, size, width, left, center);
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ProfileColors.Display(color);
            for (int i = 0; i < lines.Length; i++)
            {
                if (Console.CursorTop >= Console.WindowHeight - 1) break;
                Console.WriteLine(lines[i]);
            }
            Console.ForegroundColor = previous;
        }
    }

    public static class MenuNavigation
    {
        public static int Move(int current, int count, ConsoleKey key)
        {
            if (count < 1) return 0;
            if (key == ConsoleKey.DownArrow || key == ConsoleKey.RightArrow) return (current + 1) % count;
            if (key == ConsoleKey.UpArrow || key == ConsoleKey.LeftArrow) return (current + count - 1) % count;
            return current;
        }
        public static int FirstVisible(int selected, int first, int visible)
        {
            if (selected < first) return selected;
            if (selected >= first + visible) return selected - visible + 1;
            return first;
        }
    }
}
