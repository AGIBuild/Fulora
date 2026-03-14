using System.CommandLine;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agibuild.Fulora.Cli.Commands;

internal static class SearchCommand
{
    private const string NuGetSearchBase = "https://azuresearch-usnc.nuget.org/query";
    private const int DefaultTake = 20;

    public static Command Create()
    {
        var queryArg = new Argument<string?>("query")
        {
            Description = "Search query (optional; searches Tags:fulora-plugin by default)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var takeOpt = new Option<int>("--take") { Description = "Maximum number of results to return" };

        var command = new Command("search") { Description = "Search NuGet.org for Fulora plugins" };
        command.Arguments.Add(queryArg);
        command.Options.Add(takeOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArg);
            var take = parseResult.GetValue(takeOpt);
            if (take <= 0) take = DefaultTake;
            return await ExecuteAsync(query, take, ct);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(string? query, int take, CancellationToken ct, HttpClient? httpClient = null)
    {
        var searchTerm = string.IsNullOrWhiteSpace(query)
            ? "Tags:fulora-plugin"
            : $"Tags:fulora-plugin+{query.Trim()}";
        var url = $"{NuGetSearchBase}?q={Uri.EscapeDataString(searchTerm)}&take={take}";

        var useOwnClient = httpClient is null;
        var http = httpClient ?? new HttpClient();
        if (useOwnClient)
            http.DefaultRequestHeaders.Add("User-Agent", "Agibuild.Fulora.Cli/1.0");

        try
        {
            NuGetSearchResponse? response;
            try
            {
                response = await http.GetFromJsonAsync<NuGetSearchResponse>(url, ct);
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Failed to query NuGet: {ex.Message}");
                return 1;
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Failed to parse NuGet response: {ex.Message}");
                return 1;
            }

            if (response?.Data is null || response.Data.Count == 0)
            {
                Console.WriteLine("No plugins found.");
                return 0;
            }

            PrintTable(response.Data);
            return 0;
        }
        finally
        {
            if (useOwnClient)
                http.Dispose();
        }
    }

    private static void PrintTable(IReadOnlyList<NuGetPackageInfo> packages)
    {
        var idWidth = Math.Max(4, packages.Max(p => p.Id?.Length ?? 0));
        var versionWidth = Math.Max(7, packages.Max(p => p.Version?.Length ?? 0));
        idWidth = Math.Min(idWidth, 50);
        versionWidth = Math.Min(versionWidth, 20);

        var header = string.Format(CultureInfo.InvariantCulture, "{0,-" + idWidth + "} {1,-" + versionWidth + "} Description", "Package", "Version");
        Console.WriteLine(header);
        Console.WriteLine(new string('-', Math.Min(header.Length, 120)));

        foreach (var pkg in packages)
        {
            var id = Truncate(pkg.Id ?? "(unknown)", idWidth);
            var ver = Truncate(pkg.Version ?? "", versionWidth);
            var desc = Truncate(pkg.Description ?? "", 60);
            var tags = pkg.Tags?.Length > 0 ? $" [{string.Join(", ", pkg.Tags.Take(3))}]" : "";
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-" + idWidth + "} {1,-" + versionWidth + "} {2}{3}", id, ver, desc, tags));
        }
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";

    private sealed class NuGetSearchResponse
    {
        [JsonPropertyName("totalHits")]
        public int TotalHits { get; set; }

        [JsonPropertyName("data")]
        public List<NuGetPackageInfo>? Data { get; set; }
    }

    private sealed class NuGetPackageInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }
    }
}
