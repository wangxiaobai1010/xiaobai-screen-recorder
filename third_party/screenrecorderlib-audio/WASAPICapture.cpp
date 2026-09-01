// SPDX-License-Identifier: MIT
// Copyright (c) Microsoft Corporation.
//
// Capture transport adapted from Microsoft Windows-classic-samples,
// CaptureSharedTimerDriven and ApplicationLoopback, pinned at commit
// 77f217b3f89d4dac7864a62cc91ff7b569f26a50.
//
// Microsoft reference paths:
// Samples/Win7Samples/multimedia/audio/CaptureSharedTimerDriven
// Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp
//
// The compatibility surface is derived from the MIT-licensed ScreenRecorderLib
// AudioManager and WASAPINotify call sites. No legacy WASAPICapture
// implementation source is copied by this file.

#include "WASAPICapture.h"

#include "CommonTypes.h"
#include "CoreAudio.util.h"
#include "DynamicWait.h"
#include "Log.h"
#include "WASAPINotify.h"

#include <audioclient.h>
#include <avrt.h>
#include <ksmedia.h>
#include <mmreg.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <deque>
#include <limits>
#include <mutex>
#include <new>
#include <system_error>
#include <thread>
#include <utility>

#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "ole32.lib")

namespace
{
	using Microsoft::WRL::ComPtr;
	// Retained WASAPINotify calls SetOffline(false) and StartCapture back to
	// back on the same notification thread. A per-thread one-shot token keeps
	// that callback path nonblocking without conflating concurrent callbacks.
	thread_local WASAPICapture* g_notificationResumeCapture = nullptr;

	struct CoTaskMemDeleter
	{
		void operator()(void* const memory) const noexcept
		{
			CoTaskMemFree(memory);
		}
	};
	// CaptureSharedTimerDriven uses a 20 ms engine latency and wakes at half
	// that interval. Keep that Microsoft reference cadence unchanged.
	constexpr REFERENCE_TIME kEngineLatency100ns = 20 * 10'000;
	constexpr DWORD kCapturePollIntervalMs = 10;
	constexpr UINT64 kHundredNanosPerSecond = 10'000'000;
	constexpr UINT64 kSilenceChunkFrames = 4'096;

	HRESULT LastErrorAsHResult() noexcept
	{
		const DWORD error = GetLastError();
		return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error);
	}

	DWORD DefaultChannelMask(const WORD channels) noexcept
	{
		switch (channels)
		{
		case 1:
			return SPEAKER_FRONT_CENTER;
		case 2:
			return SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
		case 6:
			return KSAUDIO_SPEAKER_5POINT1;
		default:
			return 0;
		}
	}

	HRESULT DescribeWaveFormat(
		_In_ const WAVEFORMATEX* const format,
		_Out_ WWMFPcmFormat& description) noexcept
	{
		if (format == nullptr || format->nChannels == 0 ||
			format->nSamplesPerSec == 0 || format->wBitsPerSample == 0)
		{
			return E_INVALIDARG;
		}

		WWMFBitFormatType sampleFormat = WWMFBitFormatType::WWMFBitFormatUnknown;
		WORD validBits = format->wBitsPerSample;
		DWORD channelMask = DefaultChannelMask(format->nChannels);

		switch (format->wFormatTag)
		{
		case WAVE_FORMAT_PCM:
			sampleFormat = WWMFBitFormatType::WWMFBitFormatInt;
			break;
		case WAVE_FORMAT_IEEE_FLOAT:
			sampleFormat = WWMFBitFormatType::WWMFBitFormatFloat;
			break;
		case WAVE_FORMAT_EXTENSIBLE:
		{
			if (format->cbSize <
				sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX))
			{
				return AUDCLNT_E_UNSUPPORTED_FORMAT;
			}

			const auto* const extensible =
				reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(format);
			if (IsEqualGUID(extensible->SubFormat, KSDATAFORMAT_SUBTYPE_PCM))
			{
				sampleFormat = WWMFBitFormatType::WWMFBitFormatInt;
			}
			else if (IsEqualGUID(
				extensible->SubFormat,
				KSDATAFORMAT_SUBTYPE_IEEE_FLOAT))
			{
				sampleFormat = WWMFBitFormatType::WWMFBitFormatFloat;
			}
			else
			{
				return AUDCLNT_E_UNSUPPORTED_FORMAT;
			}

			validBits = extensible->Samples.wValidBitsPerSample == 0
				? format->wBitsPerSample
				: extensible->Samples.wValidBitsPerSample;
			channelMask = extensible->dwChannelMask;
			break;
		}
		default:
			return AUDCLNT_E_UNSUPPORTED_FORMAT;
		}

		description = WWMFPcmFormat(
			sampleFormat,
			format->nChannels,
			format->wBitsPerSample,
			format->nSamplesPerSec,
			channelMask,
			validBits);
		return S_OK;
	}

	bool RequiresResampler(
		const WWMFPcmFormat& input,
		const WWMFPcmFormat& output) noexcept
	{
		return input.sampleFormat != output.sampleFormat ||
			input.nChannels != output.nChannels ||
			input.bits != output.bits ||
			input.sampleRate != output.sampleRate ||
			input.validBitsPerSample != output.validBitsPerSample;
	}

	void CloseEventHandle(HANDLE& handle) noexcept
	{
		if (handle != nullptr)
		{
			CloseHandle(handle);
			handle = nullptr;
		}
	}
}

struct WASAPICapture::Impl
{
	enum class WorkerState
	{
		Idle,
		Starting,
		Running,
		Exited,
	};

