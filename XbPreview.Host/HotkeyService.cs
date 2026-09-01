namespace XbPreview.Host;

internal sealed class HotkeyService : IDisposable
{
    internal const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly nint _window;
    private readonly IHotkeyRegistrar _registrar;
    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly HashSet<int> _registeredIds = [];
    private bool _previewAvailable;
    private bool _userEnabled;
    private bool _directorOwnsCamera;
    private bool _disposed;

    internal HotkeyService(
        nint window,
        IHotkeyRegistrar? registrar = null,
        IReadOnlyList<HotkeyBinding>? bindings = null)
    {
        _window = window;
        _registrar = registrar ?? new Win32HotkeyRegistrar();
        _bindings = bindings ?? HotkeyBindings.All;
        State = HotkeyActivationState.NotAvailable;
        LastResult = HotkeyRegistrationResult.ForState(State);
    }

    internal HotkeyActivationState State { get; private set; }

    internal HotkeyRegistrationResult LastResult { get; private set; }

    internal bool CanToggle =>
        !_disposed && _previewAvailable && !_directorOwnsCamera;

    internal bool UserEnabled => _userEnabled;

    internal bool IsSuspendedByDirector =>
        !_disposed && _previewAvailable && _directorOwnsCamera;

    internal void SetPreviewAvailable(bool available)
    {
        if (_disposed)
        {
            return;
        }

        if (!available)
        {
            ReleaseRegistrations();
            _previewAvailable = false;
            State = HotkeyActivationState.NotAvailable;
            LastResult = HotkeyRegistrationResult.ForState(State);
            return;
        }

        if (!_previewAvailable)
        {
            ReleaseRegistrations();
            _previewAvailable = true;
            ApplyPreference();
        }
    }

    internal HotkeyRegistrationResult SetUserEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_previewAvailable)
        {
            LastResult = HotkeyRegistrationResult.ForState(
                HotkeyActivationState.NotAvailable);
            return LastResult;
        }
        if (_userEnabled == enabled &&
            State != HotkeyActivationState.Failed)
        {
            return LastResult;
        }
        _userEnabled = enabled;
        ApplyPreference();
        return LastResult;
    }

    internal HotkeyRegistrationResult SetDirectorOwnsCamera(bool ownsCamera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_directorOwnsCamera == ownsCamera)
        {
            return LastResult;
        }
        _directorOwnsCamera = ownsCamera;
        ApplyPreference();
        return LastResult;
    }

    internal HotkeyRegistrationResult Enable()
    {
        return SetUserEnabled(true);
    }

    internal void Disable()
    {
        if (_disposed)
        {
            return;
        }

        SetUserEnabled(false);
    }

    internal bool IsRegistered(HotkeyBinding binding) =>
        _registeredIds.Contains(binding.Id);

    internal bool CanDispatch(HotkeyBinding binding) =>
        !_disposed &&
        _previewAvailable &&
        !_directorOwnsCamera &&
        State == HotkeyActivationState.Enabled &&
        IsRegistered(binding);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseRegistrations();
        _previewAvailable = false;
        _userEnabled = false;
        _directorOwnsCamera = false;
        State = HotkeyActivationState.NotAvailable;
        LastResult = HotkeyRegistrationResult.ForState(State);
        _disposed = true;
    }

    private void ApplyPreference()
    {
        ReleaseRegistrations();
        if (!_previewAvailable)
        {
            State = HotkeyActivationState.NotAvailable;
            LastResult = HotkeyRegistrationResult.ForState(State);
            return;
        }
        if (_directorOwnsCamera)
        {
            State = HotkeyActivationState.SuspendedByDirector;
            LastResult = HotkeyRegistrationResult.ForState(State);
            return;
        }
        if (!_userEnabled)
        {
            State = HotkeyActivationState.Disabled;
            LastResult = HotkeyRegistrationResult.ForState(State);
            return;
        }

        foreach (HotkeyBinding binding in _bindings)
        {
            if (!_registrar.Register(
                _window,
                binding.Id,
                ModNoRepeat,
                binding.VirtualKey,
                out int windowsErrorCode))
            {
                ReleaseRegistrations();
                State = HotkeyActivationState.Failed;
                LastResult = HotkeyRegistrationResult.Failure(
                    binding,
                    windowsErrorCode);
                return;
            }

            _registeredIds.Add(binding.Id);
        }

        State = HotkeyActivationState.Enabled;
        LastResult = HotkeyRegistrationResult.ForState(State);
    }

    private void ReleaseRegistrations()
    {
        for (int index = _bindings.Count - 1; index >= 0; index--)
        {
            HotkeyBinding binding = _bindings[index];
            if (_registeredIds.Remove(binding.Id))
            {
                _registrar.Unregister(_window, binding.Id);
            }
        }
    }
}
