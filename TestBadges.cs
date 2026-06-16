using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "TwitchChatCore/1.0");

        try
        {
            Console.WriteLine("Fetching global badges...");
            var json = await httpClient.GetStringAsync("https://api.ivr.fi/v2/twitch/badges/global");
            Console.WriteLine($"Global JSON size: {json.Length}");
            
            int count = 0;
            using var doc = JsonDocument.Parse(json);
            foreach (var set in doc.RootElement.EnumerateArray())
            {
                count++;
            }
            Console.WriteLine($"Parsed {count} sets.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
