# GStreamer / GLib disposition for Xiaobai Recorder 1.0.0

`DECISION = KEEP_CURRENT_GSTREAMER`

## Frozen runtime

- `RUNTIME = GStreamer 1.28.6`
- `PLUGIN SET = 7`
  - `gstcoreelements.dll`
  - `gstwasapi2.dll`
  - `gstaudioconvert.dll`
  - `gstaudioresample.dll`
  - `gstwebrtcdsp.dll`
  - `gstflac.dll`
  - `gstlevel.dll`
- `SUPPORT DLL SET = 15`
  - `gstreamer-1.0-0.dll`
  - `gstbase-1.0-0.dll`
  - `gstaudio-1.0-0.dll`
  - `gsttag-1.0-0.dll`
  - `gstbadaudio-1.0-0.dll`
  - `glib-2.0-0.dll`
  - `gmodule-2.0-0.dll`
  - `gobject-2.0-0.dll`
  - `intl-8.dll`
  - `orc-0.4-0.dll`
  - `z-1.dll`
  - `ffi-7.dll`
  - `pcre2-8-0.dll`
  - `FLAC-8.dll`
  - `ogg-0.dll`

## Disposition

- `LICENSE MODEL = LGPL-covered GStreamer/GLib components plus documented permissive dependency licenses`
- `GPL/RESTRICTED RUNTIME = NONE FOUND IN FROZEN SET`
- `PRODUCT CODE CHANGE = NONE`
- `RUNTIME CHANGE = NONE`
- `SOURCE COMPANION REQUIRED = YES`
- `XIAOBAI RECORDER LICENSE = MIT`
- `TECHNICAL MODEL = app-local dynamically linked/runtime-loaded GStreamer components`

The corresponding-source candidate is `xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz`, size 35,709,296 bytes, SHA-256 `A1BBAFC3EF8248547CB41D855886CFA8C10250A0ADCA4FCC8B8816091DA7FE93`. It maps all seven plugins and fifteen support DLLs, supplies the source/build-control closure, and documents compatible DLL/plugin replacement and LGPL modification debugging.

`DISCLAIMER = Engineering compliance decision; not legal advice.`
