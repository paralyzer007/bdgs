// BDGS Launcher - downloads the BDGS custom maps from the fastdl (Cloudflare R2 via Worker),
// Download only - it does not start the game. Target: .NET Framework 4.x (built into Windows 10/11).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("BDGS Launcher")]
[assembly: AssemblyProduct("BDGS Launcher")]
[assembly: AssemblyDescription("Downloads BDGS [SAB] custom maps")]
[assembly: AssemblyCompany("BDGS")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Launcher
{
    const string BaseUrl = "https://iw4x-fastdl.ahmed-shahjada.workers.dev/";

    static readonly string[] Maps = {
            "mp_afghan", "mp_derail", "mp_estate", "mp_favela", "mp_highrise", "mp_invasion",
            "mp_checkpoint", "mp_quarry", "mp_rundown", "mp_rust", "mp_boneyard", "mp_nightshift",
            "mp_subbase", "mp_terminal", "mp_underpass", "mp_brecourt", "mp_crash", "mp_overgrown",
            "mp_compact", "mp_storm", "mp_abandon", "mp_fuel2", "mp_strike", "mp_trailerpark",
            "mp_vacant", "mp_nuked", "mp_bloc", "mp_bog_sh", "mp_cargoship", "mp_cross_fire",
            "mp_crash_snow", "mp_killhouse", "mp_cargoship_sh", "mp_shipment_long", "mp_rust_long", "mp_firingrange",
            "mp_storm_spring", "mp_fav_tropical", "mp_estate_tropical", "mp_crash_tropical", "mp_bloc_sh", "mp_backlot",
            "mp_shipment", "mp_broadcast", "mp_carentan", "mp_citystreets", "mp_convoy", "mp_countdown",
            "mp_farm", "mp_pipeline", "mp_showdown", "mp_dome", "mp_hardhat", "mp_paris",
            "mp_plaza2", "mp_seatown", "mp_village", "mp_alpha", "mp_bravo", "oilrig",
            "co_hunted", "mp_aground_ss", "mp_bootleg", "mp_burn_ss", "mp_carbon", "mp_cement",
            "mp_courtyard_ss", "mp_csgo_monastery", "mp_hillside_ss", "mp_interchange", "mp_italy", "mp_lambeth",
            "mp_meteora", "mp_moab", "mp_mogadishu", "mp_nola", "mp_overwatch", "mp_qadeem",
            "mp_rasalem", "mp_roughneck", "mp_shipbreaker"
    };
    static readonly string[] FilePatterns = { "{0}.ff", "{0}_load.ff", "{0}.iwd", "{0}.arena" };

    class Item { public string Map, Name, Url, Dest; public long Size; }

    static long doneBytes, totalBytes, movedBytes;
    static readonly Stopwatch clock = new Stopwatch();
    const char Full = '█', Empty = '░';

    [STAThread]
    static int Main(string[] args)
    {
        try { return Run(); }
        catch (Exception e)
        {
            Console.WriteLine();
            Say("Unexpected error: " + e.Message, ConsoleColor.Red);
            return Finish(1);
        }
    }

    static int Run()
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        ServicePointManager.DefaultConnectionLimit = 8;
        string baseUrl = Environment.GetEnvironmentVariable("BDGS_BASEURL") ?? BaseUrl;
        try { Console.Title = "BDGS Launcher"; } catch { }

        try { Console.Clear(); } catch { }
        Say("=============================================", ConsoleColor.Green);
        Say("   BDGS [SAB]  -  map downloader", ConsoleColor.Green);
        Say("=============================================", ConsoleColor.Green);
        Console.WriteLine();

        string game = FindGameDir();
        if (game == null) { Say("No folder picked - nothing to do.", ConsoleColor.Red); return Finish(1); }
        Say("Game folder: " + game);
        string usermaps = Path.Combine(game, "usermaps");

        if (Process.GetProcessesByName("iw4x").Length > 0)
        {
            Say("IW4x is running. Close the game first so map files can be updated.", ConsoleColor.Yellow);
            Console.Write("Press Enter when the game is closed");
            Console.ReadLine();
        }

        // ---- 1. check
        Console.WriteLine();
        Say("Checking " + Maps.Length + " maps against the BDGS server...");
        var todo = new List<Item>();
        int custom = 0;
        var mapsTodo = new HashSet<string>();
        try
        {
            for (int i = 0; i < Maps.Length; i++)
            {
                string map = Maps[i];
                DrawBar((double)i / Maps.Length, "checking " + map + " (" + (i + 1) + " of " + Maps.Length + ")");
                bool first = true;
                foreach (string pat in FilePatterns)
                {
                    string name = string.Format(pat, map);
                    string rel = "usermaps/" + map + "/" + name;
                    long size = RemoteSize(baseUrl + rel);
                    if (size < 0) { if (first) break; continue; }   // no .ff = built-in map
                    if (first) { custom++; first = false; }
                    string dest = Path.Combine(Path.Combine(usermaps, map), name);
                    if (File.Exists(dest) && new FileInfo(dest).Length == size) continue;
                    todo.Add(new Item { Map = map, Name = name, Url = baseUrl + rel, Dest = dest, Size = size });
                    mapsTodo.Add(map);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Say("Can't reach the BDGS map server: " + e.Message, ConsoleColor.Red);
            Say("Check your internet connection and try again.", ConsoleColor.Red);
            return Finish(1);
        }
        DrawBar(1, "check complete");
        Console.WriteLine();

        foreach (var t in todo) totalBytes += t.Size;
        Say(string.Format("Custom maps on BDGS: {0}   up to date: {1}   to download: {2} ({3})",
            custom, custom - mapsTodo.Count, mapsTodo.Count, GB(totalBytes)), ConsoleColor.Green);

        // ---- 2. download
        if (todo.Count > 0)
        {
            long free = new DriveInfo(Path.GetPathRoot(game)).AvailableFreeSpace;
            if (free < totalBytes + 500L * 1024 * 1024)
            {
                Say(string.Format("Not enough disk space on {0}: need {1}, free {2}.",
                    Path.GetPathRoot(game), GB(totalBytes + 500L * 1024 * 1024), GB(free)), ConsoleColor.Red);
                return Finish(1);
            }
            Console.WriteLine();
            Say("Downloading - you can close this window any time; it continues where it stopped next time.");
            clock.Restart();
            for (int n = 0; n < todo.Count; n++)
            {
                var f = todo[n];
                string label = string.Format("[{0}/{1}] {2}/{3}", n + 1, todo.Count, f.Map, f.Name);
                SayAboveBar(string.Format("{0,-60} {1,10}", label, MB(f.Size)), ConsoleColor.Gray);
                ShowProgress(0);
                try { SaveFile(f); }
                catch (Exception e)
                {
                    Console.WriteLine();
                    Say("Download failed: " + e.Message, ConsoleColor.Red);
                    Say("Run the launcher again to continue where it stopped.", ConsoleColor.Red);
                    return Finish(1);
                }
                doneBytes += f.Size;
                ShowProgress(0);
            }
            Console.WriteLine();
            double secs = Math.Max(1, clock.Elapsed.TotalSeconds);
            Say(string.Format("All maps downloaded: {0} in {1:mm\\:ss} ({2}/s)",
                GB(totalBytes), TimeSpan.FromSeconds((int)secs), MB(movedBytes / secs)), ConsoleColor.Green);
        }
        else Say("You already have every BDGS map.", ConsoleColor.Green);

        Console.WriteLine();
        Say("All BDGS maps are ready - start IW4x and join the server as usual.", ConsoleColor.Green);
        return Finish(0);
    }

    // ------------------------------------------------------------------ game folder
    static string FindGameDir()
    {
        string cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BDGS");
        string cfg = Path.Combine(cfgDir, "gamedir.txt");
        var candidates = new List<string>();
        string env = Environment.GetEnvironmentVariable("BDGS_GAMEDIR");
        if (env != null) candidates.Add(env);
        candidates.Add(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
        candidates.Add(Environment.CurrentDirectory);
        try { if (File.Exists(cfg)) candidates.Add(File.ReadAllText(cfg).Trim()); } catch { }
        foreach (string d in candidates)
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "iw4x.exe"))) return d;

        Say("Couldn't find iw4x.exe next to this launcher.", ConsoleColor.Yellow);
        Say("Pick your IW4x / MW2 folder (the one that contains iw4x.exe)...", ConsoleColor.Yellow);
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Select your IW4x / MW2 folder (the one with iw4x.exe)";
            while (true)
            {
                if (dlg.ShowDialog() != DialogResult.OK) return null;
                if (File.Exists(Path.Combine(dlg.SelectedPath, "iw4x.exe")))
                {
                    try { Directory.CreateDirectory(cfgDir); File.WriteAllText(cfg, dlg.SelectedPath); } catch { }
                    return dlg.SelectedPath;
                }
                MessageBox.Show("iw4x.exe is not in that folder. Try again.", "BDGS Launcher");
            }
        }
    }

    // ------------------------------------------------------------------ http
    static HttpWebRequest NewRequest(string url, string method)
    {
        var r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = method;
        r.UserAgent = "BDGS-Launcher/1.0";
        r.Timeout = 30000;
        r.ReadWriteTimeout = 60000;
        r.AllowAutoRedirect = true;
        return r;
    }

    static long RemoteSize(string url)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using (var resp = (HttpWebResponse)NewRequest(url, "HEAD").GetResponse())
                    return resp.ContentLength;
            }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r != null && (r.StatusCode == HttpStatusCode.NotFound || r.StatusCode == HttpStatusCode.Forbidden)) return -1;
                if (attempt >= 3) throw;
                Thread.Sleep(2000);
            }
        }
    }

    static void SaveFile(Item f)
    {
        string part = f.Dest + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(f.Dest));
        for (int attempt = 1; ; attempt++)
        {
            long have = 0;
            if (File.Exists(part))
            {
                have = new FileInfo(part).Length;
                if (have >= f.Size) { File.Delete(part); have = 0; }
            }
            try
            {
                var req = NewRequest(f.Url, "GET");
                if (have > 0) req.AddRange(have);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent) have = 0;
                    using (var input = resp.GetResponseStream())
                    using (var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                    {
                        var buf = new byte[1 << 20];
                        long got = have;
                        var tick = Stopwatch.StartNew();
                        int n;
                        while ((n = input.Read(buf, 0, buf.Length)) > 0)
                        {
                            output.Write(buf, 0, n);
                            got += n;
                            movedBytes += n;
                            if (tick.ElapsedMilliseconds >= 250) { ShowProgress(got); tick.Restart(); }
                        }
                    }
                }
                long len = new FileInfo(part).Length;
                if (len != f.Size) throw new IOException("incomplete download (" + len + " of " + f.Size + " bytes)");
                if (File.Exists(f.Dest)) File.Delete(f.Dest);
                File.Move(part, f.Dest);
                return;
            }
            catch (Exception e)
            {
                if (attempt >= 5) throw;
                SayAboveBar("   connection problem (" + e.Message + ") - retrying...", ConsoleColor.DarkYellow);
                Thread.Sleep(2000 * attempt);
            }
        }
    }

    // ------------------------------------------------------------------ console output
    static int Width()
    {
        int w = 100;
        try { w = Console.WindowWidth - 1; } catch { }
        return w < 60 ? 100 : w;
    }

    static void DrawBar(double fraction, string info)
    {
        fraction = Math.Max(0.0, Math.Min(1.0, fraction));
        const int barWidth = 32;
        int fill = (int)Math.Round(fraction * barWidth);
        int room = Math.Max(0, Width() - (barWidth + 8));
        string tail = " " + info;
        if (tail.Length > room) tail = tail.Substring(0, room);
        var old = Console.ForegroundColor;
        Console.Write("\r");
        Console.ForegroundColor = ConsoleColor.White;       Console.Write(string.Format("{0,4}% ", (int)(fraction * 100)));
        Console.ForegroundColor = ConsoleColor.Magenta;     Console.Write(new string(Full, fill));
        Console.ForegroundColor = ConsoleColor.DarkMagenta; Console.Write(new string(Empty, barWidth - fill));
        Console.ForegroundColor = ConsoleColor.Gray;        Console.Write(tail.PadRight(room));
        Console.ForegroundColor = old;
    }

    static void ShowProgress(long fileDone)
    {
        long all = doneBytes + fileDone;
        double frac = totalBytes > 0 ? (double)all / totalBytes : 1;
        double secs = Math.Max(0.5, clock.Elapsed.TotalSeconds);
        double bps = movedBytes / secs;
        var eta = bps > 0 ? TimeSpan.FromSeconds((int)((totalBytes - all) / bps)) : TimeSpan.Zero;
        DrawBar(frac, string.Format("{0} / {1} | {2}/s | {3:hh\\:mm\\:ss} left", GB(all), GB(totalBytes), MB(bps), eta));
    }

    static void SayAboveBar(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine("\r" + text.PadRight(Width()));
        Console.ForegroundColor = old;
    }

    static void Say(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }

    static string MB(double b) { return string.Format("{0:N1} MB", b / 1048576.0); }
    static string GB(double b) { return string.Format("{0:N2} GB", b / 1073741824.0); }

    static int Finish(int code)
    {
        Console.WriteLine();
        Console.Write("Press Enter to close");
        try { Console.ReadLine(); } catch { }
        return code;
    }
}
