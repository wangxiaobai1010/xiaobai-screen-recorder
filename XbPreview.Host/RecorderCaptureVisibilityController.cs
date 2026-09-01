using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal enum RecorderCaptureVisibilityFailure
{
    None = 0,
    SetAffinityFailed = 1,
    ReadAffinityFailed = 2,
    AffinityReadbackMismatch = 3,
}

internal enum RecorderCaptureWindowRole
{
    MainRecorderWindow = 0,
    FloatingTray = 1,
    FailClosedExcluded = 2,
}

internal enum RecorderCapturePhase
{
    Idle = 0,
    Starting = 1,
    Recording = 2,
    Paused = 3,
    Stopping = 4,
    Unstable = 5,
}

internal readonly record struct RecorderCaptureVisibilityResult(
    bool Succeeded,
    bool TrayInFrame,
    RecorderCaptureVisibilityFailure Failure,
    nint WindowHandle,
    int WindowsErrorCode,
    uint AppliedAffinity)
{
    internal static RecorderCaptureVisibilityResult Success(
        bool trayInFrame) => new(
            true,
            trayInFrame,
            RecorderCaptureVisibilityFailure.None,
            nint.Zero,
            0,
            trayInFrame
                ? WindowDisplayAffinity.AllowCapture
                : WindowDisplayAffinity.ExcludeFromCapture);
}

internal interface IRecorderCaptureAffinityPlatform
{
    WindowDisplayAffinityResult TrySet(nint windowHandle, uint affinity);

    WindowDisplayAffinityResult TryRead(
        nint windowHandle,
        out uint affinity);
}

internal sealed class Win32RecorderCaptureAffinityPlatform :
    IRecorderCaptureAffinityPlatform
{
    public WindowDisplayAffinityResult TrySet(
        nint windowHandle,
        uint affinity) => WindowDisplayAffinity.TrySet(windowHandle, affinity);

    public WindowDisplayAffinityResult TryRead(
        nint windowHandle,
        out uint affinity) =>
        WindowDisplayAffinity.TryRead(windowHandle, out affinity);
}

/// <summary>
/// Owns capture visibility for recorder top-level windows. TrayInFrame is the
/// single user intent; effective affinity is derived from that intent, the
/// current recording phase, and each window's role.
/// </summary>
internal sealed class RecorderCaptureVisibilityController : IDisposable
{
    private const uint GwOwner = 4;

    private readonly Dictionary<Form, RecorderCaptureWindowRole>
        _registeredWindows = [];
    private readonly IRecorderCaptureAffinityPlatform _platform;
    private bool _trayInFrame;
    private RecorderCapturePhase _phase = RecorderCapturePhase.Idle;
    private bool _disposed;

    internal RecorderCaptureVisibilityController(
        IRecorderCaptureAffinityPlatform? platform = null)
    {
        _platform = platform ?? new Win32RecorderCaptureAffinityPlatform();
        LastResult = RecorderCaptureVisibilityResult.Success(
            trayInFrame: false);
    }

    internal event EventHandler? StateChanged;

    internal bool TrayInFrame => _trayInFrame;

    internal RecorderCapturePhase Phase => _phase;

    internal RecorderCaptureVisibilityResult LastResult { get; private set; }

    internal IDisposable RegisterTopLevelWindow(
        Form window,
        RecorderCaptureWindowRole role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        if (!_registeredWindows.TryAdd(window, role))
        {
            throw new InvalidOperationException(
                "The recorder window is already registered.");
        }

        window.HandleCreated += OnWindowHandleCreated;
        window.HandleDestroyed += OnWindowHandleDestroyed;
        if (window.IsHandleCreated)
        {
            ApplyCurrentPolicyAfterHandleCreated();
        }

        return new WindowRegistration(this, window);
    }

    internal RecorderCaptureVisibilityResult TrySetTrayInFrame(
        bool trayInFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ApplyTrayInFrameIntent(trayInFrame);
    }

    internal RecorderCaptureVisibilityResult TrySetRecordingPhase(
        RecorderCapturePhase phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        if (_phase == phase)
        {
            return LastResult;
        }

        // Commit the phase before applying affinity. If verification fails,
        // newly created HWNDs must inherit the new fail-closed phase instead
        // of reverting to an unsafe Idle policy.
        _phase = phase;
        return ApplyCurrentIntent();
    }

