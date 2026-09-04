using System.Text;

namespace GrouchyFiler.Services;

public sealed class FileLogger(string? path, string minimumLevel, long maximumBytes = 10485760, int backupCount = 3)
{
    private static readonly object gate = new();
    private static int Rank(string level) => level switch
    {
        "debug" => 0, "info" => 1, "warning" => 2, "error" => 3, "none" => 4,
        _ => throw new ArgumentException("Unknown log level.", nameof(level))
    };
    public void Write(string message, string level = "info")
    {
        if (string.IsNullOrWhiteSpace(path) || Rank(level) < Rank(minimumLevel)) return;
        if (maximumBytes < 4096 || maximumBytes > 1073741824 || backupCount < 0 || backupCount > 10)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Invalid log retention limits.");
        string line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length > maximumBytes)
        {
            bytes = new byte[(int)maximumBytes];
            Encoding.UTF8.GetEncoder().Convert(line.AsSpan(), bytes.AsSpan(0, bytes.Length - 2), true, out _, out int used, out _);
            bytes[used++] = 13; bytes[used++] = 10;
            Array.Resize(ref bytes, used);
        }
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > maximumBytes)
            {
                if (backupCount == 0) File.WriteAllBytes(path, []);
                else
                {
                    for (int i = backupCount - 1; i >= 1; i--)
                        if (File.Exists(path + "." + i)) File.Move(path + "." + i, path + "." + (i + 1), overwrite: true);
                    File.Move(path, path + ".1", overwrite: true);
                }
            }
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }
}
