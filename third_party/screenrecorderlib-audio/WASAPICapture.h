// SPDX-License-Identifier: MIT
// Copyright (c) Microsoft Corporation.
//
// Capture transport adapted from Microsoft Windows-classic-samples,
// CaptureSharedTimerDriven, pinned at commit
// 77f217b3f89d4dac7864a62cc91ff7b569f26a50.
//
// This compatibility surface is derived from the MIT-licensed call sites in
// ScreenRecorderLib AudioManager and WASAPINotify. No legacy WASAPICapture
// implementation source is copied by this file.

#pragma once

#include "WWMFResampler.h"

#include <windows.h>
#include <mmdeviceapi.h>

#include <memory>
#include <string>
#include <thread>
#include <vector>

struct AUDIO_OPTIONS;

class WASAPICapture final
{
public:
	WASAPICapture(
		std::shared_ptr<AUDIO_OPTIONS>& audioOptions,
		std::wstring tag = L"");
	~WASAPICapture();

	WASAPICapture(const WASAPICapture&) = delete;
	WASAPICapture& operator=(const WASAPICapture&) = delete;

	HRESULT Initialize(std::wstring deviceId, EDataFlow flow);
	HRESULT StartCapture();
	HRESULT StopCapture();
	HRESULT GetCaptureResult() const noexcept;

	bool IsCapturing();
	void ClearRecordedBytes();
	std::vector<BYTE> GetRecordedBytes(UINT64 duration100Nanos);
	void ReturnAudioBytesToBuffer(std::vector<BYTE> bytes);

	void SetDefaultDevice(EDataFlow flow, ERole role, LPCWSTR id);
	void SetOffline(bool isOffline);

	EDataFlow GetFlow();
	std::wstring GetTag();
	std::wstring GetDeviceName();
	std::wstring GetDeviceId();
	WWMFPcmFormat GetInputFormat();

private:
	struct Impl;
	std::unique_ptr<Impl> impl_;
};
