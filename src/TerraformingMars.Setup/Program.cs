using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace TerraformingMars.Setup;

/// <summary>
/// Φτιάχνει τους installers του παιχνιδιού από τα ήδη published binaries.
/// <para>
/// Διαβάζει έναν φάκελο με έναν υποφάκελο ανά λειτουργικό (<c>WinX64</c>, <c>LinuxX64</c>,
/// <c>LinuxArm</c> — ό,τι παράγουν τα publish profiles) και βγάζει:
/// </para>
/// <list type="bullet">
/// <item>Windows: ένα self-extracting <c>Setup.exe</c> (IExpress) που ξεπακετάρει στο
/// <c>%LOCALAPPDATA%\Programs</c>, φτιάχνει συντομεύσεις σε Έναρξη + επιφάνεια εργασίας και
/// γράφεται στα «Εφαρμογές &amp; δυνατότητες» με uninstaller.</item>
/// <item>Linux / Linux ARM: ένα self-extracting <c>.sh</c> (tar.gz κολλημένο στο τέλος) που
/// εγκαθιστά σε <c>/opt</c> ή <c>~/.local/share</c>, γράφει <c>.desktop</c> στο μενού
/// εφαρμογών και στην επιφάνεια εργασίας, και αφήνει <c>uninstall.sh</c>.</item>
/// </list>
/// Δεν χρειάζεται τίποτα εγκατεστημένο πέρα από το .NET SDK και το IExpress των Windows.
/// </summary>
internal static class Program
{
    private const string AppName = "Mars Terraforming";
    private const string AppId = "MarsTerraforming";              // κλειδί μητρώου / uninstall id
    private const string LinuxPackage = "mars-terraforming";      // όνομα πακέτου & .desktop στο Linux
    private const string Publisher = "Vassilis Perantzakis";
    private const string Comment = "Build a colony on Mars, then make Mars habitable";
    private const string DefaultSource = @"C:\Deploy\TerraformingMars";
    private const string FallbackVersion = "1.2";

    /// <summary>Ένας στόχος = ένας υποφάκελος του source με τα published αρχεία.</summary>
    private sealed record Target(string Folder, string Rid, bool Windows, string ArchLabel)
    {
        public string ExeName => Windows ? "TerraformingMars.Game.exe" : "TerraformingMars.Game";
    }

    private static readonly Target[] Targets =
    {
        new("WinX64",   "win-x64",   true,  "Windows x64"),
        new("LinuxX64", "linux-x64", false, "Linux x64"),
        new("LinuxArm", "linux-arm", false, "Linux ARM"),
    };

    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h")) { Usage(); return 0; }

