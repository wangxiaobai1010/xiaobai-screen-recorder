using System.Diagnostics;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

/// <summary>
/// Owns only the Panel 3 Stage Background command boundary. Its native
/// dependency exposes neither Stage pose nor Camera commands, so a background
/// selection cannot mutate the frozen 2.5D pose or Panel 2 zoom.
/// </summary>
internal sealed class Stage3DPanelBackgroundController
{
    private readonly object _gate = new();
    private readonly Stage3DPanelBackgroundState _presentationState;
    private readonly ProductState _productState;
    private readonly Func<IWindowShowcaseBackgroundCommands?> _sessionProvider;
    private bool _initialized;
    private bool _actionsEnabled;

    internal Stage3DPanelBackgroundController(
        Stage3DPanelBackgroundState presentationState,
        ProductState productState,
        Func<IWindowShowcaseBackgroundCommands?> sessionProvider)
    {
        _presentationState = presentationState ??
            throw new ArgumentNullException(nameof(presentationState));
        _productState = productState ??
            throw new ArgumentNullException(nameof(productState));
        _sessionProvider = sessionProvider ??
            throw new ArgumentNullException(nameof(sessionProvider));

        lock (_gate)
        {
            PublishSettingsUnsafe(
                _productState.Current,
                actionsEnabled: false,
                statusText: string.Empty);
        }
    }

    internal NativeMethods.Result Initialize(bool actionsEnabled)
    {
        lock (_gate)
        {
            ProductSettings settings = _productState.Current;
            IWindowShowcaseBackgroundCommands? session = _sessionProvider();
            if (session is null)
            {
                PublishSettingsUnsafe(
                    settings,
                    actionsEnabled: false,
                    statusText: Strings.Get("BackgroundRuntimeNotReady"));
                return NativeMethods.Result.InvalidState;
            }

            NativeMethods.Result result = ApplySettingsUnsafe(
                session,
                settings);
            if (result != NativeMethods.Result.Ok)
            {
                result = FallbackToWarmUnsafe(
                    session,
                    Strings.Get("BackgroundFileFallback"));
            }
            else
            {
                PublishSettingsUnsafe(
                    settings,
                    actionsEnabled: false,
                    statusText: string.Empty);
            }

            _initialized = result == NativeMethods.Result.Ok;
            _actionsEnabled = _initialized && actionsEnabled;
            Stage3DPanelBackgroundSnapshot current =
                _presentationState.Snapshot;
            _presentationState.Apply(current with
            {
                ActionsEnabled = _actionsEnabled,
            });
            return result;
        }
    }

    internal void SetActionsEnabled(
        bool enabled,
        bool changesPresentation = true)
    {
        lock (_gate)
        {
            _actionsEnabled = _initialized && enabled;
            if (!changesPresentation)
            {
                return;
            }
            Stage3DPanelBackgroundSnapshot current =
                _presentationState.Snapshot;
            _presentationState.Apply(current with
            {
                ActionsEnabled = _actionsEnabled,
            });
        }
    }

    internal NativeMethods.Result SelectPreset(
        Stage3DPanelBackgroundPreset preset)
    {
        lock (_gate)
        {
            if (!_actionsEnabled)
            {
                return NativeMethods.Result.InvalidState;
            }

            IWindowShowcaseBackgroundCommands? session = _sessionProvider();
            if (session is null)
            {
                return FailUnsafe(
                    NativeMethods.Result.InvalidState,
                    Strings.Get("BackgroundRuntimeUnavailable"));
            }

            NativeMethods.WindowShowcaseBackgroundPreset nativePreset =
                MapPreset(preset);
            NativeMethods.Result result =
                session.SetWindowShowcaseBackgroundPreset(nativePreset);
            if (result != NativeMethods.Result.Ok)
            {
                return FailUnsafe(result, Strings.Get("BackgroundLoadFailed"));
            }

            ProductSettings settings = _productState.Current with
            {
                BackgroundSource = ProductBackgroundSource.Preset,
                BackgroundPreset = (ProductBackgroundPreset)preset,
                CustomBackgroundPath = null,
            };
            string status = CommitSettingsUnsafe(settings);
            PublishSettingsUnsafe(settings, _actionsEnabled, status);
            return NativeMethods.Result.Ok;
        }
    }

