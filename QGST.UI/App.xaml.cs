using System.Linq;
using System.Windows;

namespace QGST.UI;

public partial class App : Application
{
    public static string TargetFile { get; private set; } = string.Empty;
    public static string GpuId { get; private set; } = string.Empty;
    public static string Mode { get; private set; } = string.Empty;
    public static bool ResetMode { get; private set; } = false;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse command line arguments
        // Supported formats:
        //   --target "path" --gpu "id" --mode "one-time|set-default"
        //   --reset "path"
        
        for (int i = 0; i < e.Args.Length; i++)
        {
            var arg = e.Args[i].ToLowerInvariant();
            
            switch (arg)
            {
                case "--target" when i + 1 < e.Args.Length:
                    TargetFile = e.Args[++i];
                    break;
                    
                case "--gpu" when i + 1 < e.Args.Length:
                    GpuId = e.Args[++i];
                    break;
                    
                case "--mode" when i + 1 < e.Args.Length:
                    Mode = e.Args[++i].ToLowerInvariant();
                    break;
                    
                case "--reset":
                    ResetMode = true;
                    if (i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("--"))
                    {
                        TargetFile = e.Args[++i];
                    }
                    break;
                    
                case "--one-time":
                    Mode = "one-time";
                    break;
                    
                case "--set-default":
                    Mode = "set-default";
                    break;
                    
                default:
                    // If it's a path without a flag, treat as target
                    if (!arg.StartsWith("--") && System.IO.File.Exists(e.Args[i]))
                    {
                        TargetFile = e.Args[i];
                    }
                    break;
            }
        }
    }
}