        string source = ArgValue(args, "--source") ?? DefaultSource;
        string output = ArgValue(args, "--out") ?? Path.Combine(source, "Installers");
        string? only = ArgValue(args, "--targets");

        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"Source folder not found: {source}");
            Console.Error.WriteLine("Publish the game first (Properties/PublishProfiles) or pass --source <dir>.");
            return 1;
        }

        var wanted = Targets.Where(t => only is null ||
            only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(s => s.Equals(t.Rid, StringComparison.OrdinalIgnoreCase))).ToList();
        if (wanted.Count == 0) { Console.Error.WriteLine($"No target matches --targets {only}"); return 1; }

        string version = ArgValue(args, "--version") ?? DetectVersion(source);
        Directory.CreateDirectory(output);

        Console.WriteLine($"{AppName} {version} - building installers");
        Console.WriteLine($"  source: {source}");
        Console.WriteLine($"  output: {output}");
        Console.WriteLine();

        var built = new List<(string file, long bytes)>();
        int failures = 0;
        foreach (var target in wanted)
        {
            string payload = Path.Combine(source, target.Folder);
            if (!Directory.Exists(payload))
            {
                Console.WriteLine($"[skip] {target.ArchLabel}: {payload} does not exist");
                continue;
            }
            if (!File.Exists(Path.Combine(payload, target.ExeName)))
            {
                Console.WriteLine($"[skip] {target.ArchLabel}: {target.ExeName} not found in {payload}");
                continue;
            }

            Console.WriteLine($"[{target.Rid}] {target.ArchLabel}");
            try
            {
                string file = target.Windows
                    ? BuildWindowsInstaller(payload, output, version, target)
                    : BuildLinuxInstaller(payload, output, version, target);
                built.Add((file, new FileInfo(file).Length));
                Console.WriteLine($"  -> {Path.GetFileName(file)}  ({Mb(new FileInfo(file).Length)})");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"  !! failed: {ex.Message}");
            }
            Console.WriteLine();
        }

        if (built.Count == 0) { Console.Error.WriteLine("Nothing was built."); return 1; }

        Console.WriteLine("Installers:");
        foreach (var (file, bytes) in built) Console.WriteLine($"  {Mb(bytes),9}  {file}");
        Console.WriteLine();
        Console.WriteLine($"Linux: ship the .sh as-is - the user runs  sh {AppId}-*-linux-*.sh");
        return failures == 0 ? 0 : 1;
    }

    private static void Usage() => Console.WriteLine($"""
        {AppName} installer builder

          dotnet run --project src/TerraformingMars.Setup -- [options]

          --source DIR    folder holding WinX64 / LinuxX64 / LinuxArm  (default: {DefaultSource})
          --out DIR       where to write the installers                (default: <source>\Installers)
          --targets LIST  comma separated: win-x64,linux-x64,linux-arm (default: all present)
          --version V     version shown by the installer               (default: read from the game exe)
          -h, --help      this text
        """);

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length) return args[i + 1];
        string prefix = name + "=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    }

    /// <summary>Παίρνει την έκδοση από το ίδιο το published exe των Windows (πάντα συμφωνεί με το payload).</summary>
    private static string DetectVersion(string source)
    {
        string exe = Path.Combine(source, "WinX64", "TerraformingMars.Game.exe");
        if (File.Exists(exe) && FileVersionInfo.GetVersionInfo(exe) is { } info)
        {
            if (info.FileMajorPart > 0 || info.FileMinorPart > 0)
                return $"{info.FileMajorPart}.{info.FileMinorPart}";
        }
        return FallbackVersion;
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

    // ----------------------------------------------------------------- κοινά

    /// <summary>Τα αρχεία του payload που μπαίνουν στον installer (χωρίς σύμβολα & άσχετα zip).</summary>
    private static List<string> PayloadFiles(string payloadDir) =>
        Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                     && !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Template(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        string resource = asm.GetManifestResourceNames()
            .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] IconIco()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Icon.ico")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Render(string template, string version) => template
        .Replace("@APP_NAME@", AppName)
        .Replace("@APP_VERSION@", version)
        .Replace("@APP_ID@", AppId)
        .Replace("@PUBLISHER@", Publisher)
        .Replace("@COMMENT@", Comment)
        .Replace("@PKG@", LinuxPackage);

    /// <summary>
    /// Βγάζει το μεγαλύτερο PNG που κρύβει ένα .ico (τα εικονίδια ≥256px αποθηκεύονται ως PNG).
    /// Χρειάζεται για το Linux, όπου το .ico δεν είναι χρησιμοποιήσιμο εικονίδιο εφαρμογής.
    /// </summary>
    private static byte[]? ExtractLargestPng(byte[] ico)
    {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1) return null;   // type 1 = icon
        int count = BitConverter.ToUInt16(ico, 4);
        byte[]? best = null;
        int bestPixels = 0;

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            if (entry + 16 > ico.Length) break;
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            int size = BitConverter.ToInt32(ico, entry + 8);
            int offset = BitConverter.ToInt32(ico, entry + 12);
            if (offset < 0 || size <= 8 || offset + size > ico.Length) continue;

            bool isPng = ico[offset] == 0x89 && ico[offset + 1] == 0x50
                      && ico[offset + 2] == 0x4E && ico[offset + 3] == 0x47;
            if (!isPng) continue;                                                // BMP entries: δεν τα μετατρέπουμε

            if (width * height > bestPixels)
            {
                bestPixels = width * height;
                best = ico.AsSpan(offset, size).ToArray();
            }
        }
        return best;
    }

    // ----------------------------------------------------------------- Windows

    /// <summary>
    /// Windows: payload.zip + τα scripts εγκατάστασης, τυλιγμένα σε ένα self-extracting exe με το
    /// IExpress (υπάρχει σε κάθε Windows — δεν απαιτείται WiX/Inno).
    /// </summary>
    private static string BuildWindowsInstaller(string payloadDir, string outputDir, string version, Target target)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows installer is packed with IExpress, so it has to be built on Windows.");

        string iexpress = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "iexpress.exe");
        if (!File.Exists(iexpress)) throw new FileNotFoundException($"IExpress not found at {iexpress}");

        string staging = Path.Combine(Path.GetTempPath(), "tm-setup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        bool keepStaging = false;
        try
        {
            // 1. payload.zip: τα αρχεία του παιχνιδιού + ο uninstaller που θα ζήσει στον φάκελο εγκατάστασης.
            Console.WriteLine("  packing payload...");
            string zipPath = Path.Combine(staging, "payload.zip");
            var files = PayloadFiles(payloadDir);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(payloadDir, file).Replace('\\', '/'),
                        CompressionLevel.Optimal);

                AddTextEntry(zip, "uninstall.ps1", Render(Template("uninstall.ps1"), version));
                AddTextEntry(zip, "uninstall.cmd",
                    "@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0uninstall.ps1\" %*\r\n");
            }
            Console.WriteLine($"  {files.Count} files -> payload.zip ({Mb(new FileInfo(zipPath).Length)})");

            // 2. Τα scripts που τρέχει το setup (Windows: CRLF υποχρεωτικά — βλ. WriteWindowsScript).
            WriteWindowsScript(Path.Combine(staging, "install.cmd"), Template("install.cmd"));
            WriteWindowsScript(Path.Combine(staging, "install.ps1"),
                Render(Template("install.ps1"), version).Replace("@EXE_NAME@", target.ExeName));

            // 3. IExpress: όλα σε ένα exe.
            string outFile = Path.Combine(outputDir, $"{AppId}-{version}-Setup-{target.Rid}.exe");
            string sed = Path.Combine(staging, "setup.sed");
            File.WriteAllText(sed, SedScript(staging, outFile, version), Encoding.ASCII);

            // Ένα φρεσκογραμμένο 40άρι zip το «κρατάει» για λίγο ο real-time scanner· αν το δώσουμε
            // στο IExpress όσο είναι ακόμη κλειδωμένο, εκείνο απλώς γυρίζει 1. Περιμένουμε να ανοίγει.
            WaitUntilUnlocked(zipPath, TimeSpan.FromMinutes(2));

            Console.WriteLine("  running iexpress...");
            // Το IExpress ΔΕΝ αφαιρεί εισαγωγικά από το όρισμα: με "διαδρομή" σε quotes γυρίζει
            // σκέτο 1. Γι' αυτό τρέχει μέσα στο staging και παίρνει σκέτο το όνομα του .sed.
            var psi = new ProcessStartInfo(iexpress, $"/N /Q {Path.GetFileName(sed)}")
            {
                UseShellExecute = false,
                WorkingDirectory = staging,
            };

            // Το IExpress σκοντάφτει περιστασιακά στο μόλις-γραμμένο payload.zip (antivirus/flush),
            // οπότε δίνουμε λίγες ευκαιρίες πριν το πούμε αποτυχία.
            int exitCode = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using var proc = Process.Start(psi)!;
                proc.WaitForExit();
                exitCode = proc.ExitCode;
                if (exitCode == 0 && File.Exists(outFile)) return outFile;

                if (attempt < 3)
                {
                    Console.WriteLine($"  iexpress returned {exitCode} - retrying ({attempt}/2)...");
                    Thread.Sleep(3000);
                }
            }

            keepStaging = true;   // άφησε τα αρχεία για να δει κανείς τι έφταιξε
            throw new InvalidOperationException($"iexpress exited with {exitCode} - see {sed}");
        }
        finally
        {
            if (keepStaging) Console.Error.WriteLine($"  (staging kept: {staging})");
            else try { Directory.Delete(staging, recursive: true); } catch (IOException) { /* temp: δεν πειράζει */ }
        }
    }

    /// <summary>
    /// Γράφει script των Windows με <b>CRLF</b> — το cmd.exe διαβάζει ένα .cmd με σκέτα LF σαν μία
    /// γραμμή και βγάζει παράλογα σφάλματα («: was unexpected at this time»).
    /// <para>BOM: το θέλει το .ps1 (αλλιώς το Windows PowerShell 5.1 διαβάζει τα ελληνικά σχόλια ως
    /// ANSI), αλλά το .cmd το σκοντάφτει (γίνεται μέρος της πρώτης εντολής).</para>
    /// </summary>
    private static void WriteWindowsScript(string path, string content)
    {
        string crlf = content.Replace("\r\n", "\n").Replace("\n", "\r\n");
        bool bom = path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, crlf, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
    }

    /// <summary>Περιμένει μέχρι το αρχείο να ανοίγει αποκλειστικά (κανείς άλλος να μην το κρατά).</summary>
    private static void WaitUntilUnlocked(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var probe = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Console.WriteLine("  waiting for the antivirus to let go of payload.zip...");
                Thread.Sleep(2000);
            }
        }
    }

    /// <summary>Βάζει ένα script στο zip με τους ίδιους κανόνες CRLF/BOM (βλ. WriteWindowsScript).</summary>
    private static void AddTextEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        bool bom = name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        using var writer = new StreamWriter(stream, new UTF8Encoding(bom));
        writer.Write(content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }

    /// <summary>
    /// Το «project» του IExpress: ποια αρχεία μπαίνουν και τι τρέχει μετά την εξαγωγή.
    /// <para>Δύο παγίδες, και οι δύο σιωπηλές:</para>
    /// <list type="bullet">
    /// <item><c>AppLaunched</c> πρέπει να δείχνει σε αρχείο <b>του πακέτου</b>. Ένα
    /// <c>cmd /c install.cmd</c> απλώς δεν εκτελείται ποτέ (το πακέτο βγαίνει, τρέχει, δεν κάνει
    /// τίποτα) — μπαίνει σκέτο <c>install.cmd</c>. Το IExpress ορίζει ήδη ως τρέχοντα φάκελο τον
    /// φάκελο εξαγωγής, οπότε το script βρίσκεται.</item>
    /// <item><c>ShowInstallProgramWindow=0</c> σημαίνει <b>ορατό</b> παράθυρο· το 1 το κρύβει
    /// (και τότε ο χρήστης δεν βλέπει ούτε πρόοδο ούτε ερωτήσεις).</item>
    /// </list>
    /// </summary>
    private static string SedScript(string staging, string outFile, string version) => $"""
        [Version]
        Class=IEXPRESS
        SEDVersion=3
        [Options]
        PackagePurpose=InstallApp
        ShowInstallProgramWindow=0
        HideExtractAnimation=1
        UseLongFileName=1
        InsideCompressed=0
        CAB_FixedSize=0
        CAB_ResvCodeSigning=0
        RebootMode=N
        InstallPrompt=%InstallPrompt%
        DisplayLicense=%DisplayLicense%
        FinishMessage=%FinishMessage%
        TargetName=%TargetName%
        FriendlyName=%FriendlyName%
        AppLaunched=%AppLaunched%
        PostInstallCmd=%PostInstallCmd%
        AdminQuietInstCmd=%AdminQuietInstCmd%
        UserQuietInstCmd=%UserQuietInstCmd%
        SourceFiles=SourceFiles
        [Strings]
        InstallPrompt=
        DisplayLicense=
        FinishMessage=
        TargetName={outFile}
        FriendlyName={AppName} {version} Setup
        AppLaunched=install.cmd
        PostInstallCmd=<None>
        AdminQuietInstCmd=
        UserQuietInstCmd=
        FILE0="install.cmd"
        FILE1="install.ps1"
        FILE2="payload.zip"
        [SourceFiles]
        SourceFiles0={staging}\
        [SourceFiles0]
        %FILE0%=
        %FILE1%=
        %FILE2%=

        """;

    // ----------------------------------------------------------------- Linux

    /// <summary>
    /// Linux: κλασικό self-extracting shell script — κείμενο εγκατάστασης, γραμμή-δείκτης, και από
    /// κάτω το tar.gz. Δουλεύει σε x64 και ARM, με ή χωρίς root.
    /// </summary>
    private static string BuildLinuxInstaller(string payloadDir, string outputDir, string version, Target target)
    {
        Console.WriteLine("  packing payload...");
        string outFile = Path.Combine(outputDir, $"{AppId}-{version}-Setup-{target.Rid}.sh");
        var files = PayloadFiles(payloadDir);
        byte[]? icon = ExtractLargestPng(IconIco());

        string header = Render(Template("linux-install.sh"), version)
            .Replace("@EXE_NAME@", target.ExeName)
            .Replace("@ARCH_LABEL@", target.ArchLabel)
            .Replace("\r\n", "\n");                                   // LF: αλλιώς δεν τρέχει σε Linux

        using (var fs = File.Create(outFile))
        {
            fs.Write(new UTF8Encoding(false).GetBytes(header));

            using var gzip = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: true);
            using var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true);
            foreach (string file in files)
            {
                string name = Path.GetRelativePath(payloadDir, file).Replace('\\', '/');
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    // Το εκτελέσιμο θέλει +x· ο installer κάνει ούτως ή άλλως chmod, αλλά ας είναι σωστό.
                    Mode = name == target.ExeName ? Executable : ReadWrite,
                    DataStream = File.OpenRead(file),
                };
                tar.WriteEntry(entry);
                entry.DataStream?.Dispose();
            }
            if (icon is not null)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "Icon.png")
                {
                    Mode = ReadWrite,
                    DataStream = new MemoryStream(icon),
                });
            }
        }

        Console.WriteLine($"  {files.Count} files + icon -> tar.gz appended to the script");
        return outFile;
    }

    // Unix permissions: 0644 και 0755.
    private const UnixFileMode ReadWrite =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode Executable = ReadWrite |
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
}
