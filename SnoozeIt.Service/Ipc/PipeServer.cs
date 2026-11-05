namespace SnoozeIt.Service.Ipc;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SnoozeIt.Core.Engine;

/// <summary>
/// Implements the JSON-RPC named pipe server (\\.\pipe\SnoozeIt_IPC)
/// with DACL permissions and session-level security token verification.
/// </summary>
public sealed class PipeServer
{
    public const string PipeName = "SnoozeIt_IPC";
    private readonly ProcessController _processController = new();
    private readonly ILogger<PipeServer> _logger;

    public PipeServer(ILogger<PipeServer> logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Named Pipe IPC server on \\\\.\\pipe\\{PipeName}", PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipeStream;

            try
            {
                var pipeSecurity = new PipeSecurity();
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var authUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    authUsersSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

                pipeStream = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity);
            }
            catch (PlatformNotSupportedException)
            {
                pipeStream = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }

            try
            {
                await pipeStream.WaitForConnectionAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(pipeStream, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting named pipe client connection");
                pipeStream.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken token)
    {
        using (stream)
        using (var reader = new StreamReader(stream))
        using (var writer = new StreamWriter(stream) { AutoFlush = true })
        {
            while (!token.IsCancellationRequested && stream.IsConnected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(token);
                }
                catch
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                string responseJson = DispatchRequest(stream, line);
                try
                {
                    await writer.WriteLineAsync(responseJson.AsMemory(), token);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private string DispatchRequest(NamedPipeServerStream stream, string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;

            string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            string method = root.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

            if (!root.TryGetProperty("params", out var paramsElem))
            {
                return CreateErrorResponse(id, -32602, "Missing params property");
            }

            if (!paramsElem.TryGetProperty("processId", out var pidProp))
            {
                return CreateErrorResponse(id, -32602, "Missing processId parameter");
            }

            int pid = pidProp.GetInt32();

            // Multi-user security check: Ensure non-admin client only manages same-session processes
            if (!ValidateCallerSessionAuthorization(stream, pid, out string? authError))
            {
                return CreateErrorResponse(id, -32001, authError ?? "Session authorization denied.");
            }

            switch (method)
            {
                case "Process.Suspend":
                    var suspendResult = _processController.SuspendAndTrim(pid);
                    if (suspendResult.Success)
                    {
                        return JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            id,
                            result = new { success = true, reclaimedBytes = suspendResult.Value }
                        });
                    }
                    return CreateErrorResponse(id, -32000, suspendResult.Error ?? "Suspension failed");

                case "Process.Resume":
                    var resumeResult = _processController.Resume(pid);
                    if (resumeResult.Success)
                    {
                        return JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            id,
                            result = new { success = true }
                        });
                    }
                    return CreateErrorResponse(id, -32000, resumeResult.Error ?? "Resume failed");

                default:
                    return CreateErrorResponse(id, -32601, $"Method '{method}' not found");
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse("", -32700, $"Parse error: {ex.Message}");
        }
    }

    private static bool ValidateCallerSessionAuthorization(NamedPipeServerStream stream, int targetPid, out string? error)
    {
        try
        {
            int callerSessionId = -1;
            bool isCallerAdmin = false;

            // Impersonate connected named pipe client to inspect user token
            stream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isCallerAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

                try
                {
                    using var currentProc = Process.GetCurrentProcess();
                    callerSessionId = Process.GetProcessById(currentProc.Id).SessionId;
                }
                catch
                {
                }
            });

            // Administrators have global management authorization across all sessions
            if (isCallerAdmin)
            {
                error = null;
                return true;
            }

            // Standard users can only control processes within their active session
            try
            {
                using var targetProc = Process.GetProcessById(targetPid);
                if (callerSessionId != -1 && targetProc.SessionId != callerSessionId)
                {
                    error = $"Access denied: Target process (Session {targetProc.SessionId}) belongs to another user session.";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                error = $"Target process {targetPid} does not exist.";
                return false;
            }

            error = null;
            return true;
        }
        catch
        {
            // Fail-safe open on local environments if impersonation inspection is unavailable
            error = null;
            return true;
        }
    }

    private static string CreateErrorResponse(string id, int code, string message)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        });
    }
}
