# MVP Audio GStreamer 唯一 Runtime 终审

## 结论与边界

本审计针对提交 `8cc50fb4dd8d273993c914ed58379a44a11144b7` 的正式产品路由和重新生成的 `artifacts/package/win-x64`。未运行 Human Gate、长测或参数调优。

正式 capture/processing Audio Runtime 只有 GStreamer 1.28.6 MSVC x86_64。NAudio、SoundFlow、miniaudio 产品音频 runtime、旧 Audio V3/V4 runtime，以及旧 FFmpeg `agate`/`amix` speech patch runtime 均为 `ABSENT`，对应生产 Start 次数均为 0。后续最终响度收口仅在 Stop 后使用 package 内同一 FFmpeg LGPL build 的官方 two-pass `loudnorm` 与官方 `amix`; 这不是旧 capture/DSP runtime 的恢复。

## Release 历史残留与正式引用的区分

审计开始时，忽略版本控制的共享目录 `artifacts/bin/Release/x64` 中仍有旧构建留下的 `NAudio*.dll`、`SoundFlow*.dll`、`MiniaudioLoopbackProbe.exe/.pdb` 和多平台 `runtimes/**/libminiaudio.*`，共 21 个命中文件。其时间戳早于当前 GStreamer 提交；它们不是当前构建输入，也未被复制到正式 package。

事实核验：

- 当前 `csproj` 没有任何 `PackageReference`，只有测试/LongRun 到 Host 的项目引用；Host 不引用 NAudio 或 SoundFlow。
- 当前 `XbPreview.Native.vcxproj` 只编译 `GStreamerAudioCore`、`GStreamerAudioFinalizer` 和 `GStreamerAudioMode` 这些音频实现，不编译旧音频源。
- `XbPreview.Native.dll` 的 PE import 直接包含 `gstreamer-1.0-0.dll`、`gobject-2.0-0.dll`、`glib-2.0-0.dll`，不包含 NAudio、SoundFlow 或 miniaudio。
- package 的文件名、项目/运行时配置和 manifest 均没有旧 runtime 引用。

因此这些旧 DLL 是纯构建目录残留。终审安全删除了精确解析并确认位于仓库 `artifacts` 内的 `artifacts/bin/Release/x64`，随后由 package 脚本重新构建所需二进制。没有删除源码历史，也没有使用 `git reset --hard`。

## 唯一 Start/Stop 路由

产品链路是唯一的：

`RecordingController.StartCore/StopCore` → `NativePreviewSession` → `XbPreview_StartRecording/XbPreview_StopRecording` → `PreviewEngine` → `PreviewRenderer` → `VideoEncoderConsumer` → 唯一成员 `GStreamerAudioCore audioCore` 的 `Start/Stop`。

`SetAudioProgramMode` 只把 SystemOnly、MicrophoneOnly、Dual、None 映射到 `GStreamerAudioMode`。生产源码对 `NAudioMicrophoneSession`、SoundFlow、`MiniaudioSystemCapture`、`SystemAudioCapture`、`AudioProgramMixer`、`AudioSidecarRecorder`、`AudioV2Finalizer` 和 `agate` 的引用/启动调用计数均为 0。当前 `GStreamerAudioFinalizer` 在 Stop 后按产品模式使用 FFmpeg 官方 two-pass `loudnorm`；Dual 仅使用 `amix=inputs=2:weights='1 1':normalize=1`，SystemOnly 仍无音频 filter graph。所有模式都显式 map、H.264 copy、AAC 192 kbit/s、48 kHz stereo、`-shortest` 和 faststart。

## 分支名与 candidate 来源

当前分支仍叫 `candidate/mvp-audio-soundflow-core`，这是曾经评估 SoundFlow 时留下的历史分支名。SoundFlow 方案已明确 NO-GO，只以 `docs/stages/abandoned/MVP-AUDIO-SOUNDFLOW-NO-GO.patch` 保存历史（SHA-256 `4A1E5553235264A53DCA418F4CAC5A18C5767E0FE650429A5D97B26FC179D79A`）。实际 candidate 来源由 HEAD 提交、当前项目依赖和 package manifest 共同确定为 GStreamer，不由分支显示名决定。保留名称是为了保持候选演进链路，不代表 SoundFlow runtime 仍存在。

## 143-file 大变更核验

提交统计为 143 files changed、16,201 insertions、143,953 deletions。大体量的主要来源是：

