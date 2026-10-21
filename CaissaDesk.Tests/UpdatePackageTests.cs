using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using CaissaDesk.Core.Updates;

namespace CaissaDesk.Tests;

/// <summary>Archive extraction safety, helper script generation (and execution on Linux), install checks.</summary>
public sealed class UpdatePackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caissadesk-update-tests-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("#!/bin/sh\necho new\n");

    // ---------------------------------------------------------------- tar.gz

    internal static void WriteTarGz(string path, params TarEntry[] entries)
    {
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        using var tar = new TarWriter(gz, TarEntryFormat.Pax);
        foreach (var e in entries) tar.WriteEntry(e);
    }

    internal static PaxTarEntry Entry(string name, byte[] content) =>
        new(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) };

    private string Archive(string name = "a.tar.gz") => Path.Combine(_dir, name);
    private string Out => Path.Combine(_dir, "out");

    [Theory]
    [InlineData("CaissaDesk")]
    [InlineData("./CaissaDesk")]
    public async Task TarGz_ExtractsTheExecutable(string entryName)
    {
        WriteTarGz(Archive(), new PaxTarEntry(TarEntryType.Directory, "./"), Entry(entryName, Payload));

        string exe = await UpdatePackageExtractor.ExtractTarGzAsync(Archive(), "CaissaDesk", Out, 1 << 20);

        Assert.Equal(Path.Combine(Out, "CaissaDesk"), exe);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(exe));
    }

    public static TheoryData<string, TarEntry[]> MaliciousTarArchives() => new()
    {
        { "traversal", [Entry("../CaissaDesk", Payload)] },
        { "nested traversal", [Entry("./x/../../CaissaDesk", Payload)] },
        { "absolute", [Entry("/tmp/CaissaDesk", Payload)] },
        { "extra file", [Entry("CaissaDesk", Payload), Entry("libevil.so", Payload)] },
        { "exe in subfolder", [Entry("bin/CaissaDesk", Payload)] },
        { "duplicate", [Entry("CaissaDesk", Payload), Entry("CaissaDesk", Payload)] },
        { "symlink", [new PaxTarEntry(TarEntryType.SymbolicLink, "CaissaDesk") { LinkName = "/etc/passwd" }] },
        { "hardlink", [new PaxTarEntry(TarEntryType.HardLink, "CaissaDesk") { LinkName = "/etc/passwd" }] },
        { "fifo", [new PaxTarEntry(TarEntryType.Fifo, "CaissaDesk")] },
        { "wrong case", [Entry("caissadesk", Payload)] },
        { "missing", [new PaxTarEntry(TarEntryType.Directory, "docs/")] },
        { "empty exe", [Entry("CaissaDesk", [])] }
    };

    [Theory]
    [MemberData(nameof(MaliciousTarArchives))]
    public async Task TarGz_RejectsUnsafeOrUnexpectedArchives(string label, TarEntry[] entries)
    {
        WriteTarGz(Archive(), entries);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractTarGzAsync(Archive(), "CaissaDesk", Out, 1 << 20));

        Assert.True(ex.Failure == UpdateFailure.InvalidArchive, label);
        Assert.False(File.Exists(Path.Combine(_dir, "CaissaDesk")), "nothing may be written outside the output folder");
        Assert.False(File.Exists(Path.Combine(Out, "CaissaDesk")), "a rejected archive leaves no executable behind");
    }

    [Fact]
    public async Task TarGz_RejectsOversizedPayload()
    {
        WriteTarGz(Archive(), Entry("CaissaDesk", new byte[4096]));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractTarGzAsync(Archive(), "CaissaDesk", Out, 1024));

        Assert.Equal(UpdateFailure.InvalidArchive, ex.Failure);
        Assert.False(File.Exists(Path.Combine(Out, "CaissaDesk")));
    }

    [Fact]
    public async Task TarGz_RejectsCorruptArchive()
    {
        await File.WriteAllBytesAsync(Archive(), [0x1f, 0x8b, 8, 0, 1, 2, 3, 4, 5]);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractTarGzAsync(Archive(), "CaissaDesk", Out, 1 << 20));
        Assert.Equal(UpdateFailure.InvalidArchive, ex.Failure);
    }

    // ---------------------------------------------------------------- zip

    private void WriteZip(string path, params (string Name, byte[] Content, int UnixMode)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content, mode) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (mode != 0) entry.ExternalAttributes = mode << 16;
            using var s = entry.Open();
            s.Write(content);
        }
    }

    [Fact]
    public async Task Zip_ExtractsTheExecutable()
    {
        WriteZip(Archive("a.zip"), ("CaissaDesk.exe", Payload, 0));

        string exe = await UpdatePackageExtractor.ExtractZipAsync(Archive("a.zip"), "CaissaDesk.exe", Out, 1 << 20);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(exe));
    }

    [Theory]
    [InlineData("../CaissaDesk.exe", 0)]
    [InlineData("..\\CaissaDesk.exe", 0)]
    [InlineData("C:/Windows/CaissaDesk.exe", 0)]
    [InlineData("/CaissaDesk.exe", 0)]
    [InlineData("other.dll", 0)]
    [InlineData("CaissaDesk.exe", 0xA1FF)] // symlink (S_IFLNK | 0777)
    public async Task Zip_RejectsUnsafeEntries(string name, int unixMode)
    {
        WriteZip(Archive("a.zip"), (name, Payload, unixMode));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractZipAsync(Archive("a.zip"), "CaissaDesk.exe", Out, 1 << 20));

        Assert.Equal(UpdateFailure.InvalidArchive, ex.Failure);
        Assert.False(File.Exists(Path.Combine(Out, "CaissaDesk.exe")));
    }

    [Fact]
    public async Task Zip_RejectsExtraFilesAndDuplicates()
    {
        WriteZip(Archive("x.zip"), ("CaissaDesk.exe", Payload, 0), ("readme.txt", Payload, 0));
        WriteZip(Archive("d.zip"), ("CaissaDesk.exe", Payload, 0), ("CaissaDesk.exe", Payload, 0));

        await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractZipAsync(Archive("x.zip"), "CaissaDesk.exe", Out, 1 << 20));
        await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageExtractor.ExtractZipAsync(Archive("d.zip"), "CaissaDesk.exe", Path.Combine(_dir, "out2"), 1 << 20));
    }

    // ---------------------------------------------------------------- scripts

    private UpdateScriptPlan Plan(string root, int pid = 4242) => new(
        pid,
        Path.Combine(root, "staging", "payload", "CaissaDesk"),
        Path.Combine(root, "install", "CaissaDesk"),
        Path.Combine(root, "staging"),
        Path.Combine(root, "update.log"));

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData("it's")]
    [InlineData("$HOME `id` $(id) \"q\" \\ %PATH% !x ; & | *")]
    public void ShellQuote_RoundTripsThroughSh(string value)
    {
        if (OperatingSystem.IsWindows()) return;

        var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("printf '%s' " + UpdateScriptBuilder.ShellQuote(value));
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        Assert.Equal(value, output);
    }

    [Fact]
    public void ShellScript_EmbedsPathsAsSingleQuotedLiterals()
    {
        string root = Path.Combine(_dir, "we'ird $dir `x`");
        var script = UpdateScriptBuilder.BuildShellScript(Plan(root));

        Assert.Equal("apply-update.sh", script.FileName);
        Assert.StartsWith("#!/bin/sh\n", script.Content);
        Assert.DoesNotContain('\r', script.Content);
        Assert.Contains("PID=4242\n", script.Content);
        Assert.Contains("TARGET=" + UpdateScriptBuilder.ShellQuote(Path.Combine(root, "install", "CaissaDesk")) + "\n", script.Content);
        Assert.Contains("BACKUP=" + UpdateScriptBuilder.ShellQuote(Path.Combine(root, "install", "CaissaDesk.bak")) + "\n", script.Content);
        Assert.Empty(script.Environment);
    }

    [Fact]
    public void WindowsScript_TakesAllPathsFromEnvironment()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\Users\Tomáš (Work) & 100%\AppData" : "/home/to m%PATH%&!/x";
        var plan = Plan(root);
        var script = UpdateScriptBuilder.BuildWindowsScript(plan);

        Assert.Equal("apply-update.cmd", script.FileName);
        Assert.DoesNotContain(root, script.Content);
        Assert.DoesNotContain("\n", script.Content.Replace("\r\n", ""));
        Assert.Contains("DisableDelayedExpansion", script.Content);
        Assert.Contains("\"%CAISSADESK_UPDATE_TARGET%\"", script.Content);
        Assert.True(script.Content.All(c => c < 128), "the batch file must be plain ASCII (cmd reads it in the OEM code page)");

        Assert.Equal("4242", script.Environment["CAISSADESK_UPDATE_PID"]);
        Assert.Equal(plan.NewExecutable, script.Environment["CAISSADESK_UPDATE_NEW"]);
        Assert.Equal(plan.TargetExecutable, script.Environment["CAISSADESK_UPDATE_TARGET"]);
        Assert.Equal(plan.BackupExecutable, script.Environment["CAISSADESK_UPDATE_BACKUP"]);
        Assert.Equal(plan.StagingDirectory, script.Environment["CAISSADESK_UPDATE_STAGING"]);
    }

    [Fact]
    public void Scripts_RejectRelativeOrControlCharacterPaths()
    {
        var relative = Plan(_dir) with { TargetExecutable = "CaissaDesk" };
        var newline = Plan(_dir) with { StagingDirectory = Path.Combine(_dir, "a\nb") };
        var quote = Plan(_dir) with { TargetExecutable = Path.Combine(_dir, "a\"b", "CaissaDesk") };

        Assert.Throws<ArgumentException>(() => UpdateScriptBuilder.BuildShellScript(relative));
        Assert.Throws<ArgumentException>(() => UpdateScriptBuilder.BuildShellScript(newline));
        Assert.Throws<ArgumentException>(() => UpdateScriptBuilder.BuildWindowsScript(quote));
        Assert.Throws<ArgumentException>(() => UpdateScriptBuilder.BuildShellScript(Plan(_dir, pid: 0)));
    }

    [Fact]
    public void WindowsLauncher_RefusesPercentInScriptPath()
    {
        var ex = Assert.Throws<UpdateException>(() => UpdateService.CreateScriptStartInfo(@"C:\Users\a%b%\apply-update.cmd", isWindows: true, @"C:\"));
        Assert.Equal(UpdateFailure.InstallNotSupported, ex.Failure);

        var sh = UpdateService.CreateScriptStartInfo("/x y/apply-update.sh", isWindows: false, "/");
        Assert.Equal("/bin/sh", sh.FileName);
        Assert.Equal(["/x y/apply-update.sh"], sh.ArgumentList);
        Assert.False(sh.UseShellExecute);
    }

    /// <summary>Creates a fake install whose "executable" is a shell script writing <paramref name="marker"/>.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void WriteFakeExe(string path, string marker, string label)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"#!/bin/sh\nprintf '%s' {UpdateScriptBuilder.ShellQuote(label)} > {UpdateScriptBuilder.ShellQuote(marker)}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static int ExitedPid()
    {
        using var p = Process.Start(new ProcessStartInfo("/bin/sh", "-c true") { UseShellExecute = false })!;
        p.WaitForExit();
        return p.Id;
    }

    private static int RunScript(string scriptPath)
    {
        var psi = UpdateService.CreateScriptStartInfo(scriptPath, isWindows: false, Path.GetDirectoryName(scriptPath)!);
        using var p = Process.Start(psi)!;
        Assert.True(p.WaitForExit(30_000), "script did not finish");
        return p.ExitCode;
    }

    private static string WaitForFile(string path)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return File.ReadAllText(path);
            Thread.Sleep(50);
        }
        throw new TimeoutException($"{path} was not written");
    }

    [Fact]
    public void ShellScript_ReplacesExecutable_KeepsBackup_Restarts_AndCleansUp()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = Path.Combine(_dir, "it's a $dir");
        string marker = Path.Combine(_dir, "started.txt");
        var plan = Plan(root, ExitedPid());
        WriteFakeExe(plan.TargetExecutable, marker, "old");
        WriteFakeExe(plan.NewExecutable, marker, "new");
        File.SetUnixFileMode(plan.NewExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite); // the script must chmod
        string newContent = File.ReadAllText(plan.NewExecutable);

        var script = UpdateScriptBuilder.BuildShellScript(plan);
        string scriptPath = Path.Combine(plan.StagingDirectory, script.FileName);
        File.WriteAllText(scriptPath, script.Content);

        Assert.Equal(0, RunScript(scriptPath));

        Assert.Equal("new", WaitForFile(marker));
        Assert.Equal(newContent, File.ReadAllText(plan.TargetExecutable));
        Assert.True(File.GetUnixFileMode(plan.TargetExecutable).HasFlag(UnixFileMode.OtherExecute));
        Assert.Contains("printf '%s' 'old'", File.ReadAllText(plan.BackupExecutable));
        Assert.False(File.Exists(plan.TemporaryExecutable));
        Assert.False(Directory.Exists(plan.StagingDirectory), "staging folder (with the script) is removed");
        Assert.Contains("update: installed", File.ReadAllText(plan.LogFile));
    }

    [Fact]
    public void ShellScript_WhenCopyFails_LeavesOldExecutableAndRestartsIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = Path.Combine(_dir, "fail");
        string marker = Path.Combine(_dir, "started-old.txt");
        var plan = Plan(root, ExitedPid());
        WriteFakeExe(plan.TargetExecutable, marker, "old");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.NewExecutable)!); // staged exe missing
        string oldContent = File.ReadAllText(plan.TargetExecutable);

        var script = UpdateScriptBuilder.BuildShellScript(plan);
        string scriptPath = Path.Combine(plan.StagingDirectory, script.FileName);
        File.WriteAllText(scriptPath, script.Content);

        Assert.Equal(1, RunScript(scriptPath));

        Assert.Equal("old", WaitForFile(marker));
        Assert.Equal(oldContent, File.ReadAllText(plan.TargetExecutable));
        Assert.False(File.Exists(plan.BackupExecutable));
        Assert.False(File.Exists(plan.TemporaryExecutable));
    }

    [Fact]
    public void ShellScript_WaitsForTheProcessToExit()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var sleeper = Process.Start(new ProcessStartInfo("/bin/sh", "-c \"sleep 2\"") { UseShellExecute = false })!;
        string root = Path.Combine(_dir, "wait");
        string marker = Path.Combine(_dir, "started-wait.txt");
        var plan = Plan(root, sleeper.Id);
        WriteFakeExe(plan.TargetExecutable, marker, "old");
        WriteFakeExe(plan.NewExecutable, marker, "new");

        var script = UpdateScriptBuilder.BuildShellScript(plan);
        string scriptPath = Path.Combine(plan.StagingDirectory, script.FileName);
        File.WriteAllText(scriptPath, script.Content);

        var sw = Stopwatch.StartNew();
        var psi = UpdateService.CreateScriptStartInfo(scriptPath, isWindows: false, plan.StagingDirectory);
        using var p = Process.Start(psi)!;
        // Reap the sleeper as soon as it exits so kill -0 stops seeing a zombie
        sleeper.WaitForExit();
        Assert.True(p.WaitForExit(30_000));

        Assert.Equal(0, p.ExitCode);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.5), $"script finished after {sw.Elapsed} while the process was still running");
        Assert.Equal("new", WaitForFile(marker));
    }

    // ---------------------------------------------------------------- install checks

    private static readonly UpdatePlatform Linux = new("CaissaDesk-linux-x64.tar.gz", "CaissaDesk", ArchiveKind.TarGz);
    private static readonly UpdatePlatform Windows = new("CaissaDesk-win-x64.zip", "CaissaDesk.exe", ArchiveKind.Zip);

    [Theory]
    [InlineData("/opt/caissadesk/CaissaDesk", null, false, true, UpdateInstallBlocker.None)]
    [InlineData("/opt/caissadesk/CaissaDesk", "/home/u/CaissaDesk.AppImage", false, true, UpdateInstallBlocker.AppImage)]
    [InlineData("/usr/share/dotnet/dotnet", null, false, true, UpdateInstallBlocker.DevelopmentBuild)]
    [InlineData("/src/CaissaDesk/bin/Debug/net10.0/CaissaDesk", null, true, true, UpdateInstallBlocker.DevelopmentBuild)]
    [InlineData("/usr/bin/CaissaDesk", null, false, false, UpdateInstallBlocker.DirectoryNotWritable)]
    [InlineData(null, null, false, true, UpdateInstallBlocker.UnknownExecutable)]
    [InlineData("CaissaDesk", null, false, true, UpdateInstallBlocker.UnknownExecutable)]
    public void Installability_Linux(string? processPath, string? appImage, bool dllNextToExe, bool writable, UpdateInstallBlocker expected)
    {
        if (OperatingSystem.IsWindows()) return; // Unix-style absolute paths

        var host = new UpdateHostInfo(processPath, appImage, Linux, IsWindows: false);
        var blocker = UpdateInstallability.Evaluate(host, p => dllNextToExe && p.EndsWith("CaissaDesk.dll", StringComparison.Ordinal), _ => writable);

        Assert.Equal(expected, blocker);
    }

    [Fact]
    public void Installability_UnsupportedPlatform_AndWindowsNameIsCaseInsensitive()
    {
        Assert.Equal(UpdateInstallBlocker.UnsupportedPlatform,
            UpdateInstallability.Evaluate(new UpdateHostInfo("/opt/CaissaDesk", null, null, false), _ => false, _ => true));

        string exe = OperatingSystem.IsWindows() ? @"C:\Apps\CaissaDesk\caissadesk.EXE" : "/apps/caissadesk.EXE";
        Assert.Equal(UpdateInstallBlocker.None,
            UpdateInstallability.Evaluate(new UpdateHostInfo(exe, null, Windows, IsWindows: true), _ => false, _ => true));
    }

    [Fact]
    public void IsDirectoryWritable_ProbesWithoutLeavingFiles()
    {
        Assert.True(UpdateInstallability.IsDirectoryWritable(_dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
        Assert.False(UpdateInstallability.IsDirectoryWritable(Path.Combine(_dir, "missing")));
    }
}
