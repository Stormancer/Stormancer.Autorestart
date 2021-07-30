using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stormancer.Autorestart
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            this.configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cmd = GetCommand();
            Process? process = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                var output = new List<string>();
                await RunCommand("dotnet", "tool restore", cmd.WorkingDirectory, output, stoppingToken);
                prc = RunCommand(cmd.Name, cmd.Arguments, cmd.WorkingDirectory, output, cts.Token);
                await prc;
                await Task.Delay(cmd.RestartDelayMs, stoppingToken);
            }
            
        }

        private async Task<int> RunCommand(string name, string arguments, string workingDirectory, List<string> output, CancellationToken stoppingToken)
        {
            var prcStartInfos = new ProcessStartInfo(name, arguments);
            prcStartInfos.RedirectStandardOutput = true;
            prcStartInfos.RedirectStandardError = true;
            prcStartInfos.WorkingDirectory = workingDirectory;
            prcStartInfos.UseShellExecute = false;
           

            var process = Process.Start(prcStartInfos);

          
            using var registration = stoppingToken.Register(() => process.CloseMainWindow());
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, args) =>
            {
                output.Add(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                output.Add(args.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process != null)
            {
                while (!process.HasExited && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                }

                var closeTime = DateTime.UtcNow;
                while(!process.HasExited && stoppingToken.IsCancellationRequested && DateTime.UtcNow < closeTime.AddSeconds(20))
                {
                    await Task.Delay(1000);
                }
                if(!process.HasExited)
                {
                    process.Kill();
                }
                return process.ExitCode;
            }
            else
            {
                return -1;
            }
        }
        CancellationTokenSource cts = new CancellationTokenSource();
        Task<int>? prc;
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            cts.Cancel();
            if(prc!=null)
            {
                await prc;
            }
        }
        private CommandOptions GetCommand()
        {
            var section = configuration.GetSection("Command");
            var command = new CommandOptions();
            section.Bind(command);

            return command;

        }

    }

    public class CommandOptions
    {
        public string Name { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public int RestartDelayMs { get; set; }
    }
}
