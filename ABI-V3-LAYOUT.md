# XbPreview ABI v3 布局

## 版本

```text
XB_PREVIEW_API_VERSION = 0x00030000
calling convention      = __stdcall
architecture            = x64
packing                 = 8
wchar_t                  = 2 bytes
```

v3 保留全部旧导出名称及 camera 调用约定。由于 cursor 模式必须在 Start 前协商，主版本从 v2 升为 v3，旧 v2 client 会被 major-version gate 拒绝，不会误用新 DLL。

## 保持不变

| 结构 | 大小 |
|---|---:|
| `XbPreviewCreateOptions` | 72 |
| `XbPreviewStats` | 1080 |
| `XbPreviewAbiLayout` | 40 |
| `XbCameraState` | 120 |
| `XbLetterboxRect` | 16 |

`XbPreviewAbiLayout` 最后一个原保留字段改名为 `cursorStatsSize`，结构大小和此前字段偏移不变。

`XbCameraState` 关键偏移保持：

| 字段 | 偏移 |
|---|---:|
| `sequence` | 8 |
| `zoom` | 32 |
| `targetX` | 64 |

## 新结构：XbCursorStats

总大小：944 bytes。

关键偏移：

| 字段 | 偏移 |
|---|---:|
| `requestedMode` | 8 |
| `cursorSequence` | 72 |
| `sourceX` | 200 |
| `shapeId` | 360 |
| `logFilePath` | 392 |

结构包含：

- requested/actual/fallback；
- WGC cursor property、system/custom 所有权；
- sample/draw/skip/cache/upload/fallback/error 计数；
- screen/source/camera/output/viewport 坐标；
- shape id/generation/size/hotspot；
- cursor render duration；
- `p1c-cursor-*.jsonl` 路径。

## 新导出

```cpp
XbPreview_SetCursorMode(handle, XbCursorMode)
XbPreview_GetCursorStats(handle, XbCursorStats*)
```

`SetCursorMode` 只接受 Stopped；Running/Starting/Stopping 返回 `InvalidState`。旧 camera exports、stats、letterbox、Stop/Destroy 均保留。

Native/C# 双方通过 `sizeof`、`offsetof`/`Marshal.OffsetOf` 和 `XbPreview_GetAbiLayout` 自动测试锁定上述布局。
