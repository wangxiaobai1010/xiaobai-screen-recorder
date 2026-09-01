// SPDX-License-Identifier: MIT
// Copyright (c) 2017 Sverre Skodje
//
// Mechanically extracted audio-only RAII helpers from ScreenRecorderLib
// v6.6.0 Cleanup.h. Video/source-reader helpers are intentionally omitted.

#pragma once

#include "Log.h"
#include "WWMFResampler.h"

#include <Windows.h>
#include <atlbase.h>
#include <propidl.h>

template <class T> void SafeRelease(T** ppT)
{
	if (*ppT)
	{
		(*ppT)->Release();
		*ppT = nullptr;
	}
}

class LeaveCriticalSectionOnExit {
public:
	LeaveCriticalSectionOnExit(CRITICAL_SECTION* p, std::wstring tag = L"") : m_p(p), m_tag(tag) {}
	~LeaveCriticalSectionOnExit() {
		LeaveCriticalSection(m_p);
		if (!m_tag.empty()) {
			// Logging intentionally remains disabled as in the donor.
		}
	}

private:
	CRITICAL_SECTION* m_p;
	std::wstring m_tag;
};

class PropVariantClearOnExit {
public:
	PropVariantClearOnExit(PROPVARIANT* p) : m_p(p) {}
	~PropVariantClearOnExit() {
		HRESULT hr = PropVariantClear(m_p);
		if (FAILED(hr)) {
			LOG_ERROR(L"PropVariantClear failed: hr = 0x%08x", hr);
		}
	}

private:
	PROPVARIANT* m_p;
};

class ReleaseOnExit {
public:
	ReleaseOnExit(IUnknown* p) : m_p(p) {}
	~ReleaseOnExit() {
		SafeRelease(&m_p);
	}

private:
	IUnknown* m_p;
};

class ReleaseWWMFSampleDataOnExit {
public:
	ReleaseWWMFSampleDataOnExit(WWMFSampleData* p) : m_p(p) {}
	~ReleaseWWMFSampleDataOnExit() {
		m_p->Release();
	}

private:
	WWMFSampleData* m_p;
};

class ForgetWWMFSampleDataOnExit {
public:
	ForgetWWMFSampleDataOnExit(WWMFSampleData* p) : m_p(p) {}
	~ForgetWWMFSampleDataOnExit() {
		m_p->Forget();
	}

private:
	WWMFSampleData* m_p;
};