- 删除 vendored `third_party/miniaudio/miniaudio.h`，约 95,864 行；
- 删除不再使用的 vendored libFLAC headers、旧 Audio V2/V3/P2.8 实现、诊断脚本和历史阶段文档；
- 删除与旧音频架构耦合的单体 `XbPreview.Native.Tests`（其中 `NativeTests.cpp` 约 12,576 行、`CrashValidationHarness.cpp` 约 2,773 行），并新增聚焦的 `XbPreview.GStreamer.Tests`；这是测试工程替换，不是生产能力删除；
- 新增约 13,876 行的 SoundFlow NO-GO 历史 patch；
- 新增 `GStreamerAudioCore`、finalizer、测试、打包/门禁脚本和许可证清单。

生产删除项是旧音频捕获、timeline、mixer、sidecar、DSP 和 FLAC 路径。视频 H.264 录制链、`RecordingController`、P2.6 存储安全/生命周期、Director、Window Stage 均未删除。`RecordingController` 只移除了 managed NAudio owner 并保留统一录制生命周期；`MfH264SinkWriterSession` 只移除了旧 PCM/AAC 音频分支并保留视频写入；`VideoEncoderConsumer` 的修改是接入 GStreamer 音频和保留 safe-publish seam。Director/Window Stage 正式源文件不在该提交的变更列表中。

## 正式 package manifest

固定版本：GStreamer 1.28.6，MSVC x86_64。

实际从 package 私有 `gstreamer/plugins` 加载的插件（`gst-inspect-1.0` 报告版本均为 1.28.6、License 均为 LGPL）：

| DLL | module / elements | 许可证 |
| --- | --- | --- |
| `gstcoreelements.dll` | gstreamer / queue, filesink | LGPL-2.0-or-later |
| `gstwasapi2.dll` | gst-plugins-bad / wasapi2src | LGPL-2.0-or-later |
| `gstaudioconvert.dll` | gst-plugins-base / audioconvert | LGPL-2.0-or-later |
| `gstaudioresample.dll` | gst-plugins-base / audioresample | LGPL-2.0-or-later |
| `gstwebrtcdsp.dll` | gst-plugins-bad / webrtcdsp | LGPL-2.0-or-later；WebRTC 依赖 BSD-3-Clause |
| `gstflac.dll` | gst-plugins-good / flacenc | LGPL-2.0-or-later；FLAC 依赖 BSD-3-Clause |

app-local runtime DLL 与许可证：

| DLL | component | 许可证 |
| --- | --- | --- |
| `gstreamer-1.0-0.dll`, `gstbase-1.0-0.dll`, `gstaudio-1.0-0.dll`, `gsttag-1.0-0.dll`, `gstbadaudio-1.0-0.dll` | GStreamer | LGPL-2.0-or-later |
| `glib-2.0-0.dll`, `gobject-2.0-0.dll`, `gmodule-2.0-0.dll` | GLib | LGPL-2.0-or-later |
| `intl-8.dll` | proxy-libintl | LGPL-2.0-or-later |
| `orc-0.4-0.dll` | ORC | BSD-2-Clause |
| `z-1.dll` | zlib | Zlib |
| `ffi-7.dll` | libffi | MIT |
| `pcre2-8-0.dll` | PCRE2 | BSD-3-Clause |
| `FLAC-8.dll` | FLAC | BSD-3-Clause |
| `ogg-0.dll` | libogg | BSD-3-Clause |

FFmpeg 是 package 内私有的 LGPL shared 文件 mastering/MP4 最终化依赖，不是第二套 capture/DSP runtime；manifest 明确记录 `audioFilters=true`、固定 `loudnorm`/`amix` 策略、`customDsp=false`、`agate=false`、`expander=false`。VC143 CRT 也随应用本地部署。

package 生成器排除 PDB，检查文本配置不存在开发机绝对路径，并把每个 payload 的相对路径、字节数和 SHA-256 写入 `package-manifest.json`。产品在初始化前按可执行文件位置解析 DLL/plugin 路径，设置私有 `GST_PLUGIN_SYSTEM_PATH_1_0` 并清空外部 plugin search path；不依赖全局 `PATH` 或全局 `GST_PLUGIN_PATH`。

## 唯一 Human Gate 命令（本审计未执行）

```powershell
powershell.exe -NoProfile -Command "Set-Location -LiteralPath 'E:\小白录屏器\xiaobai-screen-recorder\artifacts\package\win-x64'; .\XbPreview.Host.exe"
```
