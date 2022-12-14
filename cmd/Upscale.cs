using System.CommandLine;

namespace Upscale
{
    // Parse command line arguments, the following args are supported:
    // - -d, --dryrun: Dry run, don't actually run the process, only show in the console (and log) what might be run)
    // - -f, --folders <folders> - List of folders to search for files separated by ;
    // - -t, --transform <transform> - Path to the transform file (e.g. "{tools.path}/transforms.config.json")
    // - -p, --processes <processes> - Path to the processes file (e.g. "{tools.path}/processes.config.json")


    class Program
    {
        private static string ExpandPaths(string path, Dictionary<string, string> paths)
        {
            foreach (var item in paths)
            {
                if (path.Contains(item.Key))
                {
                    path = path.Replace($"{item.Key}", item.Value);
                    path = Environment.ExpandEnvironmentVariables(path);
                    path = Path.GetFullPath(path);
                    break;
                }
            }
            return path;
        }

        private static int Run(bool dryrun, string folders, string transformsFilepath, string processesFilepath)
        {
            var paths = new Dictionary<string, string>();

            foreach(var f in folders.Split(';'))
            {
                var kv = f.Trim().Split('=');
                if (kv.Length == 2)
                {
                    var key = $"{{{kv[0].Trim()}}}";
                    paths.Add(key, kv[1].Trim());
                }
            }

            // Expand paths in the transforms and processes filepaths
            transformsFilepath = ExpandPaths(transformsFilepath, paths);
            processesFilepath = ExpandPaths(processesFilepath, paths);

            // Load JSON 'process.config.json' file
            if (Config.Processes.ReadJson(processesFilepath, out var processes) == false)
            {
                return -1;
            }
            if (Config.Transforms.ReadJson(transformsFilepath, out var transforms) == false)
            {
                return -1;
            }

            return 0;
        }

        public static int Main(string[] args)
        {
            var optionDryrun =  new Option<bool>(new[] { "-d", "--dryrun" }, "Dry run, don't actually run the process, only show in the console (and log) what might be run)");
            var optionFolders =  new Option<string>(new[] { "-f", "--folders" }, "List of folders to search for files separated by ;");
            var optionTransforms = new Option<string>(new[] { "-t", "--transforms" }, getDefaultValue: () => "{tools.path}/transforms.config.json", "Path to the transform file (e.g. \"{tools.path}/transforms.config.json\")");
            var optionProcesses = new Option<string>(new[] { "-p", "--processes" }, getDefaultValue: () => "{tools.path}/processes.config.json", "Path to the processes file (e.g. \"{tools.path}/processes.config.json\")");

            var rootCommand = new RootCommand
            {
                optionDryrun,
                optionFolders,
                optionTransforms,
                optionProcesses
            };

            rootCommand.SetHandler((dryrun, folders, transforms, processes) =>
            {
                Run(dryrun, folders, transforms, processes);
            },
            optionDryrun, optionFolders, optionTransforms, optionProcesses);

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}
