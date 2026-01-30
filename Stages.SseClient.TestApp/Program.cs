using System.CommandLine;
using Stages.SseClient;
using Stages.SseClient.Authentication;
using Stages.SseClient.Authentication.RoomConfiguration;

var apiKeyOption = new Option<string?>(
    aliases: ["--api-key", "-k"],
    description: "The API key for authentication");

var serviceUrlOption = new Option<string?>(
    aliases: ["--service-url", "-s"],
    description: "The Stages service URL");

var rootCommand = new RootCommand("Stages SSE Client Test Application")
{
    apiKeyOption,
    serviceUrlOption
};

rootCommand.SetHandler(async (apiKeyArg, serviceUrlArg) =>
{
    DisplayHeader();

    // Priority: argument > environment variable > default
    var apiKey = apiKeyArg
        ?? Environment.GetEnvironmentVariable("STAGES_API_KEY");

    var serviceUrl = serviceUrlArg
        ?? Environment.GetEnvironmentVariable("STAGES_SERVICE_URL")
        ?? "http://localhost:3500";

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("  [!] No API key configured.");
        Console.WriteLine();
        Console.WriteLine("  Provide the API key via argument or environment variable:");
        Console.WriteLine("    dotnet run -- --api-key <key> [--service-url <url>]");
        Console.WriteLine("    dotnet run -- -k <key> [-s <url>]");
        Console.WriteLine();
        Console.WriteLine("  Or set environment variables:");
        Console.WriteLine("    STAGES_API_KEY (required)");
        Console.WriteLine("    STAGES_SERVICE_URL (optional, defaults to http://localhost:3500)");
        Console.WriteLine();
        Environment.Exit(1);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Step 1: Authenticate with API Key
    // ─────────────────────────────────────────────────────────────────────────────

    Console.WriteLine("  STEP 1: Room Authentication");
    Console.WriteLine();
    Console.WriteLine($"  Service URL: {serviceUrl}");
    Console.WriteLine($"  API Key:     {apiKey[..Math.Min(8, apiKey.Length)]}...");
    Console.WriteLine();
    Console.WriteLine("  Authenticating...");

    RoomConfiguration config;
    using var authenticator = new RoomAuthenticator();

    try
    {
        config = await authenticator.AuthenticateAsync(serviceUrl, apiKey);
    }
    catch (AuthenticationException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"  [ERROR] Authentication failed: {ex.Message}");
        Console.WriteLine();
        Environment.Exit(1);
        return;
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"  [ERROR] Connection failed: {ex.Message}");
        Console.WriteLine();
        Environment.Exit(1);
        return;
    }

    Console.WriteLine("  Authentication successful!");
    Console.WriteLine();
    Console.WriteLine("  STEP 2: Room Configuration Received");
    Console.WriteLine();
    Console.WriteLine($"  Client ID:   {config.ClientId}");
    Console.WriteLine($"  Room ID:     {config.RoomId}");
    Console.WriteLine($"  SSE URL:     {config.SseUrl}");
    Console.WriteLine();

    // ─────────────────────────────────────────────────────────────────────────────
    // Step 3: Connect to SSE Stream
    // ─────────────────────────────────────────────────────────────────────────────

    Console.WriteLine("  STEP 3: Connecting to SSE Stream");
    Console.WriteLine();
    Console.WriteLine($"  Connecting to: {config.SseUrl}");
    Console.WriteLine("  Press Ctrl+C to disconnect");
    Console.WriteLine();
    Console.WriteLine("  ─────────────────────────────────────────────────────────────");
    Console.WriteLine("  Waiting for events...");
    Console.WriteLine();

    // Pattern to handle SSE events.
    // This example shows handling the training-session.interval.changed event.
    using var client = new SseClient(config.SseUrl)
        .OnTrainingSessionIntervalChanged(evt =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            Console.WriteLine($"  [{timestamp}] EVENT: training-session.interval.changed");
            Console.WriteLine($"  ├── Session ID: {evt.SessionId}");

            var current = evt.Payload.Data.CurrentInterval;
            var next = evt.Payload.Data.NextInterval;

            if (current?.Interval != null)
            {
                var currentIndex = evt.Payload.Data.CurrentIntervalIndex?.ToString() ?? "unknown";
                Console.WriteLine($"  ├── Current Interval ({currentIndex}):");
                Console.WriteLine($"  │   ├── Title:    {current.Interval.Title}");
                Console.WriteLine($"  │   ├── Duration: {current.Interval.DurationValue}s");
                if (current.Interval.Primary != null)
                {
                    Console.WriteLine($"  │   ├── Target:   {current.Interval.Primary.Value}% FTP");
                }
                Console.WriteLine($"  │   └── State:    {current.State}");

                // Display power zone with color
                var currentFtp = current?.Interval?.Primary?.Value ?? 0;
                var currentZone = PowerZoneHelper.GetZone(currentFtp);
                var currentColor = PowerZoneHelper.GetColor(currentFtp);
                Console.WriteLine();
                WriteColoured($"  ⬤ Training Zone: {currentZone}", currentColor);
                Console.WriteLine();
            }

            if (next?.Interval != null)
            {
                var nextIndex = evt.Payload.Data.NextIntervalIndex?.ToString() ?? "unknown";
                Console.WriteLine($"  └── Next Interval ({nextIndex}):");
                Console.WriteLine($"      ├── Title:    {next.Interval.Title}");
                Console.WriteLine($"      ├── Duration: {next.Interval.DurationValue}s");
                if (next.Interval.Primary != null)
                {
                    Console.WriteLine($"      └── Target:   {next.Interval.Primary.Value}% FTP");
                }

                // Display power zone with color
                var nextFtp = next?.Interval?.Primary?.Value ?? 0;
                var nextZone = PowerZoneHelper.GetZone(nextFtp);
                var nextColor = PowerZoneHelper.GetColor(nextFtp);
                Console.WriteLine();
                WriteColoured($"      Training Zone: {nextZone}", nextColor);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"  └── Next Interval: (none)");
            }

            Console.WriteLine();
        })
        .OnError(ex =>
        {
            Console.WriteLine($"  [ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine();
        });

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine();
        Console.WriteLine("  Disconnecting...");
    };

    try
    {
        await client.ConnectAsync(cts.Token);
    }
    catch (AuthenticationException ex)
    {
        Console.WriteLine($"  [AUTH ERROR] {ex.Message}");
        Environment.Exit(1);
    }
    catch (OperationCanceledException)
    {
        // Normal cancellation
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"  [HTTP ERROR] Status: {ex.StatusCode}");
        Console.WriteLine($"  Message: {ex.Message}");
        Environment.Exit(1);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [ERROR] {ex.GetType().Name}: {ex.Message}");
        Environment.Exit(1);
    }

    Console.WriteLine();
    Console.WriteLine("  ─────────────────────────────────────────────────────────────");
    Console.WriteLine("  Connection closed.");
    Console.WriteLine();

}, apiKeyOption, serviceUrlOption);

