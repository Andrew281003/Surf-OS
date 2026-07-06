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
        File.WriteAllText(path, JsonSerializer.Serialize(value, SerializerOptions));
    }
}
