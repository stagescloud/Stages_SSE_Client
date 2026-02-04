# Stages SSE Client

A .NET SDK for subscribing to Server-Sent Events (SSE) from Stages Studio. 

## Features

- Real-time event streaming from Stages Studio rooms
- API key authentication with automatic room configuration
- Support for training session interval change events
- Multi-target support for .NET 8, 9, and 10

## Installation
Install the package from Nuget.

```bash
dotnet add package Stages.SseClient
```

## Quick Start

```csharp
using Stages.SseClient;
using Stages.SseClient.Authentication.RoomConfiguration;

// Step 1: Authenticate with your API key to get room configuration
using var authenticator = new RoomAuthenticator();
var config = await authenticator.AuthenticateAsync("https://your-stages-service.com", "your-api-key"); // README will be updated when this URL is avilable.

// Step 2: Connect to the SSE stream and handle events
using var client = new SseClient(config.SseUrl)
    .OnTrainingSessionIntervalChanged(evt =>
    {
        var current = evt.Payload.Data.CurrentInterval;

        var target = current?.Interval?.Primary?.Value;

        Console.WriteLine($"Target: {target}% FTP");
    })
    .OnError(ex => Console.WriteLine($"Error: {ex.Message}"));

await client.ConnectAsync();
```

## Authentication

The SDK uses a two-step authentication process:

1. **Room Authentication**: Exchange your API key for room configuration
2. **SSE Connection**: Connect to the SSE endpoint provided in the configuration

```csharp
using var authenticator = new RoomAuthenticator();

try
{
    RoomConfiguration config = await authenticator.AuthenticateAsync(serviceUrl, apiKey);

    using var client = new SseClient(config.SseUrl);
    client.OnTrainingSessionIntervalChanged(evt =>
        {
            // Handle interval change events
        });
}
catch (AuthenticationException ex)
{
    // Handle authentication failures
}
catch (Exception ex)
{
    // Handle other errors
}

```

### Configuration Format
The configuration is returned in the following JSON format:
```
{
    "clientId": "123456",
    "roomId": "123",
    "sseUrl": "https://next-sse.stagescloud.com/rooms/123"
}
```

## Event Structure

Events arrive from the SSE stream as JSON. Below is the full structure of a `TrainingSessionIntervalChanged` event:

```json
{
    "id": "1770191856506_Xq3cSUVy",
    "type": "training-session.interval.changed",
    "roomId": "828",
    "user_id": "828",
    "session_id": "e34c67f2-335c-4283-b853-a6777290ac12",
    "broadcastedAt": 1770191856506,
    "payload": "{...}"
}
```

The `payload` field is a JSON-encoded string. When deserialized, it has the following structure:

```json
{
    "data": {
        "currentIntervalIndex": 1,
        "currentInterval": {
        "index": null,
        "groupType": "workout",
        "absoluteStart": 60,
        "duration": 60,
        "enterOffset": 60,
        "exitOffset": 120,
        "groupEnterOffset": 0,
        "state": "future",
        "interval": {
            "id": "interval-2",
            "title": "Interval 2",
            "durationType": "time",
            "durationValue": 60,
            "primary": {
            "type": "ftpPercentage",
            "valueType": "single",
            "value": 110,
            "endValue": 110
            }
        }
        },
        "nextIntervalIndex": 2,
        "nextInterval": {
        "index": null,
        "groupType": "workout",
        "absoluteStart": 120,
        "duration": 60,
        "enterOffset": 120,
        "exitOffset": 180,
        "groupEnterOffset": 0,
        "state": "future",
        "interval": {
            "id": "interval-3",
            "title": "Interval 3",
            "durationType": "time",
            "durationValue": 60,
            "primary": {
            "type": "ftpPercentage",
            "valueType": "single",
            "value": 160,
            "endValue": 160
            }
        }
        }
    },
"event_type": "training-session.interval.changed",
"source": "studio-state-manager",
"timestamp": "2026-02-04T07:57:36.505761686Z"
}
```

### Field Reference

| Field | Type | Description |
|---|---|---|
| `currentIntervalIndex` | `int?` | Zero-based index of the current interval. Null at session boundaries. |
| `nextIntervalIndex` | `int?` | Zero-based index of the next interval. Null at end of session. |
| `groupType` | `string` | Interval group: `"workout"`, `"warmup"`, `"cooldown"`. |
| `state` | `string` | Interval state: `"future"`, `"active"`, `"completed"`. |
| `durationType` | `string` | How duration is measured, e.g. `"time"`, `"distance"`. |
| `primary.type` | `string` | Target type, e.g. `"ftpPercentage"`, `"heartRate"`, `"cadence"`. |
| `primary.valueType` | `string` | `"single"` for a fixed target, `"range"` for a range (use `value` and `endValue`). |

> **Note:** `currentInterval` and `nextInterval` will be `null` at session boundaries (start/end). Always null-check before accessing nested properties.

## Event Handling

### Training Session Interval Changed

Triggered when the current interval changes during a training session:

```csharp
client.OnTrainingSessionIntervalChanged(evt =>
{
    // Event metadata
    Console.WriteLine($"Session ID: {evt.SessionId}");
    Console.WriteLine($"Room ID: {evt.RoomId}");

    // Current interval
    var current = evt.Payload.Data.CurrentInterval;
    if (current?.Interval != null)
    {
        Console.WriteLine($"Title: {current.Interval.Title}");
        Console.WriteLine($"Duration: {current.Interval.DurationValue}s");
        Console.WriteLine($"Target: {current.Interval.Primary?.Value}% FTP");
        Console.WriteLine($"State: {current.State}");
    }

    // Next interval (if available)
    var next = evt.Payload.Data.NextInterval;
    if (next?.Interval != null)
    {
        Console.WriteLine($"Up next: {next.Interval.Title}");
    }
});
```

### Power Zone Conversions
The SDK includes the PowerZoneHelper class, which returns the resulting zone, based on the FTP percentage in the interval target.
It can also return the target colour for the interval. 

```csharp
client.OnTrainingSessionIntervalChanged(evt =>
{
    var current = evt.Payload.Data.CurrentInterval;
    if (current?.Interval != null)
    {
        Console.WriteLine($"Target: {current.Interval.Primary?.Value}% FTP");
        Console.WriteLine($"Target Zone Name: {PowerZoneHelper.GetZone(current?.Interval?.Primary?.Value)}");
        Console.WriteLine($"Color: {PowerZoneHelper.GetColor(current?.Interval?.Primary?.Value)}");
    }
});
```


### Error Handling

Register an error handler to catch connection and parsing errors:

```csharp
client.OnError(ex =>
{
    Console.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
});
```

## Connection Management

### Connecting with Cancellation

```csharp
using var cts = new CancellationTokenSource();

// Cancel on Ctrl+C
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await client.ConnectAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Normal cancellation
}
```

### Disconnection

```csharp
client.Disconnect();
```

## Test Application

The repository includes a test application that demonstrates all SDK features:

```bash
cd Stages.SseClient.TestApp

# Using command line arguments
dotnet run -- --api-key <your-api-key> --service-url <service-url>
dotnet run -- -k <your-api-key> -s <service-url>

# Using environment variables
export STAGES_API_KEY=your-api-key
export STAGES_SERVICE_URL=https://your-stages-service.com
dotnet run
```

## Requirements

- .NET 8.0, 9.0, or 10.0

## License

Proprietary - SPIA Cycling

