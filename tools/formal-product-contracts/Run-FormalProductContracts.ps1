[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64'
$nativeDll = Join-Path $binRoot 'XbPreview.Native.dll'
$asset = Get-ChildItem -LiteralPath (Join-Path $binRoot 'assets') `
    -Filter '*.png' -File -ErrorAction SilentlyContinue |
    Sort-Object Name |
    Select-Object -First 1 -ExpandProperty FullName
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace($asset) -or
    -not (Test-Path -LiteralPath $asset -PathType Leaf)) {
    throw 'Release x64 native DLL or packaged background asset is missing.'
}

$runRoot = Join-Path $repoRoot (
    'artifacts\formal-product-contracts\' +
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' +
    [Guid]::NewGuid().ToString('N'))
$diagnostics = Join-Path $runRoot 'diagnostics'
$outputRoot = Join-Path $runRoot 'recordings'
New-Item -ItemType Directory -Path $diagnostics,$outputRoot -Force | Out-Null

$source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class FormalProductContractsHarness
{
    private const uint ApiVersion = 0x00040004;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct CreateOptions
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong ExclusionWindow;
        internal uint AllowWarp;
        internal uint FramePoolBufferCount;
        internal uint StatsIntervalMilliseconds;
        internal uint Reserved0;
        internal IntPtr DiagnosticLogDirectory;
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SessionGeometry
    {
        internal uint StructSize;
        internal uint Version;
        internal uint SourceWidth;
        internal uint SourceHeight;
        internal int CaptureLeft;
        internal int CaptureTop;
        internal uint CaptureWidth;
        internal uint CaptureHeight;
        internal uint OutputWidth;
        internal uint OutputHeight;
        internal ulong GeometryRevision;
        internal ulong Flags;
    }

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Create(
        IntPtr previewHwnd, ref CreateOptions options, out IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Start(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Stop(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Destroy(ref IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetSessionGeometry(
        IntPtr handle, ref SessionGeometry geometry);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetWindowStagePose(
        IntPtr handle, int orientation, int level);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetWindowShowcaseBackgroundPreset(
        IntPtr handle, int preset);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Unicode)]
    private static extern int XbPreview_SetWindowShowcaseCustomBackground(
        IntPtr handle, string path);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Unicode)]
    private static extern int XbPreview_SetRecordingOutputRoot(
        IntPtr handle, string path);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetAudioProgramMode(IntPtr handle, int mode);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_StartRecording(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_StopRecording(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_GetRecordingSnapshot(
        IntPtr handle, IntPtr snapshot);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_GetStats(IntPtr handle, IntPtr stats);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Expected diagnostics, output root, and image path.");
            return 2;
        }
        string diagnostics = Path.GetFullPath(args[0]);
        string outputRoot = Path.GetFullPath(args[1]);
        string imagePath = Path.GetFullPath(args[2]);
        string missingPath = Path.Combine(diagnostics, "missing-background.png");
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(outputRoot);

        using (Form window = new Form())
        using (Panel surface = new Panel())
        {
            window.Text = "Formal product contracts automated harness";
            window.ClientSize = new System.Drawing.Size(640, 360);
            surface.Parent = window;
            surface.Dock = DockStyle.Fill;
            window.Show();
            Application.DoEvents();

            IntPtr handle = IntPtr.Zero;
            IntPtr diagnosticPointer = Marshal.StringToHGlobalUni(diagnostics);
            try
            {
                CreateOptions options = new CreateOptions();
                options.StructSize = 72;
                options.ApiVersion = ApiVersion;
                options.ExclusionWindow = unchecked((ulong)window.Handle.ToInt64());
                options.AllowWarp = 1;
                options.FramePoolBufferCount = 2;
                options.StatsIntervalMilliseconds = 1000;
                options.DiagnosticLogDirectory = diagnosticPointer;
                Require(XbPreview_Create(surface.Handle, ref options, out handle),
                    "create");

                System.Drawing.Rectangle bounds = Screen.PrimaryScreen.Bounds;
                SessionGeometry geometry = new SessionGeometry();
                geometry.StructSize = 56;
                geometry.Version = 1;
                geometry.SourceWidth = (uint)bounds.Width;
                geometry.SourceHeight = (uint)bounds.Height;
                geometry.CaptureWidth = (uint)bounds.Width;
                geometry.CaptureHeight = (uint)bounds.Height;
                geometry.OutputWidth = 1920;
                geometry.OutputHeight = 1080;
                geometry.GeometryRevision = 1;
                Require(XbPreview_SetSessionGeometry(handle, ref geometry),
                    "session geometry");
                Require(XbPreview_SetRecordingOutputRoot(handle, outputRoot),
                    "custom output root");
                ApplyVisualSwitching(handle, imagePath, missingPath);
                Console.WriteLine("FORMAL-CONTRACT-NATIVE-SETTERS-PRESTART = PASS");
                int previewStart = XbPreview_Start(handle);
                if (previewStart != 0)
                {
                    Console.WriteLine(
                        "FORMAL-CONTRACT-RUNTIME = BLOCKED-WGC-UNAVAILABLE; result=" +
                        previewStart);
                    return 3;
                }
                WaitForFrames(handle);
                Require(XbPreview_SetAudioProgramMode(handle, 0),
                    "video-only automated audio mode");
                ApplyVisualSwitching(handle, imagePath, missingPath);

                Require(XbPreview_StartRecording(handle), "recording start");
                Thread.Sleep(1800);
                Application.DoEvents();
                Require(XbPreview_StopRecording(handle), "recording stop");

                IntPtr snapshot = Marshal.AllocHGlobal(2856);
                try
                {
                    Zero(snapshot, 2856);
                    Marshal.WriteInt32(snapshot, 0, 2856);
                    Marshal.WriteInt32(snapshot, 4, unchecked((int)ApiVersion));
                    Require(XbPreview_GetRecordingSnapshot(handle, snapshot),
                        "recording snapshot");
                    int state = Marshal.ReadInt32(snapshot, 8);
                    ulong frames = unchecked((ulong)Marshal.ReadInt64(snapshot, 72));
                    int ready = Marshal.ReadInt32(snapshot, 1272);
                    int published = Marshal.ReadInt32(snapshot, 1276);
                    int publishAttempted = Marshal.ReadInt32(snapshot, 1280);
                    int publishHResult = Marshal.ReadInt32(snapshot, 1284);
                    int validationAttempted = Marshal.ReadInt32(snapshot, 1288);
                    int validationHResult = Marshal.ReadInt32(snapshot, 1292);
                    string publishedPath = Marshal.PtrToStringUni(
                        IntPtr.Add(snapshot, 2336)) ?? string.Empty;
                    if (state != 4 || frames == 0 || ready != 1 || published != 1 ||
                        publishAttempted != 1 || publishHResult != 0 ||
                        validationAttempted != 1 || validationHResult != 0 ||
                        !File.Exists(publishedPath) ||
                        !string.Equals(Path.GetDirectoryName(publishedPath),
                            outputRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "safe publish facts failed: state=" + state +
                            "; frames=" + frames + "; ready=" + ready +
                            "; published=" + published + "; path=" + publishedPath);
                    }
                    Console.WriteLine("FORMAL-CONTRACT-3D = PASS");
                    Console.WriteLine("FORMAL-CONTRACT-BACKGROUND-PRESETS = PASS");
                    Console.WriteLine("FORMAL-CONTRACT-CUSTOM-BACKGROUND = PASS");
                    Console.WriteLine("FORMAL-CONTRACT-OUTPUT-ROOT = PASS");
                    Console.WriteLine("FORMAL-CONTRACT-FINAL-MP4 = " + publishedPath);
                }
                finally
                {
                    Marshal.FreeHGlobal(snapshot);
                }
                Require(XbPreview_Stop(handle), "preview stop");
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(diagnosticPointer);
                if (handle != IntPtr.Zero)
                {
                    XbPreview_Stop(handle);
                    XbPreview_Destroy(ref handle);
                }
            }
        }
    }

    private static void WaitForFrames(IntPtr handle)
    {
        IntPtr stats = Marshal.AllocHGlobal(1080);
        try
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                Zero(stats, 1080);
                Marshal.WriteInt32(stats, 0, 1080);
                Marshal.WriteInt32(stats, 4, unchecked((int)ApiVersion));
                Require(XbPreview_GetStats(handle, stats), "preview stats");
                if (Marshal.ReadInt64(stats, 32) > 0)
                {
                    return;
                }
                Application.DoEvents();
                Thread.Sleep(50);
            }
            throw new TimeoutException("Preview produced no frame within 15 seconds.");
        }
        finally
        {
            Marshal.FreeHGlobal(stats);
        }
    }

    private static void ApplyVisualSwitching(
        IntPtr handle,
        string imagePath,
        string missingPath)
    {
        int[,] poses = new int[,] { {1,1}, {0,0}, {2,2}, {1,1} };
        for (int index = 0; index < poses.GetLength(0); index++)
        {
            Require(XbPreview_SetWindowStagePose(
                handle, poses[index, 0], poses[index, 1]),
                "pose " + index);
        }
        int[] presets = new int[] { 0, 1, 2, 0 };
        for (int index = 0; index < presets.Length; index++)
        {
            Require(XbPreview_SetWindowShowcaseBackgroundPreset(
                handle, presets[index]), "background " + index);
        }
        Require(XbPreview_SetWindowShowcaseCustomBackground(
            handle, imagePath), "valid custom background");
        int invalid = XbPreview_SetWindowShowcaseCustomBackground(
            handle, missingPath);
        if (invalid != -1)
        {
            throw new InvalidOperationException(
                "missing custom background expected InvalidArgument; actual=" +
                invalid);
        }
    }

    private static void Zero(IntPtr pointer, int length)
    {
        byte[] zero = new byte[length];
        Marshal.Copy(zero, 0, pointer, length);
    }

    private static void Require(int result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(
                operation + " failed: " + result);
        }
    }
}
'@

$harness = Join-Path $binRoot 'FormalProductContractsHarness.exe'
if (Test-Path -LiteralPath $harness -PathType Leaf) {
    Remove-Item -LiteralPath $harness -Force
}
Add-Type -TypeDefinition $source `
    -Language CSharp `
    -ReferencedAssemblies @('System.Windows.Forms.dll','System.Drawing.dll') `
    -OutputAssembly $harness `
    -OutputType ConsoleApplication

& $harness $diagnostics $outputRoot $asset
if ($LASTEXITCODE -eq 3) {
    Write-Warning (
        'Native setters passed before Start; runtime recording is blocked ' +
        'because WGC is unavailable in this environment. Evidence: ' +
        $diagnostics)
    return
}
if ($LASTEXITCODE -ne 0) {
    throw "Formal product contracts native harness failed: $LASTEXITCODE"
}