    internal NativeMethods.Result SelectCustom(string? selectedPath)
    {
        lock (_gate)
        {
            // null is the explicit file-picker cancellation contract.
            if (selectedPath is null)
            {
                return NativeMethods.Result.Ok;
            }
            if (!_actionsEnabled)
            {
                return NativeMethods.Result.InvalidState;
            }
            if (!ProductPathContract.TryValidateCustomBackground(
                    selectedPath,
                    out string validated))
            {
                return FailUnsafe(
                    NativeMethods.Result.InvalidArgument,
                    Strings.Get("BackgroundReadFailed"));
            }

            IWindowShowcaseBackgroundCommands? session = _sessionProvider();
            if (session is null)
            {
                return FailUnsafe(
                    NativeMethods.Result.InvalidState,
                    Strings.Get("BackgroundRuntimeUnavailable"));
            }
            NativeMethods.Result result =
                session.SetWindowShowcaseCustomBackground(validated);
            if (result != NativeMethods.Result.Ok)
            {
                return FailUnsafe(
                    result,
                    Strings.Get("BackgroundDecodeFailed"));
            }

            ProductSettings settings = _productState.Current with
            {
                BackgroundSource = ProductBackgroundSource.CustomImage,
                BackgroundPreset = ProductBackgroundPreset.Warm,
                CustomBackgroundPath = validated,
            };
            string status = CommitSettingsUnsafe(settings);
            PublishSettingsUnsafe(settings, _actionsEnabled, status);
            return NativeMethods.Result.Ok;
        }
    }

    internal void ReportPickerFailure(string detail)
    {
        lock (_gate)
        {
            _ = FailUnsafe(
                NativeMethods.Result.NativeFailure,
                string.IsNullOrWhiteSpace(detail)
                    ? Strings.Get("BackgroundPickerFailed")
                    : detail);
        }
    }

    internal static NativeMethods.WindowShowcaseBackgroundPreset MapPreset(
        Stage3DPanelBackgroundPreset preset) => preset switch
        {
            Stage3DPanelBackgroundPreset.Warm =>
                NativeMethods.WindowShowcaseBackgroundPreset.Warm,
            Stage3DPanelBackgroundPreset.Art01 =>
                NativeMethods.WindowShowcaseBackgroundPreset.Art01,
            Stage3DPanelBackgroundPreset.Art001 =>
                NativeMethods.WindowShowcaseBackgroundPreset.Art001,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Unknown Panel 3 background preset."),
        };

    private NativeMethods.Result ApplySettingsUnsafe(
        IWindowShowcaseBackgroundCommands session,
        ProductSettings settings)
    {
        if (settings.BackgroundSource == ProductBackgroundSource.CustomImage)
        {
            return ProductPathContract.TryValidateCustomBackground(
                settings.CustomBackgroundPath,
                out string customPath)
                    ? session.SetWindowShowcaseCustomBackground(customPath)
                    : NativeMethods.Result.InvalidArgument;
        }

        return session.SetWindowShowcaseBackgroundPreset(
            MapPreset((Stage3DPanelBackgroundPreset)
                settings.BackgroundPreset));
    }

    private NativeMethods.Result FallbackToWarmUnsafe(
        IWindowShowcaseBackgroundCommands session,
        string statusText)
    {
        NativeMethods.Result result =
            session.SetWindowShowcaseBackgroundPreset(
                NativeMethods.WindowShowcaseBackgroundPreset.Warm);
        if (result != NativeMethods.Result.Ok)
        {
            return FailUnsafe(result, Strings.Get("BackgroundWarmFailed"));
        }

        ProductSettings settings = _productState.Current with
        {
            BackgroundSource = ProductBackgroundSource.Preset,
            BackgroundPreset = ProductBackgroundPreset.Warm,
            CustomBackgroundPath = null,
        };
        string persistenceStatus = CommitSettingsUnsafe(settings);
        PublishSettingsUnsafe(
            settings,
            actionsEnabled: false,
            statusText: string.IsNullOrWhiteSpace(persistenceStatus)
                ? statusText
                : $"{statusText}；{persistenceStatus}");
        return NativeMethods.Result.Ok;
    }

    private NativeMethods.Result FailUnsafe(
        NativeMethods.Result result,
        string statusText)
    {
        Stage3DPanelBackgroundSnapshot current =
            _presentationState.Snapshot;
        _presentationState.Apply(current with { StatusText = statusText });
        return result;
    }

    private string CommitSettingsUnsafe(ProductSettings settings)
    {
        _productState.Set(settings);
        try
        {
            _productState.Persist();
            return string.Empty;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            Debug.WriteLine(
                $"Panel 3 background persistence failed: {error}");
            return Strings.Get("BackgroundSaveFailed");
        }
    }

    private void PublishSettingsUnsafe(
        ProductSettings settings,
        bool actionsEnabled,
        string statusText)
    {
        bool custom = settings.BackgroundSource ==
            ProductBackgroundSource.CustomImage &&
            !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath);
        _presentationState.Apply(new Stage3DPanelBackgroundSnapshot(
            custom
                ? Stage3DPanelBackgroundSource.CustomImage
                : Stage3DPanelBackgroundSource.Preset,
            (Stage3DPanelBackgroundPreset)settings.BackgroundPreset,
            custom ? settings.CustomBackgroundPath! : string.Empty,
            actionsEnabled,
            statusText));
    }
}
