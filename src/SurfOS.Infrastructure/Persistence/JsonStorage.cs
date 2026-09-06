using System.Text.Json;

namespace SurfOS2;

internal static class JsonStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static T? Read<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions);
    }

    public static void Write<T>(string path, T value)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("A JSON storage path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, SerializerOptions));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