    /// <summary>
    /// Revalidates registered windows and newly opened owned popups. Popup
    /// affinity follows a reliably resolved registered owner role; unresolved
    /// process windows are never treated as floating trays.
    /// </summary>
    internal RecorderCaptureVisibilityResult TryRefreshTopLevelWindows()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ApplyCurrentIntent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Form window in _registeredWindows.Keys.ToArray())
        {
            Detach(window);
        }
        _registeredWindows.Clear();
    }

    private RecorderCaptureVisibilityResult ApplyTrayInFrameIntent(
        bool trayInFrame)
    {
        IReadOnlyList<PolicyWindow> windows = EnumeratePolicyWindows();
        RecorderCaptureVisibilityResult result = ApplyPolicy(
            windows,
            trayInFrame,
            _phase);
        if (result.Succeeded)
        {
            _trayInFrame = trayInFrame;
            LastResult = RecorderCaptureVisibilityResult.Success(trayInFrame);
        }
        else
        {
            // Keep the previously committed single user intent. A failed
            // transition must not silently reset TrayInFrame, especially at
            // the recording-start boundary.
            BestEffortExclude(windows);
            LastResult = result with { TrayInFrame = _trayInFrame };
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return LastResult;
    }

    private RecorderCaptureVisibilityResult ApplyCurrentIntent()
    {
        IReadOnlyList<PolicyWindow> windows = EnumeratePolicyWindows();
        RecorderCaptureVisibilityResult result = ApplyPolicy(
            windows,
            _trayInFrame,
            _phase);
        if (result.Succeeded)
        {
            LastResult = RecorderCaptureVisibilityResult.Success(
                _trayInFrame);
        }
        else
        {
            BestEffortExclude(windows);
            LastResult = result with { TrayInFrame = _trayInFrame };
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return LastResult;
    }

    private RecorderCaptureVisibilityResult ApplyPolicy(
        IEnumerable<PolicyWindow> windows,
        bool trayInFrame,
        RecorderCapturePhase phase)
    {
        // Main and unresolved HWNDs are always verified before a floating tray
        // may become capturable. This ordering is the recording-start safety
        // boundary that prevents preview recursion.
        foreach (PolicyWindow window in windows
                     .OrderBy(static window => window.Role ==
                         RecorderCaptureWindowRole.FloatingTray ? 1 : 0)
                     .ThenBy(static window => window.Role)
                     .ThenBy(static window => window.Handle))
        {
            uint expectedAffinity = GetExpectedAffinity(
                window.Role,
                phase,
                trayInFrame);
            RecorderCaptureVisibilityResult result = ApplyAndVerify(
                window.Handle,
                expectedAffinity,
                trayInFrame);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return RecorderCaptureVisibilityResult.Success(trayInFrame);
    }

    internal static uint GetExpectedAffinity(
        RecorderCaptureWindowRole role,
        RecorderCapturePhase phase,
        bool trayInFrame)
    {
        if (!trayInFrame ||
            role == RecorderCaptureWindowRole.FailClosedExcluded)
        {
            return WindowDisplayAffinity.ExcludeFromCapture;
        }

        if (role == RecorderCaptureWindowRole.FloatingTray)
        {
            return WindowDisplayAffinity.AllowCapture;
        }

        return phase == RecorderCapturePhase.Idle
            ? WindowDisplayAffinity.AllowCapture
            : WindowDisplayAffinity.ExcludeFromCapture;
    }

    private void BestEffortExclude(IEnumerable<PolicyWindow> windows)
    {
        foreach (PolicyWindow window in windows)
        {
            _ = _platform.TrySet(
                window.Handle,
                WindowDisplayAffinity.ExcludeFromCapture);
            _ = _platform.TryRead(window.Handle, out _);
        }
    }

    private IReadOnlyList<PolicyWindow> EnumeratePolicyWindows()
    {
        Dictionary<nint, RecorderCaptureWindowRole> registeredHandles =
            _registeredWindows
                .Where(static pair =>
                    !pair.Key.IsDisposed && pair.Key.IsHandleCreated)
                .ToDictionary(
                    static pair => pair.Key.Handle,
                    static pair => pair.Value);
        if (registeredHandles.Count == 0)
        {
            return [];
        }

        Dictionary<nint, RecorderCaptureWindowRole> policyWindows =
            new(registeredHandles);
        uint currentProcessId = unchecked((uint)Environment.ProcessId);
        _ = EnumWindows(
            (windowHandle, parameter) =>
            {
                _ = GetWindowThreadProcessId(windowHandle, out uint processId);
                if (processId != currentProcessId)
                {
                    return true;
                }

                if (TryResolveRegisteredOwnerRole(
                    windowHandle,
                    registeredHandles,
                    out RecorderCaptureWindowRole role))
                {
                    policyWindows[windowHandle] = role;
                }
                else if (GetWindow(windowHandle, GwOwner) != nint.Zero)
                {
                    // An owned popup that cannot be rooted in a registered
                    // role is never allowed to inherit floating-tray capture.
                    policyWindows[windowHandle] =
                        RecorderCaptureWindowRole.FailClosedExcluded;
                }
                return true;
            },
            nint.Zero);
        return policyWindows
            .Select(static pair => new PolicyWindow(pair.Key, pair.Value))
            .OrderBy(static window => window.Role)
            .ThenBy(static window => window.Handle)
            .ToArray();
    }

    private static bool TryResolveRegisteredOwnerRole(
        nint windowHandle,
        IReadOnlyDictionary<nint, RecorderCaptureWindowRole>
            registeredHandles,
        out RecorderCaptureWindowRole role)
    {
        nint current = windowHandle;
        for (int depth = 0; depth < 32 && current != nint.Zero; ++depth)
        {
            if (registeredHandles.TryGetValue(current, out role))
            {
                return true;
            }
            current = GetWindow(current, GwOwner);
        }

        role = RecorderCaptureWindowRole.FailClosedExcluded;
        return false;
    }

    private RecorderCaptureVisibilityResult ApplyAndVerify(
        nint windowHandle,
        uint expectedAffinity,
        bool trayInFrame)
    {
        WindowDisplayAffinityResult set = _platform.TrySet(
            windowHandle,
            expectedAffinity);
        if (!set.Succeeded)
        {
            return new RecorderCaptureVisibilityResult(
                false,
                trayInFrame,
                RecorderCaptureVisibilityFailure.SetAffinityFailed,
                windowHandle,
                set.WindowsErrorCode,
                0);
        }

        WindowDisplayAffinityResult read = _platform.TryRead(
            windowHandle,
            out uint appliedAffinity);
        if (!read.Succeeded)
        {
            return new RecorderCaptureVisibilityResult(
                false,
                trayInFrame,
                RecorderCaptureVisibilityFailure.ReadAffinityFailed,
                windowHandle,
                read.WindowsErrorCode,
                appliedAffinity);
        }

        if (appliedAffinity != expectedAffinity)
        {
            return new RecorderCaptureVisibilityResult(
                false,
                trayInFrame,
                RecorderCaptureVisibilityFailure.AffinityReadbackMismatch,
                windowHandle,
                0,
                appliedAffinity);
        }

        return RecorderCaptureVisibilityResult.Success(trayInFrame);
    }

    private void ApplyCurrentPolicyAfterHandleCreated()
    {
        _ = ApplyCurrentIntent();
    }

    private void OnWindowHandleCreated(object? sender, EventArgs e)
    {
        if (!_disposed && sender is Form { IsHandleCreated: true })
        {
            ApplyCurrentPolicyAfterHandleCreated();
        }
    }

    private void OnWindowHandleDestroyed(object? sender, EventArgs e)
    {
        // Keep the Form registered: HandleCreated reapplies the role-specific
        // policy after any legitimate WinForms HWND recreation.
    }

    private void Unregister(Form window)
    {
        if (_registeredWindows.Remove(window))
        {
            Detach(window);
        }
    }

    private void Detach(Form window)
    {
        window.HandleCreated -= OnWindowHandleCreated;
        window.HandleDestroyed -= OnWindowHandleDestroyed;
    }

    private readonly record struct PolicyWindow(
        nint Handle,
        RecorderCaptureWindowRole Role);

    private sealed class WindowRegistration : IDisposable
    {
        private RecorderCaptureVisibilityController? _owner;
        private Form? _window;

        internal WindowRegistration(
            RecorderCaptureVisibilityController owner,
            Form window)
        {
            _owner = owner;
            _window = window;
        }

        public void Dispose()
        {
            RecorderCaptureVisibilityController? owner =
                Interlocked.Exchange(ref _owner, null);
            Form? window = Interlocked.Exchange(ref _window, null);
            if (owner is not null && window is not null)
            {
                owner.Unregister(window);
            }
        }
    }

    private delegate bool EnumWindowsCallback(
        nint windowHandle,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(
        nint windowHandle,
        uint command);
}
