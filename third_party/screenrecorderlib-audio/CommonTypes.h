// SPDX-License-Identifier: MIT
// Copyright (c) 2017 Sverre Skodje
//
// Mechanically extracted from ScreenRecorderLib v6.6.0 CommonTypes.h.
// Only the AUDIO_OPTIONS definition required by the vendored audio subsystem
// is retained; its defaults, property notifications, and public contract are
// unchanged.

#pragma once

#include <Windows.h>
#include <mfapi.h>

#include <string>

#include "Util.h"

struct AUDIO_OPTIONS {
protected:
#pragma region Format constants
	const GUID AUDIO_ENCODING_FORMAT = MFAudioFormat_AAC;
	const UINT32 AUDIO_BITS_PER_SAMPLE = 16;
	const UINT32 AUDIO_SAMPLES_PER_SECOND = 48000;
#pragma endregion

	std::wstring m_AudioOutputDevice = L"";
	std::wstring m_AudioInputDevice = L"";
	bool m_IsAudioEnabled = false;
	bool m_IsOutputDeviceEnabled = true;
	bool m_IsInputDeviceEnabled = true;
	bool m_IsInputDeviceDownmixingEnabled = true;
	UINT32 m_AudioBitrate = (96 / 8) * 1000;
	UINT32 m_AudioChannels = 2;
	float m_OutputVolumeModifier = 1;
	float m_InputVolumeModifier = 1;
	UINT32 m_InputMasterChannel = 0;

	void Notify(HANDLE h) {
		SetEvent(h);
	}

public:
	HANDLE OnPropertyChangedEvent;

	AUDIO_OPTIONS() {
		OnPropertyChangedEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
	}

	~AUDIO_OPTIONS() {
		CloseHandle(OnPropertyChangedEvent);
	}

	void SetInputVolume(float volume) { m_InputVolumeModifier = volume; Notify(OnPropertyChangedEvent); }
	void SetOutputVolume(float volume) { m_OutputVolumeModifier = volume; Notify(OnPropertyChangedEvent); }
	void SetAudioBitrate(UINT32 bitrate) { m_AudioBitrate = bitrate; Notify(OnPropertyChangedEvent); }
	void SetAudioChannels(UINT32 channels) { m_AudioChannels = channels; Notify(OnPropertyChangedEvent); }
	void SetOutputDevice(std::wstring string) { m_AudioOutputDevice = string; Notify(OnPropertyChangedEvent); }
	void SetInputDevice(std::wstring string) { m_AudioInputDevice = string; Notify(OnPropertyChangedEvent); }
	void SetAudioEnabled(bool value) { m_IsAudioEnabled = value; Notify(OnPropertyChangedEvent); }
	void SetOutputDeviceEnabled(bool value) { m_IsOutputDeviceEnabled = value; Notify(OnPropertyChangedEvent); }
	void SetInputDeviceEnabled(bool value) { m_IsInputDeviceEnabled = value; Notify(OnPropertyChangedEvent); }
	void SetInputDeviceDownmixingEnabled(bool value) { m_IsInputDeviceDownmixingEnabled = value; Notify(OnPropertyChangedEvent); }
	void SetInputDeviceMasterChannel(int value) { m_InputMasterChannel = value; Notify(OnPropertyChangedEvent); }

	std::wstring GetAudioOutputDevice() { return m_AudioOutputDevice; }
	std::wstring GetAudioInputDevice() { return m_AudioInputDevice; }
	bool IsAudioEnabled() { return m_IsAudioEnabled; }
	UINT32 GetAudioBitrate() { return m_AudioBitrate; }
	UINT32 GetAudioChannels() { return m_AudioChannels; }
	float GetOutputVolume() { return m_OutputVolumeModifier; }
	float GetInputVolume() { return m_InputVolumeModifier; }
	bool IsOutputDeviceEnabled() { return m_IsOutputDeviceEnabled; }
	bool IsInputDeviceEnabled() { return m_IsInputDeviceEnabled; }
	bool IsInputDeviceDownmixingEnabled() { return m_IsInputDeviceDownmixingEnabled; }
	GUID GetAudioEncoderFormat() { return AUDIO_ENCODING_FORMAT; }
	UINT32 GetAudioBitsPerSample() { return AUDIO_BITS_PER_SAMPLE; }
	UINT32 GetAudioSamplesPerSecond() { return AUDIO_SAMPLES_PER_SECOND; }
	UINT32 getInputMasterChannel() { return m_InputMasterChannel; }
};
