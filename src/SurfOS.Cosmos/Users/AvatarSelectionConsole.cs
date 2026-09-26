using System;

namespace SurfOS.Users
{
    public static class AvatarSelectionConsole
    {
        public static bool Choose(string currentId, ConsoleColor currentColor, out string avatarId, out ConsoleColor color)
        {
            avatarId = currentId;
            color = currentColor;
            int index = 0;
            for (int i = 0; i < AvatarLibrary.All.Count; i++) if (AvatarLibrary.All[i].Id == currentId) index = i;
            int colorIndex = 0;
            for (int i = 0; i < ProfileColors.Choices.Length; i++) if (ProfileColors.Choices[i] == currentColor) colorIndex = i;
            AvatarSize size = AvatarSize.Medium;
            int first = 0;
            while (true)
            {
                Console.Clear();
                Console.WriteLine("Choose avatar  (arrows: browse, Tab: size, C: color, Enter: save, Esc: cancel)");
                int rows = Math.Max(1, Console.WindowHeight - 15);
                first = MenuNavigation.FirstVisible(index, first, rows);
                for (int i = first; i < Math.Min(AvatarLibrary.All.Count, first + rows); i++)
                {
                    AvatarDefinition item = AvatarLibrary.All[i];
                    Console.ForegroundColor = i == index ? ProfileColors.Display(ProfileColors.Choices[colorIndex]) : ConsoleColor.Gray;
                    Console.WriteLine((i == index ? "> " : "  ") + item.Name);
                }
                Console.ResetColor();
                Console.WriteLine("Avatar " + (index + 1) + "/" + AvatarLibrary.All.Count + "  Size: " + size + "  Color: " + ProfileColors.Choices[colorIndex]);
                AvatarRenderer.Draw(AvatarLibrary.All[index].Id, size, ProfileColors.Choices[colorIndex], 0, false);
                ConsoleKey key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Escape) return false;
                if (key == ConsoleKey.Enter)
                {
                    avatarId = AvatarLibrary.All[index].Id;
                    color = ProfileColors.Choices[colorIndex];
                    return true;
                }
                if (key == ConsoleKey.Tab) size = (AvatarSize)(((int)size + 1) % 3);
                else if (key == ConsoleKey.C) colorIndex = (colorIndex + 1) % ProfileColors.Choices.Length;
                else index = MenuNavigation.Move(index, AvatarLibrary.All.Count, key);
            }
        }
    }
}