return await rootCommand.InvokeAsync(args);


void DisplayHeader()
{
string ascii ="""
    ____                           ______            _____                        __  _
   / __ \____  ____  ____ ___     / ____/___  ____  / __(_)___ ___  ___________ _/ /_(_)___  ____
  / /_/ / __ \/ __ \/ __ `__ \   / /   / __ \/ __ \/ /_/ / __ `/ / / / ___/ __ `/ __/ / __ \/ __ \
 / _, _/ /_/ / /_/ / / / / / /  / /___/ /_/ / / / / __/ / /_/ / /_/ / /  / /_/ / /_/ / /_/ / / / /
/_/ |_|\____/\____/_/ /_/ /_/   \____/\____/_/ /_/_/ /_/\__, /\__,_/_/   \__,_/\__/_/\____/_/ /_/
   / __ \___  ____ ___  ____                           /____/
  / / / / _ \/ __ `__ \/ __ \
 / /_/ /  __/ / / / / / /_/ /
/_____/\___/_/ /_/ /_/\____/
""";

Console.WriteLine(ascii);
Console.WriteLine("");
}

void WriteColoured(string text, string hexColor)
{
    var hex = hexColor.TrimStart('#');
    var r = Convert.ToInt32(hex.Substring(0, 2), 16);
    var g = Convert.ToInt32(hex.Substring(2, 2), 16);
    var b = Convert.ToInt32(hex.Substring(4, 2), 16);

    Console.WriteLine($"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m");
}
