namespace SnoozeIt.Client.Services;

using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnoozeIt.Core.Engine;

/// <summary>
/// Client service for communicating with SnoozeIt.Service over \\.\pipe\SnoozeIt_IPC.
/// Seamlessly falls back to direct in-process execution (Direct Mode) if the service is stopped or uninstalled.
/// </summary>
public sealed class ProcessServiceClient
{
    private const string PipeName = "SnoozeIt_IPC";
    private readonly ProcessController _fallbackController = new();

    /// <summary>
    /// Checks if the privileged Windows Service named pipe is currently listening.
    /// </summary>
    public async Task<bool> IsServiceAvailableAsync(int timeoutMs = 250)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Requests process suspension and working-set eviction.
    /// </summary>
    public async Task<Result<long>> SuspendProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        var (connected, responseJson) = await SendRpcRequestAsync("Process.Suspend", new { processId, trimWorkingSet = true }, cancellationToken);

        if (connected && !string.IsNullOrEmpty(responseJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var resultElem))
                {
                    long reclaimed = resultElem.TryGetProperty("reclaimedBytes", out var rec) ? rec.GetInt64() : 0;
                    return Result<long>.Ok(reclaimed);
                }
                if (root.TryGetProperty("error", out var errElem))
                {
                    string message = errElem.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Unknown error" : "RPC Error";
                    return Result<long>.Fail(message);
                }
            }
            catch (Exception ex)
            {
                return Result<long>.Fail($"Response parsing error: {ex.Message}");
            }
        }

        // Direct Mode Fallback: execute directly via Core ProcessController
        return _fallbackController.SuspendAndTrim(processId);
    }

    /// <summary>
    /// Requests process resumption.
    /// </summary>
    public async Task<Result> ResumeProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        var (connected, responseJson) = await SendRpcRequestAsync("Process.Resume", new { processId }, cancellationToken);

        if (connected && !string.IsNullOrEmpty(responseJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out _))
                {
                    return Result.Ok();
                }
                if (root.TryGetProperty("error", out var errElem))
                {
                    string message = errElem.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Unknown error" : "RPC Error";
                    return Result.Fail(message);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Response parsing error: {ex.Message}");
            }
        }

        // Direct Mode Fallback: execute directly via Core ProcessController
        return _fallbackController.Resume(processId);
    }

    private async Task<(bool Connected, string? Response)> SendRpcRequestAsync(string method, object @params, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(300);

            await client.ConnectAsync(connectCts.Token);

            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            string id = Guid.NewGuid().ToString("N");
            string payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            });

            await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
            string? response = await reader.ReadLineAsync(cancellationToken);

            return (true, response);
        }
        catch
        {
            return (false, null);
        }
    }
}

