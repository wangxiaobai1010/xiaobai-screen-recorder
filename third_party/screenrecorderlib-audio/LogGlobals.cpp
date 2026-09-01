// SPDX-License-Identifier: MIT
// Copyright (c) 2017 Sverre Skodje
//
// Mechanically extracted from ScreenRecorderLib v6.6.0 RecordingManager.cpp
// so the audio-only static library does not depend on the donor video graph.

#include "Log.h"

#if _DEBUG
bool isLoggingEnabled = true;
int logSeverityLevel = LOG_LVL_TRACE;
#else
bool isLoggingEnabled = false;
int logSeverityLevel = LOG_LVL_INFO;
#endif

std::wstring logFilePath;
