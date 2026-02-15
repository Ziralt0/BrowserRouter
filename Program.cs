using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

internal static class Program
{
    // ==========================================
    private const string AppKeyName = "BrowserRouter";
    private const string AppDisplayName = "Browser Router";
    private const string AppDescription = "Routes links to Chrome/Brave depending on what's already running.";
    private const string ProgId = "BrowserRouterURL";
    private static readonly string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private static readonly string BravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";

    // ==========================================

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 1)
            {
                var cmd = args[0].Trim();

                if (IsCommand(cmd, "/register"))
                    return Register();

                if (IsCommand(cmd, "/unregister"))
                    return Unregister();

                if (IsCommand(cmd, "/help") || IsCommand(cmd, "-h") || IsCommand(cmd, "--help"))
                {
                    PrintHelp();
                    return 0;
                }
            }

            //invoked as URL handler: BrowserRouter.exe "%1"
            var url = ExtractUrl(args);
            if (string.IsNullOrWhiteSpace(url))
                return 0;

            RouteUrl(url);
            return 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return 1;
        }
    }

    private static bool IsCommand(string a, string cmd) =>
        string.Equals(a, cmd, StringComparison.OrdinalIgnoreCase);

    private static void PrintHelp()
    {
        Console.WriteLine($"{AppDisplayName}");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  BrowserRouter.exe /register     (run as Administrator)");
        Console.WriteLine("  BrowserRouter.exe /unregister   (run as Administrator)");
        Console.WriteLine("  BrowserRouter.exe \"https://...\" (normal URL handling)");
    }

    private static int Register()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("ERROR: /register must be run as Administrator.");
            return 2;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            Console.Error.WriteLine("ERROR: Could not determine executable path.");
            return 3;
        }

        // HKLM paths
        string startMenuInternet = $@"SOFTWARE\Clients\StartMenuInternet\{AppKeyName}";
        string capabilities = $@"{startMenuInternet}\Capabilities";
        string urlAssociations = $@"{capabilities}\URLAssociations";
        string registeredApps = @"SOFTWARE\RegisteredApplications";

        // StartMenuInternet registration
        using (var k = Registry.LocalMachine.CreateSubKey(startMenuInternet, true))
        {
            k!.SetValue("", AppDisplayName, RegistryValueKind.String);
        }

        // Icon
        using (var k = Registry.LocalMachine.CreateSubKey($@"{startMenuInternet}\DefaultIcon", true))
        {
            k!.SetValue("", $"\"{exePath}\",0", RegistryValueKind.String);
        }

        // Capabilities
        using (var k = Registry.LocalMachine.CreateSubKey(capabilities, true))
        {
            k!.SetValue("ApplicationName", AppDisplayName, RegistryValueKind.String);
            k.SetValue("ApplicationDescription", AppDescription, RegistryValueKind.String);
        }

        // URL associations: http/https -> ProgID
        using (var k = Registry.LocalMachine.CreateSubKey(urlAssociations, true))
        {
            k!.SetValue("http", ProgId, RegistryValueKind.String);
            k.SetValue("https", ProgId, RegistryValueKind.String);
        }

        // RegisteredApplications entry
        using (var k = Registry.LocalMachine.CreateSubKey(registeredApps, true))
        {
            k!.SetValue(AppKeyName, capabilities, RegistryValueKind.String);
        }

        // ProgID handler under HKLM\Software\Classes
        using (var k = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Classes\{ProgId}", true))
        {
            k!.SetValue("", $"URL:{AppDisplayName}", RegistryValueKind.String);
            k.SetValue("URL Protocol", "", RegistryValueKind.String);
        }

        using (var k = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Classes\{ProgId}\shell\open\command", true))
        {
            // Windows passes the URL as %1
            k!.SetValue("", $"\"{exePath}\" \"%1\"", RegistryValueKind.String);
        }

        Console.WriteLine("Registered. Now set it as default browser:");
        Console.WriteLine("Settings -> Apps -> Default apps -> Web browser -> Browser Router");
        return 0;
    }

    private static int Unregister()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("ERROR: /unregister must be run as Administrator.");
            return 2;
        }

        string startMenuInternet = $@"SOFTWARE\Clients\StartMenuInternet\{AppKeyName}";
        string capabilities = $@"{startMenuInternet}\Capabilities";
        string registeredApps = @"SOFTWARE\RegisteredApplications";

        // Remove RegisteredApplications entry
        using (var k = Registry.LocalMachine.OpenSubKey(registeredApps, writable: true))
        {
            k?.DeleteValue(AppKeyName, throwOnMissingValue: false);
        }

        // Remove StartMenuInternet tree
        Registry.LocalMachine.DeleteSubKeyTree(startMenuInternet, throwOnMissingSubKey: false);

        // Remove ProgID tree
        Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Classes\{ProgId}", throwOnMissingSubKey: false);

        Console.WriteLine("Unregistered.");
        return 0;
    }

    private static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ExtractUrl(string[] args)
    {
        // Windows usually passes 1 arg: "%1"
        // join and pick the first thing that looks like a URL.
        if (args == null || args.Length == 0) return "";

        // Prefer exact first argument if it looks like http(s)
        var first = args[0]?.Trim().Trim('"') ?? "";
        if (CatchUrl(first)) return first;

        // Otherwise scan all args
        foreach (var a in args)
        {
            var s = (a ?? "").Trim().Trim('"');
            if (CatchUrl(s)) return s;
        }

        // last resort join
        var joined = string.Join(" ", args).Trim();
        var candidate = joined.Trim('"');
        return CatchUrl(candidate) ? candidate : "";
    }

    private static bool CatchUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static void RouteUrl(string url)
    {
        // Decide target
        var chromeRunning = Process.GetProcessesByName("chrome").Any();
        var braveRunning = Process.GetProcessesByName("brave").Any();

        var targetExe =
            chromeRunning ? ChromePath :
            braveRunning ? BravePath :
            ChromePath; // fallback

        // If the chosen browser isn't found, try the other as backup
        if (!File.Exists(targetExe))
        {
            var backup = targetExe.Equals(ChromePath, StringComparison.OrdinalIgnoreCase) ? BravePath : ChromePath;
            if (File.Exists(backup)) targetExe = backup;
        }

        if (!File.Exists(targetExe))
            return;

        var psi = new ProcessStartInfo
        {
            FileName = targetExe,
            Arguments = QuoteArg(url),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);
    }

    private static string QuoteArg(string s)
    {
        // Minimal safe quoting for a single argument
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains('"')) s = s.Replace("\"", "\\\"");
        return $"\"{s}\"";
    }
}
