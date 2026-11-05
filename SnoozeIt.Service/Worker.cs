namespace SnoozeIt.Service;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnoozeIt.Service.Ipc;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly PipeServer _pipeServer;

    public Worker(ILogger<Worker> logger, ILogger<PipeServer> pipeLogger)
    {
        _logger = logger;
        _pipeServer = new PipeServer(pipeLogger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SnoozeIt.Service executing background daemon at: {time}", DateTimeOffset.Now);
        await _pipeServer.RunAsync(stoppingToken);
    }
}