	explicit Impl(
		WASAPICapture* const ownerValue,
		std::shared_ptr<AUDIO_OPTIONS> optionsValue,
		std::wstring tagValue)
		: owner(ownerValue),
		  options(std::move(optionsValue)),
		  tag(std::move(tagValue))
	{
		captureStartedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		captureExitedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		captureStopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		captureRestartEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		reconnectRequestEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		reconnectStopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

		if (captureStartedEvent == nullptr || captureExitedEvent == nullptr ||
			captureStopEvent == nullptr || captureRestartEvent == nullptr ||
			reconnectRequestEvent == nullptr || reconnectStopEvent == nullptr)
		{
			constructionResult = LastErrorAsHResult();
			return;
		}

		reconnectBackoff.SetWaitBands({
			{ 25, 10 },
			{ 250, 20 },
			{ 1'000, WAIT_BAND_STOP },
		});

	}

	~Impl()
	{
		Shutdown();
	}

	void StartRuntime() noexcept
	{
		if (constructionResult != S_OK)
		{
			return;
		}

		try
		{
			reconnectThread = std::thread([this] { ReconnectThreadMain(); });
		}
		catch (const std::system_error&)
		{
			constructionResult = E_FAIL;
			return;
		}
		catch (...)
		{
			constructionResult = E_UNEXPECTED;
			return;
		}

		const HRESULT listenerResult = StartListeners();
		if (FAILED(listenerResult))
		{
			LOG_WARN(
				L"WASAPI endpoint notifications are unavailable for %ls: hr=0x%08x",
				tag.c_str(),
				listenerResult);
		}
	}

	HRESULT StartListeners() noexcept
	{
		if (notificationsRegistered)
		{
			return S_FALSE;
		}

		ComPtr<IMMDeviceEnumerator> newEnumerator;
		HRESULT result = CoCreateInstance(
			__uuidof(MMDeviceEnumerator),
			nullptr,
			CLSCTX_INPROC_SERVER,
			IID_PPV_ARGS(newEnumerator.GetAddressOf()));
		if (FAILED(result))
		{
			return result;
		}

		auto* const notifierRaw = new (std::nothrow) WASAPINotify(owner);
		if (notifierRaw == nullptr)
		{
			return E_OUTOFMEMORY;
		}
		notifierRaw->AddRef();
		ComPtr<WASAPINotify> newNotifier;
		newNotifier.Attach(notifierRaw);

		result = newEnumerator->RegisterEndpointNotificationCallback(
			newNotifier.Get());
		if (FAILED(result))
		{
			return result;
		}

		notificationEnumerator = newEnumerator;
		notifier = newNotifier;
		notificationsRegistered = true;
		return S_OK;
	}

	void StopListeners() noexcept
	{
		if (notificationsRegistered && notificationEnumerator && notifier)
		{
			const HRESULT result =
					notificationEnumerator->UnregisterEndpointNotificationCallback(
						notifier.Get());
			if (FAILED(result))
			{
				LOG_WARN(
					L"Unable to unregister WASAPI endpoint notifications for %ls: hr=0x%08x",
					tag.c_str(),
					result);
			}
		}

		notificationsRegistered = false;
		notifier.Reset();
		notificationEnumerator.Reset();
	}

	void ResetTransportLocked() noexcept
	{
		resampler.reset();
		captureClient.Reset();
		audioClient.Reset();
		endpoint.Reset();
		inputFormat = {};
		outputFormat = {};
		haveExpectedDevicePosition = false;
		expectedDevicePosition = 0;
		lastQpcPosition100ns = 0;
	}

	HRESULT InitializeLocked(const std::wstring &deviceId, const EDataFlow newFlow) noexcept
	{
		try
		{
			if (constructionResult != S_OK)
			{
				return constructionResult;
			}
			if (options == nullptr)
			{
				return E_POINTER;
			}
			if (newFlow != eCapture && newFlow != eRender)
			{
				return E_INVALIDARG;
			}
			const UINT64 changeGeneration = endpointChangeGeneration.load();

			// Persist the caller's requested endpoint before any operation that can
			// fail. A later retry must never fall back to the previous/default flow.
			requestedDeviceId = deviceId;
			flow.store(newFlow);
			usesDefaultDevice.store(deviceId.empty());
			std::atomic_store_explicit(&resolvedDeviceIdSnapshot,
									   std::shared_ptr<const std::wstring>{},
									   std::memory_order_release);
			deviceName.clear();
			ResetTransportLocked();

			ComPtr<IMMDevice> selectedEndpoint;
			IMMDevice *selectedEndpointRaw = nullptr;
			HRESULT result = deviceId.empty() ? GetDefaultAudioDevice(newFlow, &selectedEndpointRaw)
											  : GetActiveAudioDevice(deviceId.c_str(), newFlow,
																	 &selectedEndpointRaw);
			if (FAILED(result))
			{
				return result;
			}
			selectedEndpoint.Attach(selectedEndpointRaw);
			if (!selectedEndpoint)
			{
				return E_NOTFOUND;
			}

			LPWSTR endpointIdRaw = nullptr;
			result = selectedEndpoint->GetId(&endpointIdRaw);
			if (FAILED(result))
			{
				return result;
			}
			const std::unique_ptr<WCHAR, CoTaskMemDeleter> endpointIdOwner(endpointIdRaw);
			const std::wstring newResolvedDeviceId = endpointIdRaw == nullptr ? L"" : endpointIdRaw;
			std::shared_ptr<const std::wstring> newResolvedDeviceIdSnapshot;
			try
			{
				newResolvedDeviceIdSnapshot =
					std::make_shared<const std::wstring>(newResolvedDeviceId);
			}
			catch (const std::bad_alloc &)
			{
				return E_OUTOFMEMORY;
			}

			std::wstring newDeviceName;
			result = GetAudioDeviceFriendlyName(newResolvedDeviceId.c_str(), &newDeviceName);
			if (FAILED(result))
			{
				newDeviceName = L"Unknown Device";
			}

			ComPtr<IAudioClient> newAudioClient;
			result = selectedEndpoint->Activate(
				__uuidof(IAudioClient), CLSCTX_INPROC_SERVER, nullptr,
				reinterpret_cast<void **>(newAudioClient.GetAddressOf()));
			if (FAILED(result))
			{
				return result;
			}

			WAVEFORMATEX *mixFormat = nullptr;
			result = newAudioClient->GetMixFormat(&mixFormat);
			if (FAILED(result))
			{
				return result;
			}

			WWMFPcmFormat newInputFormat;
			result = DescribeWaveFormat(mixFormat, newInputFormat);
			if (SUCCEEDED(result))
			{
				DWORD streamFlags = AUDCLNT_STREAMFLAGS_NOPERSIST;
				if (newFlow == eRender)
				{
					streamFlags |= AUDCLNT_STREAMFLAGS_LOOPBACK;
				}

				result = newAudioClient->Initialize(AUDCLNT_SHAREMODE_SHARED, streamFlags,
													kEngineLatency100ns, 0, mixFormat, nullptr);
			}
			CoTaskMemFree(mixFormat);
			if (FAILED(result))
			{
				return result;
			}

			ComPtr<IAudioCaptureClient> newCaptureClient;
			result = newAudioClient->GetService(IID_PPV_ARGS(newCaptureClient.GetAddressOf()));
			if (FAILED(result))
			{
				return result;
			}

			const UINT32 requestedChannels = options->GetAudioChannels();
			const WORD outputChannels =
				static_cast<WORD>(requestedChannels == 0 ? 1 : requestedChannels);
			const WWMFPcmFormat newOutputFormat(WWMFBitFormatType::WWMFBitFormatInt, outputChannels,
												16, options->GetAudioSamplesPerSecond(),
												DefaultChannelMask(outputChannels), 16);

			std::unique_ptr<WWMFResampler> newResampler;
			if (RequiresResampler(newInputFormat, newOutputFormat))
			{
				try
				{
					newResampler = std::make_unique<WWMFResampler>();
				}
				catch (const std::bad_alloc &)
				{
					return E_OUTOFMEMORY;
				}

				result = newResampler->Initialize(newInputFormat, newOutputFormat, 60);
				if (FAILED(result))
				{
					return result;
				}
			}
			// Clear the prior request before the final validation. A notification
			// arriving after this store writes true again, and no later commit may
			// overwrite it.
			needsReinitialize.store(false);
			if (offline.load())
			{
				needsReinitialize.store(true);
				return E_ABORT;
			}
			if (changeGeneration != endpointChangeGeneration.load())
			{
				needsReinitialize.store(true);
				return AUDCLNT_E_DEVICE_INVALIDATED;
			}

			std::atomic_store_explicit(&resolvedDeviceIdSnapshot,
									   std::move(newResolvedDeviceIdSnapshot),
									   std::memory_order_release);
			deviceName = std::move(newDeviceName);
			endpoint = selectedEndpoint;
			audioClient = newAudioClient;
			captureClient = newCaptureClient;
			inputFormat = newInputFormat;
			outputFormat = newOutputFormat;
			resampler = std::move(newResampler);
			haveExpectedDevicePosition = false;
			return S_OK;
		}
		catch (const std::bad_alloc &)
		{
			needsReinitialize.store(true);
			return E_OUTOFMEMORY;
		}
		catch (...)
		{
			needsReinitialize.store(true);
			return E_UNEXPECTED;
		}
	}

	BYTE TransportSilenceByte() const noexcept
	{
		// Integer 8-bit PCM is unsigned; all other formats admitted by
		// DescribeWaveFormat use all-zero bytes for digital silence.
		return inputFormat.sampleFormat == WWMFBitFormatType::WWMFBitFormatInt &&
			inputFormat.bits == 8
			? BYTE{ 0x80 }
			: BYTE{ 0 };
	}

	HRESULT AppendOutputBytes(const BYTE* const data, const size_t byteCount)
	{
		if (byteCount == 0)
		{
			return S_OK;
		}
		if (data == nullptr)
		{
			return E_POINTER;
		}

		try
		{
			const std::lock_guard<std::mutex> lock(fifoMutex);
			pcmFifo.insert(pcmFifo.end(), data, data + byteCount);
		}
		catch (const std::bad_alloc&)
		{
			return E_OUTOFMEMORY;
		}
		catch (...)
		{
			return E_UNEXPECTED;
		}
		return S_OK;
	}

	HRESULT ProcessTransportBytes(const BYTE* const data, const size_t byteCount)
	{
		if (byteCount == 0)
		{
			return S_OK;
		}

		if (!resampler)
		{
			return AppendOutputBytes(data, byteCount);
		}

		if (byteCount > (std::numeric_limits<DWORD>::max)())
		{
			return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
		}

		WWMFSampleData converted;
		HRESULT result = E_UNEXPECTED;
		try
		{
			result = resampler->Resample(
				data,
				static_cast<DWORD>(byteCount),
				&converted);
		}
		catch (const std::bad_alloc&)
		{
			if (converted.data != nullptr)
			{
				converted.Release();
			}
			return E_OUTOFMEMORY;
		}
		catch (...)
		{
			if (converted.data != nullptr)
			{
				converted.Release();
			}
			return E_UNEXPECTED;
		}
		if (FAILED(result))
		{
			if (converted.data != nullptr)
			{
				converted.Release();
			}
			return result;
		}

		const HRESULT appendResult =
			AppendOutputBytes(converted.data, converted.bytes);
		if (converted.data != nullptr)
		{
			converted.Release();
		}
		return appendResult;
	}

	HRESULT AppendTransportSilence(UINT64 frameCount)
	{
		const size_t frameBytes = inputFormat.FrameBytes();
		if (frameBytes == 0)
		{
			return E_UNEXPECTED;
		}

		while (frameCount != 0)
		{
			const UINT64 chunkFrames =
				(std::min)(frameCount, kSilenceChunkFrames);
			if (chunkFrames >
				(std::numeric_limits<size_t>::max)() / frameBytes)
			{
				return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
			}

			try
			{
				const std::vector<BYTE> silence(
					static_cast<size_t>(chunkFrames) * frameBytes,
					TransportSilenceByte());
				const HRESULT result =
					ProcessTransportBytes(silence.data(), silence.size());
				if (FAILED(result))
				{
					return result;
				}
			}
			catch (const std::bad_alloc&)
			{
				return E_OUTOFMEMORY;
			}

			frameCount -= chunkFrames;
		}
		return S_OK;
	}

	HRESULT DrainAvailablePackets(IAudioCaptureClient* const activeCaptureClient)
	{
		if (activeCaptureClient == nullptr)
		{
			return E_POINTER;
		}

		const size_t frameBytes = inputFormat.FrameBytes();
		if (frameBytes == 0)
		{
			return E_UNEXPECTED;
		}

		UINT32 packetFrames = 0;
		HRESULT result = activeCaptureClient->GetNextPacketSize(&packetFrames);
		while (SUCCEEDED(result) && packetFrames != 0)
		{
			BYTE* packetData = nullptr;
			UINT32 framesToRead = 0;
			DWORD flags = 0;
			UINT64 devicePosition = 0;
			UINT64 qpcPosition100ns = 0;

			result = activeCaptureClient->GetBuffer(
				&packetData,
				&framesToRead,
				&flags,
				&devicePosition,
				&qpcPosition100ns);
			if (FAILED(result))
			{
				return result;
			}

			if (framesToRead == 0 ||
				framesToRead >
					(std::numeric_limits<size_t>::max)() / frameBytes)
			{
				(void)activeCaptureClient->ReleaseBuffer(framesToRead);
				return framesToRead == 0
					? E_UNEXPECTED
					: HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
			}

			const size_t packetBytes =
				static_cast<size_t>(framesToRead) * frameBytes;
			std::vector<BYTE> ownedPacket;
			try
			{
				ownedPacket.resize(packetBytes);
				if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
				{
					std::fill(
						ownedPacket.begin(),
						ownedPacket.end(),
						TransportSilenceByte());
				}
				else if (packetData != nullptr)
				{
					std::copy_n(packetData, packetBytes, ownedPacket.data());
				}
				else
				{
					(void)activeCaptureClient->ReleaseBuffer(framesToRead);
					return E_POINTER;
				}
			}
			catch (const std::bad_alloc&)
			{
				(void)activeCaptureClient->ReleaseBuffer(framesToRead);
				return E_OUTOFMEMORY;
			}

			const HRESULT releaseResult =
				activeCaptureClient->ReleaseBuffer(framesToRead);
			if (FAILED(releaseResult))
			{
				return releaseResult;
			}

			const bool timestampValid =
				(flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) == 0;
			if (timestampValid && haveExpectedDevicePosition &&
				(flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0 &&
				devicePosition > expectedDevicePosition)
			{
				result = AppendTransportSilence(
					devicePosition - expectedDevicePosition);
				if (FAILED(result))
				{
					return result;
				}
			}

			if (timestampValid)
			{
				haveExpectedDevicePosition = true;
				expectedDevicePosition = devicePosition + framesToRead;
				lastQpcPosition100ns = qpcPosition100ns;
			}
			else
			{
				haveExpectedDevicePosition = false;
			}

			result = ProcessTransportBytes(
				ownedPacket.data(),
				ownedPacket.size());
			if (FAILED(result))
			{
				return result;
			}

			result = activeCaptureClient->GetNextPacketSize(&packetFrames);
		}

		return result;
	}

	// lifecycleMutex must be held by the caller. This starts transport work but
	// never changes the caller's requested-running intent.
	HRESULT StartWorkerLocked()
	{
		const WorkerState state = workerState.load();
		if (state == WorkerState::Starting || state == WorkerState::Running)
		{
			return S_FALSE;
		}

		if (captureThread.joinable())
		{
			if (state != WorkerState::Exited)
			{
				return HRESULT_FROM_WIN32(ERROR_BUSY);
			}
			try
			{
				captureThread.join();
			}
			catch (const std::system_error&)
			{
				return E_FAIL;
			}
			catch (...)
			{
				return E_UNEXPECTED;
			}
			workerState.store(WorkerState::Idle);
		}

		if (offline.load())
		{
			return E_ABORT;
		}
		if (constructionResult != S_OK)
		{
			return constructionResult;
		}

		if (!audioClient || !captureClient || needsReinitialize.load())
		{
			const HRESULT initializeResult =
				InitializeLocked(requestedDeviceId, flow.load());
			if (FAILED(initializeResult))
			{
				lastCaptureResult.store(initializeResult);
				return initializeResult;
			}
		}

		workerState.store(WorkerState::Starting);
		ResetEvent(captureStartedEvent);
		ResetEvent(captureExitedEvent);
		ResetEvent(captureStopEvent);
		ResetEvent(captureRestartEvent);
		if (shuttingDown.load() || !requestedRunning.load())
		{
			lastCaptureResult.store(E_ABORT);
			workerState.store(WorkerState::Idle);
			return E_ABORT;
		}
		if (offline.load())
		{
			lastCaptureResult.store(E_ABORT);
			workerState.store(WorkerState::Idle);
			return E_ABORT;
		}
		if (needsReinitialize.load())
		{
			lastCaptureResult.store(AUDCLNT_E_DEVICE_INVALIDATED);
			workerState.store(WorkerState::Idle);
			return AUDCLNT_E_DEVICE_INVALIDATED;
		}
		lastCaptureResult.store(S_OK);
		lastStopResult.store(S_OK);
		haveExpectedDevicePosition = false;

		ComPtr<IAudioClient> activeAudioClient = audioClient;
		ComPtr<IAudioCaptureClient> activeCaptureClient = captureClient;
		try
		{
			captureThread = std::thread(
				[state = this,
				 activeAudioClient,
				 activeCaptureClient]() mutable
				{
					state->CaptureThreadMain(
						std::move(activeAudioClient),
						std::move(activeCaptureClient));
				});
		}
		catch (const std::system_error&)
		{
			lastCaptureResult.store(E_FAIL);
			workerState.store(WorkerState::Idle);
			return E_FAIL;
		}
		catch (...)
		{
			lastCaptureResult.store(E_UNEXPECTED);
			workerState.store(WorkerState::Idle);
			return E_UNEXPECTED;
		}

		return S_OK;
	}

	void CaptureThreadMain(
		ComPtr<IAudioClient> activeAudioClient,
		ComPtr<IAudioCaptureClient> activeCaptureClient) noexcept
	{
		HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
		const bool comInitialized = SUCCEEDED(result);
		if (!comInitialized)
		{
			lastCaptureResult.store(result);
			capturing.store(false);
			workerState.store(WorkerState::Exited);
			SetEvent(captureExitedEvent);
			return;
		}

		DWORD mmcssTaskIndex = 0;
		HANDLE mmcssHandle =
			AvSetMmThreadCharacteristicsW(L"Audio", &mmcssTaskIndex);
		if (mmcssHandle == nullptr)
		{
			LOG_WARN(
				L"Unable to enable MMCSS for %ls: hr=0x%08x",
				tag.c_str(),
				LastErrorAsHResult());
		}

		result = activeAudioClient->Start();
		bool explicitStop = false;
		bool restartRequested = false;
		if (SUCCEEDED(result))
		{
			capturing.store(true);
			workerState.store(WorkerState::Running);
			SetEvent(captureStartedEvent);

			const HANDLE waitHandles[] = {
				captureStopEvent,
				captureRestartEvent,
			};
			bool keepRunning = true;
			while (keepRunning)
			{
				const DWORD waitResult = WaitForMultipleObjects(
					ARRAYSIZE(waitHandles),
					waitHandles,
					FALSE,
					kCapturePollIntervalMs);

				switch (waitResult)
				{
				case WAIT_OBJECT_0:
					explicitStop = true;
					keepRunning = false;
					break;
				case WAIT_OBJECT_0 + 1:
					restartRequested = true;
					keepRunning = false;
					break;
				case WAIT_TIMEOUT:
					result = DrainAvailablePackets(activeCaptureClient.Get());
					if (FAILED(result))
					{
						keepRunning = false;
					}
					break;
				case WAIT_FAILED:
					result = LastErrorAsHResult();
					keepRunning = false;
					break;
				default:
					result = E_UNEXPECTED;
					keepRunning = false;
					break;
				}
			}

			const HRESULT stopResult = activeAudioClient->Stop();
			lastStopResult.store(stopResult);
			if (SUCCEEDED(result) && FAILED(stopResult))
			{
				result = stopResult;
			}
		}

		capturing.store(false);
		workerState.store(WorkerState::Exited);
		lastCaptureResult.store(result);
		if (!explicitStop || restartRequested)
		{
			needsReinitialize.store(true);
		}

		if (mmcssHandle != nullptr)
		{
			AvRevertMmThreadCharacteristics(mmcssHandle);
		}

		activeCaptureClient.Reset();
		activeAudioClient.Reset();
		CoUninitialize();
		SetEvent(captureExitedEvent);

		if (!shuttingDown.load() && requestedRunning.load() &&
			!offline.load() && (!explicitStop || restartRequested))
		{
			SetEvent(reconnectRequestEvent);
		}
	}

	void ReconnectThreadMain() noexcept
	{
		const HRESULT comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
		const bool comInitialized = SUCCEEDED(comResult);
		if (!comInitialized)
		{
			lastCaptureResult.store(comResult);
			return;
		}

		const HANDLE waitHandles[] = {
			reconnectStopEvent,
			reconnectRequestEvent,
		};

		try
		{
			bool keepRunning = true;
			while (keepRunning)
			{
				const DWORD waitResult = WaitForMultipleObjects(
					ARRAYSIZE(waitHandles),
					waitHandles,
					FALSE,
					INFINITE);
				switch (waitResult)
				{
				case WAIT_OBJECT_0:
					keepRunning = false;
					break;
				case WAIT_OBJECT_0 + 1:
				{
					ResetEvent(reconnectRequestEvent);
					reconnectBackoff.Wait();
					if (shuttingDown.load() || !requestedRunning.load() ||
						offline.load())
					{
						break;
					}

					HRESULT result = S_OK;
					{
						const std::lock_guard<std::mutex> lock(lifecycleMutex);
						if (shuttingDown.load() || !requestedRunning.load() ||
							offline.load())
						{
							break;
						}

						if (captureThread.joinable())
						{
							if (workerState.load() != WorkerState::Exited)
							{
								break;
							}
							captureThread.join();
							workerState.store(WorkerState::Idle);
						}
						result = InitializeLocked(requestedDeviceId, flow.load());
						if (SUCCEEDED(result) && requestedRunning.load() &&
							!shuttingDown.load() && !offline.load())
						{
							result = StartWorkerLocked();
						}
					}
					if (FAILED(result) && !shuttingDown.load() &&
						requestedRunning.load() && !offline.load())
					{
						lastCaptureResult.store(result);
						SetEvent(reconnectRequestEvent);
					}
					break;
				}
				case WAIT_FAILED:
					lastCaptureResult.store(LastErrorAsHResult());
					keepRunning = false;
					break;
				default:
					lastCaptureResult.store(E_UNEXPECTED);
					keepRunning = false;
					break;
				}
			}
		}
		catch (const std::bad_alloc&)
		{
			lastCaptureResult.store(E_OUTOFMEMORY);
		}
		catch (...)
		{
			lastCaptureResult.store(E_UNEXPECTED);
		}

		CoUninitialize();
	}

	void Shutdown() noexcept
	{
		if (shutdownComplete.exchange(true))
		{
			return;
		}

		shuttingDown.store(true);
		StopListeners();
		requestedRunning.store(false);

		if (captureStopEvent != nullptr)
		{
			SetEvent(captureStopEvent);
		}
		if (reconnectStopEvent != nullptr)
		{
			SetEvent(reconnectStopEvent);
		}
		reconnectBackoff.Cancel();
		if (reconnectThread.joinable())
		{
			reconnectThread.join();
		}

		// Re-signal after reconnect has exited. This closes the narrow case where
		// a reconnect attempt reset the first stop signal before observing the
		// shutdown flag.
		if (captureStopEvent != nullptr)
		{
			SetEvent(captureStopEvent);
		}
		if (captureThread.joinable())
		{
			captureThread.join();
		}
		capturing.store(false);
		workerState.store(WorkerState::Idle);

		{
			const std::lock_guard<std::mutex> lock(lifecycleMutex);
			ResetTransportLocked();
		}

		CloseEventHandle(captureStartedEvent);
		CloseEventHandle(captureExitedEvent);
		CloseEventHandle(captureStopEvent);
		CloseEventHandle(captureRestartEvent);
		CloseEventHandle(reconnectRequestEvent);
		CloseEventHandle(reconnectStopEvent);
	}

	WASAPICapture* owner{};
	std::shared_ptr<AUDIO_OPTIONS> options;
	std::wstring tag;

	std::mutex lifecycleMutex;
	std::mutex fifoMutex;
	std::thread captureThread;
	std::thread reconnectThread;

	ComPtr<IMMDeviceEnumerator> notificationEnumerator;
	ComPtr<WASAPINotify> notifier;
	ComPtr<IMMDevice> endpoint;
	ComPtr<IAudioClient> audioClient;
	ComPtr<IAudioCaptureClient> captureClient;

	std::unique_ptr<WWMFResampler> resampler;
	WWMFPcmFormat inputFormat;
	WWMFPcmFormat outputFormat;

	std::deque<BYTE> pcmFifo;
	std::vector<BYTE> overflowBytes;

	std::wstring requestedDeviceId;
	std::shared_ptr<const std::wstring> resolvedDeviceIdSnapshot{
		std::make_shared<const std::wstring>() };
	std::wstring deviceName;
	std::atomic<EDataFlow> flow{ eRender };
	std::atomic<bool> usesDefaultDevice{ false };
	bool notificationsRegistered{};

	std::atomic<bool> requestedRunning{ false };
	std::atomic<bool> capturing{ false };
	std::atomic<WorkerState> workerState{ WorkerState::Idle };
	std::atomic<bool> offline{ false };
	std::atomic<bool> shuttingDown{ false };
	std::atomic<bool> needsReinitialize{ false };
	std::atomic<bool> shutdownComplete{ false };
	std::atomic<UINT64> endpointChangeGeneration{ 0 };
	std::atomic<HRESULT> lastCaptureResult{ S_OK };
	std::atomic<HRESULT> lastStopResult{ S_OK };

	bool haveExpectedDevicePosition{};
	UINT64 expectedDevicePosition{};
	UINT64 lastQpcPosition100ns{};

	HANDLE captureStartedEvent{};
	HANDLE captureExitedEvent{};
	HANDLE captureStopEvent{};
	HANDLE captureRestartEvent{};
	HANDLE reconnectRequestEvent{};
	HANDLE reconnectStopEvent{};

	DynamicWait reconnectBackoff;
	HRESULT constructionResult{ S_OK };
};

WASAPICapture::WASAPICapture(
	std::shared_ptr<AUDIO_OPTIONS>& audioOptions,
	std::wstring tag)
	: impl_(std::make_unique<Impl>(this, audioOptions, std::move(tag)))
{
	// The owner PImpl must be published before notification/reconnect callbacks
	// are allowed to call back into this WASAPICapture instance.
	impl_->StartRuntime();
}

WASAPICapture::~WASAPICapture() = default;

HRESULT WASAPICapture::Initialize(
	std::wstring deviceId,
	const EDataFlow flow)
{
	if (!impl_->notificationsRegistered)
	{
		(void)impl_->StartListeners();
	}
	const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
	const Impl::WorkerState state = impl_->workerState.load();
	if (state == Impl::WorkerState::Starting ||
		state == Impl::WorkerState::Running)
	{
		return HRESULT_FROM_WIN32(ERROR_BUSY);
	}
	if (impl_->captureThread.joinable())
	{
		if (state != Impl::WorkerState::Exited)
		{
			return HRESULT_FROM_WIN32(ERROR_BUSY);
		}
		try
		{
			impl_->captureThread.join();
		}
		catch (const std::system_error&)
		{
			return E_FAIL;
		}
		catch (...)
		{
			return E_UNEXPECTED;
		}
		impl_->workerState.store(Impl::WorkerState::Idle);
	}
	return impl_->InitializeLocked(deviceId, flow);
}

HRESULT WASAPICapture::StartCapture()
{
	// Retained WASAPINotify calls SetOffline(false) and then StartCapture from
	// inside an IMMNotificationClient callback. Those callbacks must not block,
	// and they must not resurrect a stream after an explicit product Stop.
	if (g_notificationResumeCapture == this)
	{
		g_notificationResumeCapture = nullptr;
		if (!impl_->requestedRunning.load())
		{
			return S_FALSE;
		}
		if (impl_->capturing.load())
		{
			return S_FALSE;
		}
		SetEvent(impl_->reconnectRequestEvent);
		return S_OK;
	}

	HANDLE startedEvent = nullptr;
	HANDLE exitedEvent = nullptr;
	{
		const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
		impl_->requestedRunning.store(true);
		const HRESULT startResult = impl_->StartWorkerLocked();
		if (startResult != S_OK)
		{
			if (FAILED(startResult) && !impl_->offline.load() &&
				!impl_->shuttingDown.load())
			{
				SetEvent(impl_->reconnectRequestEvent);
			}
			return startResult;
		}

		startedEvent = impl_->captureStartedEvent;
		exitedEvent = impl_->captureExitedEvent;
	}

	const HANDLE waitHandles[] = { startedEvent, exitedEvent };
	const DWORD waitResult = WaitForMultipleObjects(
		ARRAYSIZE(waitHandles), waitHandles, FALSE, INFINITE);
	if (waitResult == WAIT_OBJECT_0 && impl_->capturing.load())
	{
		return S_OK;
	}
	if (waitResult == WAIT_FAILED)
	{
		return LastErrorAsHResult();
	}

	const HRESULT captureResult = impl_->lastCaptureResult.load();
	return FAILED(captureResult) ? captureResult : E_FAIL;
}

HRESULT WASAPICapture::StopCapture()
{
	const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
	impl_->requestedRunning.store(false);
	ResetEvent(impl_->reconnectRequestEvent);

	const bool hadCaptureThread = impl_->captureThread.joinable();
	if (impl_->captureStopEvent != nullptr)
	{
		SetEvent(impl_->captureStopEvent);
	}
	if (hadCaptureThread)
	{
		try
		{
			impl_->captureThread.join();
		}
		catch (const std::system_error&)
		{
			return E_FAIL;
		}
		catch (...)
		{
			return E_UNEXPECTED;
		}
	}
	impl_->capturing.store(false);
	impl_->workerState.store(Impl::WorkerState::Idle);
	// Recreate the native client/resampler on the next Start so unread WASAPI
	// packets and transform state cannot cross recording boundaries. Keep the
	// current output format alive so callers can still drain the stopped FIFO.
	impl_->needsReinitialize.store(true);

	if (!hadCaptureThread)
	{
		return S_FALSE;
	}

	const HRESULT captureResult = impl_->lastCaptureResult.load();
	if (FAILED(captureResult))
	{
		return captureResult;
	}
	const HRESULT stopResult = impl_->lastStopResult.load();
	return FAILED(stopResult) ? stopResult : S_OK;
}

HRESULT WASAPICapture::GetCaptureResult() const noexcept
{
	return impl_->lastCaptureResult.load();
}

bool WASAPICapture::IsCapturing()
{
	// AudioManager only calls StopCapture when this returns true. Treat an
	// offline/reconnecting stream with a live run request as logically active so
	// an explicit product Stop always cancels reconnection.
	return impl_->requestedRunning.load() || impl_->capturing.load();
}

void WASAPICapture::ClearRecordedBytes()
{
	const std::lock_guard<std::mutex> lock(impl_->fifoMutex);
	impl_->pcmFifo.clear();
	impl_->overflowBytes.clear();
}

std::vector<BYTE> WASAPICapture::GetRecordedBytes(
	const UINT64 duration100Nanos)
{
	std::vector<BYTE> result;
	const std::lock_guard<std::mutex> lifecycleLock(impl_->lifecycleMutex);
	const std::lock_guard<std::mutex> lock(impl_->fifoMutex);

	const size_t frameBytes = impl_->outputFormat.FrameBytes();
	if (frameBytes == 0 || impl_->outputFormat.sampleRate == 0)
	{
		return result;
	}

	const long double requestedFramesExact =
		static_cast<long double>(impl_->outputFormat.sampleRate) *
		static_cast<long double>(duration100Nanos) /
		static_cast<long double>(kHundredNanosPerSecond);
	const UINT64 requestedFrames = static_cast<UINT64>(
		std::ceil(requestedFramesExact));
	if (requestedFrames >
		(std::numeric_limits<size_t>::max)() / frameBytes)
	{
		return result;
	}

	const size_t targetBytes =
		static_cast<size_t>(requestedFrames) * frameBytes;
	const size_t overflowCount = (std::min)(
		targetBytes,
		impl_->overflowBytes.size());
	const size_t alignedOverflowCount =
		overflowCount - overflowCount % frameBytes;
	const size_t remainingTarget = targetBytes - alignedOverflowCount;
	size_t fifoCount = (std::min)(remainingTarget, impl_->pcmFifo.size());
	fifoCount -= fifoCount % frameBytes;

	try
	{
		result.reserve(alignedOverflowCount + fifoCount);
		result.insert(
			result.end(),
			impl_->overflowBytes.begin(),
			impl_->overflowBytes.begin() + alignedOverflowCount);
		impl_->overflowBytes.erase(
			impl_->overflowBytes.begin(),
			impl_->overflowBytes.begin() + alignedOverflowCount);

		const auto fifoEnd = impl_->pcmFifo.begin() + fifoCount;
		result.insert(result.end(), impl_->pcmFifo.begin(), fifoEnd);
		impl_->pcmFifo.erase(impl_->pcmFifo.begin(), fifoEnd);
	}
	catch (const std::bad_alloc&)
	{
		result.clear();
	}
	catch (...)
	{
		result.clear();
	}
	return result;
}

void WASAPICapture::ReturnAudioBytesToBuffer(std::vector<BYTE> bytes)
{
	const std::lock_guard<std::mutex> lock(impl_->fifoMutex);
	if (bytes.empty())
	{
		return;
	}
	if (impl_->overflowBytes.empty())
	{
		impl_->overflowBytes.swap(bytes);
		return;
	}
	try
	{
		// AudioManager returns the tail of the bytes it just consumed. If an
		// older call left an overflow remainder, the returned tail precedes that
		// remainder on the PCM timeline and therefore must be prepended.
		bytes.insert(
			bytes.end(),
			impl_->overflowBytes.begin(),
			impl_->overflowBytes.end());
		impl_->overflowBytes.swap(bytes);
	}
	catch (const std::bad_alloc&)
	{
		LOG_ERROR(L"Unable to preserve returned WASAPI FIFO bytes for %ls",
			impl_->tag.c_str());
	}
	catch (...)
	{
		LOG_ERROR(L"Unexpected failure preserving WASAPI FIFO bytes for %ls",
			impl_->tag.c_str());
	}
}

void WASAPICapture::SetDefaultDevice(
	const EDataFlow flow,
	const ERole role,
	LPCWSTR id)
{
	if (!impl_->usesDefaultDevice.load() || flow != impl_->flow.load())
	{
		return;
	}

	// Retained CoreAudio.util resolves default endpoints with eConsole for both
	// capture and render. Ignore notifications for roles that cannot affect that
	// resolver's result.
	if (role != eConsole)
	{
		return;
	}

	(void)id;
	impl_->endpointChangeGeneration.fetch_add(1);
	impl_->needsReinitialize.store(true);
	impl_->offline.store(false);
	const Impl::WorkerState state = impl_->workerState.load();
	if (state == Impl::WorkerState::Starting ||
		state == Impl::WorkerState::Running)
	{
		SetEvent(impl_->captureRestartEvent);
	}
	else if (impl_->requestedRunning.load())
	{
		SetEvent(impl_->reconnectRequestEvent);
	}
}

void WASAPICapture::SetOffline(const bool isOffline)
{
	impl_->offline.store(isOffline);
	if (isOffline)
	{
		if (g_notificationResumeCapture == this)
		{
			g_notificationResumeCapture = nullptr;
		}
		impl_->endpointChangeGeneration.fetch_add(1);
		impl_->needsReinitialize.store(true);
		// Signal unconditionally. StartWorkerLocked performs a second offline
		// check after resetting this event, closing the check/reset race.
		SetEvent(impl_->captureRestartEvent);
		return;
	}

	g_notificationResumeCapture = this;
	if (impl_->requestedRunning.load() && !impl_->capturing.load())
	{
		SetEvent(impl_->reconnectRequestEvent);
	}
}

EDataFlow WASAPICapture::GetFlow()
{
	return impl_->flow.load();
}

std::wstring WASAPICapture::GetTag()
{
	const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
	return impl_->tag;
}

std::wstring WASAPICapture::GetDeviceName()
{
	const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
	return impl_->deviceName;
}

std::wstring WASAPICapture::GetDeviceId()
{
	const auto snapshot = std::atomic_load_explicit(
		&impl_->resolvedDeviceIdSnapshot,
		std::memory_order_acquire);
	return snapshot == nullptr ? std::wstring{} : *snapshot;
}

WWMFPcmFormat WASAPICapture::GetInputFormat()
{
	const std::lock_guard<std::mutex> lock(impl_->lifecycleMutex);
	// The FIFO exposed to AudioManager is already in the resampler's output
	// format. AudioManager uses this channel count when applying its retained
	// input-channel selection, so reporting the native endpoint format here
	// would make it reinterpret PCM16 output frames incorrectly.
	return impl_->outputFormat;
}
