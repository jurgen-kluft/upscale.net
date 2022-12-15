using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ProcessRunner
{
    public class Parallel
    {
        public async Task ExecuteAsync(string[] commands)
        {
            // Create a task for each command to run.
            var tasks = new Task[commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                tasks[i] = Task.Run(() => ExecuteCommandAsync(commands[i]));
            }

            // Wait for all tasks to complete.
            await Task.WhenAll(tasks);
        }

        private async Task ExecuteCommandAsync(string command)
        {
            // Start the process and redirect its stdout and stderr streams.
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            };

            process.Start();

            // Pipe the process' stdout and stderr to the console.
            await Task.WhenAll(
                PipeStreamAsync(process.StandardOutput, Console.Out),
                PipeStreamAsync(process.StandardError, Console.Error)
            );

            process.WaitForExit();
        }

        private async Task PipeStreamAsync(StreamReader source, TextWriter destination)
        {
            string line;
            while ((line = await source.ReadLineAsync()) != null)
            {
                await destination.WriteLineAsync(line);
            }
        }
    }
}
