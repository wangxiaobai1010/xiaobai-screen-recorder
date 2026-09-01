# P1c Cursor 资源所有权

| 资源 | 创建/取得 | 唯一所有者 | 释放 |
|---|---|---|---|
| `CURSORINFO.hCursor` | `GetCursorInfo` | 系统；P1c 只借用当前帧 | 不释放 |
| copied `HICON/HCURSOR` | `CopyIcon` | `OwnedIcon`，只存在于一次转换 | `DestroyIcon` |
| `ICONINFO.hbmMask` | `GetIconInfo` | `OwnedBitmap` | `DeleteObject` |
| `ICONINFO.hbmColor` | `GetIconInfo` | `OwnedBitmap` | `DeleteObject` |
| memory DC | `CreateCompatibleDC` | `OwnedDc` | `DeleteDC` |
| DIB CPU buffer | `std::vector` | 单个 `CursorShape` | cache eviction / Clear |
| cached shape | `shared_ptr<const CursorShape>` | 32 项 LRU 与当前 draw command | eviction / Stop `Clear` |
| cursor GPU texture/SRV | `CustomCursorRenderer::EnsureTexture` | renderer 当前 shape generation | shape change / `Shutdown` |
| cursor shaders/states/buffer | `CustomCursorRenderer::Initialize` | renderer | `Shutdown` |
| WGC session | `CreateCaptureSession` | `PreviewEngine` render worker | 原 Stop/Destroy 顺序中的 `Close` |
| cursor JSONL stream/thread | `CursorDiagnosticLogger::Open` | logger | `Close` 排空队列、join、close |

## 关键规则

1. `GetIconInfo` 每次成功都会新建 mask/color bitmap；两者即使转换中途失败也由 RAII 删除。
2. copied icon 不进入 cache。cache 只保存独立的 CPU BGRA 数据，不延长系统 cursor handle 生命周期。
3. source cursor handle 仅作为 cache key；不调用 `DestroyCursor`。
4. GPU texture 为 immutable BGRA8，只在 shape id/generation 改变时创建。
5. Stop 和 Destroy 都经过 `ShutdownWorkerResources`；cursor cache、GPU 资源和日志线程均可重复清理。
6. 测试合成 cursor 由测试自己 `CreateCursor`/`DestroyCursor`，不属于产品运行路径。
