// SPDX-License-Identifier: MIT
// Copyright (c) 2017 Sverre Skodje
//
// Mechanically extracted audio-only support from ScreenRecorderLib v6.6.0
// util.h. Macro and UTF-8 conversion behavior are unchanged.

#pragma once

#include <Windows.h>
#include <comdef.h>

#include "log.h"

#include <string>

#define RETURN_ON_BAD_HR(expr) \
{ \
    HRESULT _hr_ = (expr); \
    if (FAILED(_hr_)) { \
    {\
        _com_error err(_hr_);\
        LOG_ERROR(L"RETURN_ON_BAD_HR: hr=0x%08x, error is: %ls", _hr_, err.ErrorMessage());\
    }\
        return _hr_; \
    } \
}

inline std::wstring s2ws(const std::string& str)
{
	if (str.empty()) return std::wstring();
	int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0], static_cast<int>(str.size()), NULL, 0);
	std::wstring wstrTo(size_needed, 0);
	MultiByteToWideChar(CP_UTF8, 0, &str[0], static_cast<int>(str.size()), &wstrTo[0], size_needed);
	return wstrTo;
}
