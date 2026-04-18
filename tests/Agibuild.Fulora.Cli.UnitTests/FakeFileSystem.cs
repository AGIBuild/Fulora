using System.Text.RegularExpressions;
using Agibuild.Fulora.Cli.Commands;

namespace Agibuild.Fulora.UnitTests;

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _binaryFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public void AddDirectory(string path)
    {
        var normalized = Normalize(path);
        while (!string.IsNullOrEmpty(normalized))
        {
            _directories.Add(normalized);
            var parent = Path.GetDirectoryName(normalized);
            if (string.IsNullOrEmpty(parent) || parent == normalized)
                break;
            normalized = parent;
        }
    }

    public void AddFile(string path, string content)
    {
        var normalized = Normalize(path);
        var directory = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(directory))
            AddDirectory(directory);
        _files[normalized] = content;
        _binaryFiles[normalized] = System.Text.Encoding.UTF8.GetBytes(content);
    }

    public void AddBinaryFile(string path, byte[] content)
    {
        var normalized = Normalize(path);
        var directory = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(directory))
            AddDirectory(directory);
        _binaryFiles[normalized] = content;
        _files.Remove(normalized);
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path) => _files[Normalize(path)];

    public byte[] ReadAllBytes(string path) => _binaryFiles[Normalize(path)];

    public void WriteAllText(string path, string content) => AddFile(path, content);

    public void CreateDirectory(string path) => AddDirectory(path);

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = Normalize(path);
        if (recursive)
        {
            foreach (var file in _files.Keys.Where(k => k.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.Ordinal) || k == normalized).ToArray())
                _files.Remove(file);
            foreach (var file in _binaryFiles.Keys.Where(k => k.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.Ordinal) || k == normalized).ToArray())
                _binaryFiles.Remove(file);
            foreach (var dir in _directories.Where(d => d.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.Ordinal) || d == normalized).ToArray())
                _directories.Remove(dir);
        }
        else
        {
            _directories.Remove(normalized);
        }
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        var source = Normalize(sourcePath);
        var destination = Normalize(destinationPath);
        if (!overwrite && _files.ContainsKey(destination))
            throw new IOException($"File already exists: {destination}");
        if (_files.TryGetValue(source, out var text))
            AddFile(destination, text);
        else
            AddBinaryFile(destination, _binaryFiles[source]);
    }

    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var directory = Normalize(path);
        var pattern = "^" + Regex.Escape(searchPattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);

        return _files.Keys
            .Where(file =>
            {
                var fileDirectory = Path.GetDirectoryName(file) ?? string.Empty;
                var inScope = searchOption == SearchOption.AllDirectories
                    ? file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || fileDirectory == directory
                    : fileDirectory == directory;
                return inScope && regex.IsMatch(Path.GetFileName(file));
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    public string[] GetDirectories(string path)
    {
        var directory = Normalize(path);
        return _directories
            .Where(candidate => Path.GetDirectoryName(candidate) == directory)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string path) => Path.GetFullPath(path);
}
