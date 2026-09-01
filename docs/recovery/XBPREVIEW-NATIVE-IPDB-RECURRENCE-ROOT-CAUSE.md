# XbPreview.Native IPDB recurrence root-cause audit

Status: **XBPREVIEW-IPDB-RECURRENCE-FIX-READY**

Audit date: 2026-08-14 (Asia/Shanghai)

Frozen baseline:

- worktree: `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow`
- branch: `recovery/window-capture-fhero-rebuild`
- HEAD: `35da476283c22f1f7659ee9d41f5f52b1f70daaf`
- parent: `c015e985e4149c6f8ab9a8e3efa46dd021190f29`
- annotated tag at HEAD: `window-stage-layer4-motion-pass-2026-08-14`
- initial worktree state: clean

## Executive verdict

The recurring failure is category **A — LTCG-INCREMENTAL-STATE** with high confidence.

`XbPreview.Native` Release x64 had `WholeProgramOptimization=true`. In the installed VS 2022 v143 property sheets, that setting is imported as `UseFastLinkTimeCodeGeneration` on x64. The effective compiler/linker invocations were therefore `/GL` plus `/LTCG:INCREMENTAL /LTCGOUT:...XbPreview.Native.iobj`. That mode creates and reuses the paired `XbPreview.Native.ipdb`/`.iobj` Fast LTCG database.

The preserved database was proved unreadable in a single-project, single-node build: one unchanged-content timestamp touch compiled `Exports.cpp` with `/GL`, then the only link invocation read the existing IPDB and failed with `C1354 (corrupt ipdb)` followed by `LNK1257`. Deleting only that already-hashed IPDB made the next identical configuration fall back to a full LTCG compilation of 6,797 functions, succeed, and recreate the IPDB/IOBJ pair. This is the precise mechanism behind “delete -> one PASS -> a later Release link is corrupt”: deletion forces a one-time full compilation, but the unchanged Fast LTCG configuration recreates the persistent state that later links try to reuse.

The audit does **not** claim to identify the private byte-level event inside Microsoft's closed IPDB format that first makes a generated database unreadable. No evidence ties that event to a shared path, concurrent writer, or interrupted linker in the available record. The root cause that is both demonstrated and actionable is the build configuration's repeated creation and reuse of Fast LTCG state.

The only fix is to preserve Release `/GL` while changing the native DLL link from `/LTCG:INCREMENTAL` to full `/LTCG`, and to clear `LinkTimeCodeGenerationObjectFile` so `/LTCGOUT` is not emitted. No output-path, PDB, parallelism, clean script, cache-deletion workaround, or product source was changed.

## Microsoft behavior used as the baseline

- [`/GL` enables whole-program optimization](https://learn.microsoft.com/en-us/cpp/build/reference/gl-whole-program-optimization?view=msvc-170); `/GL` objects are intended to be linked with LTCG.
- [`/LTCG:INCREMENTAL` reoptimizes affected files](https://learn.microsoft.com/en-us/cpp/build/reference/ltcg-link-time-code-generation?view=msvc-170) and is explicitly different from ordinary incremental linking. Microsoft also directs users who remove `/LTCG:INCREMENTAL` to remove `/LTCGOUT`.
- [`/LTCGOUT` names the `.iobj` used by incremental LTCG](https://learn.microsoft.com/en-us/cpp/build/reference/ltcgout?view=msvc-170). Without an explicit path, the target base name is used.
- [Ordinary `/INCREMENTAL`](https://learn.microsoft.com/en-us/cpp/build/reference/incremental-link-incrementally?view=msvc-170) uses an `.ilk` database and is a separate mechanism. It was not present in the effective Release x64 link command here.
- [`/DEBUG` and `/PDB`](https://learn.microsoft.com/en-us/cpp/build/reference/debug-generate-debug-info?view=msvc-170) govern the final program PDB. That `XbPreview.Native.pdb` is distinct from the Fast LTCG `XbPreview.Native.ipdb` involved in this failure.

Installed-toolset provenance also confirms the default:

```text
Microsoft.Cpp.WholeProgramOptimization.props:
  WholeProgramOptimization=true + x64
  -> LinkTimeCodeGeneration=UseFastLinkTimeCodeGeneration

Microsoft.Link.Common.props:
  LinkTimeCodeGenerationObjectFile
  -> $(IntDir)$(TargetName).iobj when LTCGOUT is supported
```

## Failure timeline

1. Layer 1 recovery recorded removal of the IPDB/IOBJ cache pair, followed by a successful full LTCG Release build.
2. Layer 3 recovery recorded the same corrupt `XbPreview.Native.ipdb`; deleting only IPDB caused full compilation and a successful new DLL while IOBJ was retained.
3. Layer 4 recorded a successful Release x64 DLL at 2026-08-14 00:25 and explicitly deferred the recurring IPDB root-cause audit.
4. At audit start, the IPDB/IOBJ from that successful build existed with 00:25 timestamps. No MSBuild, link, cl, or mspdbsrv process was running.
5. Build experiment 1 at 00:53 forced one real native compile without changing source content. The subsequent Fast LTCG link failed on the preserved IPDB with C1354/LNK1257.
6. The already-hashed IPDB was copied once to `E:\小白录屏器\recovery-snapshots\2026-08-14-ipdb-audit\XbPreview.Native.pre-audit.ipdb`, with matching size and SHA-256.
7. Build experiment 2 at 00:55 deleted only that exact IPDB. Link reported `Previous IPDB not found, fall back to full compilation`, compiled all 6,797 functions, produced the DLL, and recreated IPDB/IOBJ.
8. The minimal project configuration fix was applied.
9. Build experiments 3 and 4 at 00:57 and 00:58 each timestamp-touched the same tracked `.cpp` without changing its SHA-256. Both performed a real `/GL` compile and `/LTCG` link and passed. The legacy IPDB/IOBJ were byte-for-byte and timestamp-for-timestamp unchanged across both links.

## Original artifact evidence

Evidence was captured before any cache deletion:

| Artifact | Full path | Size | LastWriteTime (+08:00) | SHA-256 |
|---|---|---:|---|---|
| IPDB | `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow\artifacts\obj\XbPreview.Native\Release\x64\XbPreview.Native.ipdb` | 11,999,328 | `2026-08-14 00:25:22.2027721` | `643DCCFD1410CD6330CFF9E71315E573DE9FCB016483447696CC974825583896` |
| IOBJ | `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow\artifacts\obj\XbPreview.Native\Release\x64\XbPreview.Native.iobj` | 19,870,640 | `2026-08-14 00:25:22.5787633` | `F2BF34161F6515DDFE19540E81C41C3B1C381D9FAD1C3446C477842F76C0B822` |
| DLL | `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow\artifacts\bin\Release\x64\XbPreview.Native.dll` | 765,440 | `2026-08-14 00:25:25.3984716` | `35E9BEEC082ED1AB1EDB82B3CFBE9B756717CE8320AFC420C82608EF3615B41D` |

The snapshot copy was 11,999,328 bytes with the same
`643DCCFD1410CD6330CFF9E71315E573DE9FCB016483447696CC974825583896`
SHA-256.

## Effective Release x64 configuration before the fix

The values below come from diagnostic MSBuild task parameters, the imported property sheets, and the actual CL/LINK tlogs—not from the project file alone.

| Requested value | Effective value before fix |
|---|---|
| `WholeProgramOptimization` | `true` |
| `LinkTimeCodeGeneration` | `UseFastLinkTimeCodeGeneration` |
| `LinkTimeCodeGenerationObjectFile` | `$(IntDir)XbPreview.Native.iobj` |
| `LinkIncremental` | empty/false for Release; no `/INCREMENTAL` switch emitted |
| `GenerateDebugInformation` | `DebugFull` |
| `ProgramDatabaseFile` | `$(OutDir)XbPreview.Native.pdb` |
| `IntermediateDirectory` | empty alias; canonical effective property is `IntDir` below |
| `IntDir` | `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow\artifacts\obj\XbPreview.Native\Release\x64\` |
| `OutDir` | `E:\小白录屏器\xiaobai-screen-recorder\.codex-tmp\window-stage-layer2-card-shadow\artifacts\bin\Release\x64\` |
| `TargetName` | `XbPreview.Native` |
| `TargetExt` | `.dll` |

Actual compiler state:

```text
/Zi /O2 /GL
/Fd:...\artifacts\obj\XbPreview.Native\Release\x64\vc143.pdb
```

Actual linker state before the fix:

```text
/DEBUG:FULL
/PDB:...\artifacts\bin\Release\x64\XbPreview.Native.pdb
/OPT:REF /OPT:ICF
/LTCG:incremental
/LTCGOUT:...\artifacts\obj\XbPreview.Native\Release\x64\XbPreview.Native.iobj
```

Switch audit before the fix:

| Switch | Present? |
|---|---|
| `/GL` | yes |
| `/LTCG` full | no |
| `/LTCG:INCREMENTAL` | yes |
| `/LTCGOUT` | yes |
| `/INCREMENTAL` | no |
| `/INCREMENTAL:NO` | no explicit switch |
| `/PDB` | yes |
| `/DEBUG:FULL` | yes |

## Path-sharing audit

Category **B — SHARED-INTERMEDIATE-PATH** is not supported by the evidence.

- `IntDir` contains project name, configuration, and platform.
- It is rooted through `$(ProjectDir)..`, so each registered worktree resolves to a different absolute repository-local `artifacts` tree.
- The four roots registered by the main repository plus this audit worktree resolve to five distinct absolute `IntDir` and `OutDir` roots.
- Other native projects use their own project-name/configuration/platform intermediate directories. The static library dependency uses `third_party\screenrecorderlib-audio\obj\x64\Release`, not the DLL's directory.
- No other project or target was found writing the audited absolute `XbPreview.Native.ipdb`, `.iobj`, final `.pdb`, or DLL path.

The template uses a literal `x64` rather than `$(Platform)`, but this project only declares x64, and the resolved path is unique. There is no evidence-based reason to alter it in this fix.

## Concurrent-writer audit

Category **C — CONCURRENT-WRITER** is not supported by the evidence.

- No MSBuild, link, cl, or mspdbsrv process existed at initial capture.
- The solution contains exactly one `XbPreview.Native` project entry.
- No custom target invokes MSBuild or link for `XbPreview.Native`; its custom targets only validate/stage runtimes.
- The decisive failure was reproduced by building `XbPreview.Native.vcxproj` directly with `/m:1`. The log contains one affected `link.exe` invocation.
- No MSBuild, link, or cl process remained after the failed link or either final validation link. At final handoff, two `mspdbsrv.exe` service processes spawned by the two fixed builds were still idle/resident; the actual links contained no `/LTCGOUT`, and the legacy IPDB/IOBJ timestamps and hashes did not change, so these were not writers of the audited Fast LTCG state. They were recorded and not forcibly terminated.

This does not prove that no historical process has ever been interrupted or overlapped. It proves that a concurrent writer was unnecessary for the observed failure and that no path/target evidence identifies one.

## Interrupted-write audit

Category **D — INTERRUPTED-LINK-WRITE** is not supported by the available evidence.

- No surviving local log records a timeout, cancellation, killed link, or external termination that created the preserved 00:25 database.
- No build process was active at audit start or after the controlled links.
- Build experiment 1 failed by reading the already-existing IPDB; it did not modify the IPDB or IOBJ (size, timestamp, and hashes stayed equal to the initial capture).

An interrupted historical write cannot be disproved from a closed database alone, but there is no positive evidence assigning the recurrence to it, so it is not the root-cause classification.

## Controlled reproduction

Exactly four Release/native link experiments were executed—the allowed maximum, including the final post-fix confirmation. No product gate, LongRun, human smoke, audio gate, window-capture gate, DPI test, or application matrix was run.

| # | State and action | Effective link | Result |
|---:|---|---|---|
| 1 | Preserved original state; SHA-preserving timestamp touch of `Exports.cpp`; direct project `/m:1` | `/LTCG:INCREMENTAL /LTCGOUT:...iobj` | **FAIL**: C1354 corrupt IPDB, then LNK1257; no DLL output from this link |
| 2 | Delete only the hashed/snapshotted IPDB; retain IOBJ and other cache | `/LTCG:INCREMENTAL /LTCGOUT:...iobj` | **PASS**: no prior IPDB, full fallback, all 6,797 functions compiled, DLL and a new IPDB/IOBJ generated |
| 3 | Apply the one configuration fix; retain the newly created IPDB/IOBJ; SHA-preserving touch | `/LTCG` | **PASS**: real compile/link; DLL generated; legacy IPDB/IOBJ untouched |
| 4 | Second consecutive SHA-preserving touch, no cleanup | `/LTCG` | **PASS**: real compile/link; DLL generated; legacy IPDB/IOBJ again untouched |

The touched `Exports.cpp` content hash remained
`FCCA0BAEFD4D9EEB8821E8E0900318ED8B26CB082740BA569BE7DC34D2DE8CFA`,
and `git diff -- XbPreview.Native/Exports.cpp` remained empty.

Diagnostic logs (ignored build artifacts) are retained at:

- `artifacts/ipdb-audit-build1-existing-state.log`
- `artifacts/ipdb-audit-build2-ipdb-only-recovery.log`
- `artifacts/ipdb-audit-build3-fixed-full-ltcg.log`
- `artifacts/ipdb-audit-build4-final-consecutive.log`
- `artifacts/ipdb-audit-effective-project.xml`

## Root-cause classification and confidence

| Category | Finding |
|---|---|
| A. LTCG-INCREMENTAL-STATE | **Confirmed; high confidence.** Effective `/LTCG:INCREMENTAL` creates/reuses the exact state that fails. Removing IPDB alone forces full fallback and success. Removing the state dependency makes two real consecutive links pass without touching the state files. |
| B. SHARED-INTERMEDIATE-PATH | Not supported. Absolute per-worktree/per-project/config/platform paths are distinct. |
| C. CONCURRENT-WRITER | Not supported. No live writer, one solution entry, no custom recursive build, and direct `/m:1` reproduction. |
| D. INTERRUPTED-LINK-WRITE | No positive evidence. Possible in the abstract, not attributable from the available record. |
| E. TOOLCHAIN-DEFECT / OTHER | Not needed as the primary classification. The internal reason a particular Microsoft IPDB becomes unreadable remains opaque, but the build's Fast LTCG state dependency is demonstrated and removable. |

Confidence is **high (0.90)** for the actionable mechanism and fix. Confidence is deliberately **undetermined** for the private internal byte-level corruption trigger; the evidence does not justify inventing one.

## The only implemented fix

File: `XbPreview.Native/XbPreview.Native.vcxproj`

```xml
<LinkTimeCodeGeneration Condition="'$(Configuration)|$(Platform)'=='Release|x64'">UseLinkTimeCodeGeneration</LinkTimeCodeGeneration>
<LinkTimeCodeGenerationObjectFile Condition="'$(Configuration)|$(Platform)'=='Release|x64'" />
```

This overrides the VS2022 x64 Fast LTCG default only for this project's Release x64 link. `WholeProgramOptimization=true` remains in place, so `/GL`, `/O2`, `/OPT:REF`, and `/OPT:ICF` remain enabled. The empty LTCG object metadata removes `/LTCGOUT` as required by Microsoft when incremental LTCG is removed.

No auto-delete workaround was added.

## Effective command comparison

Before:

```text
cl.exe ... /O2 /GL ...
link.exe ... /DEBUG:FULL /PDB:...XbPreview.Native.pdb /OPT:REF /OPT:ICF
  /LTCG:incremental /LTCGOUT:...XbPreview.Native.iobj ...
```

After:

```text
cl.exe ... /O2 /GL ...
link.exe ... /DEBUG:FULL /PDB:...XbPreview.Native.pdb /OPT:REF /OPT:ICF
  /LTCG ...
```

Final effective link task metadata:

```text
GenerateDebugInformation=DebugFull
LinkTimeCodeGeneration=UseLinkTimeCodeGeneration
LinkTimeCodeGenerationObjectFile=
ProgramDatabaseFile=...\artifacts\bin\Release\x64\XbPreview.Native.pdb
```

No `/LTCG:INCREMENTAL`, `/LTCGOUT`, `/INCREMENTAL`, or `/INCREMENTAL:NO` appears in the final actual linker command.

## Post-fix validation and final artifacts

- Release x64 direct native build after fix: **PASS**.
- Second consecutive real native compile/link after fix: **PASS**.
- Final DLL:
  - size: 793,600 bytes
  - LastWriteTime: `2026-08-14 00:58:18.5565559 +08:00`
  - SHA-256: `482BC507D491CAD6FC5B5CE293105432C0F09D48260B01112CDA17AE549CC066`
- `/GL`: retained.
- `/LTCG`: present.
- `/LTCG:INCREMENTAL`: absent.
- `/LTCGOUT`: absent.
- final program PDB: still generated normally through `/DEBUG:FULL /PDB`.

IPDB/IOBJ status deserves an exact distinction: the pair generated by recovery experiment 2 still exists on disk because the fix does not perform destructive cleanup, but neither fixed build generated, read, nor modified it. Across builds 3 and 4:

| Artifact | Size | LastWriteTime (+08:00) | SHA-256 | Fixed builds changed it? |
|---|---:|---|---|---|
| `XbPreview.Native.ipdb` | 11,999,328 | `2026-08-14 00:55:06.9920058` | `190AB2BA72B80EF890EE0DD1599FAA553A4194470D8BDE29D6BC787CB4AB25B8` | no |
| `XbPreview.Native.iobj` | 19,870,640 | `2026-08-14 00:55:07.3722289` | `6CE84701BE03C1F28E50C322769DAF442C427E9245BB74E3605C09DB88827445` | no |

Therefore future full-LTCG builds do not depend on this incremental state. A normal future Clean may remove the stale files through standard MSBuild clean rules; no special deletion target is needed.

## Source and Git integrity

- Product source diff (`*.cpp`, `*.c`, `*.cc`, `*.h`, `*.hpp`, `*.cs`, `*.xaml`): **0 files**.
- The only build configuration change is `XbPreview.Native/XbPreview.Native.vcxproj`.
- This report is the only documentation addition.
- Timestamp touches changed no source content and produced no source diff.
- `git diff --check`: **PASS** (exit code 0).
- No commit, tag, or push was performed.

Final handoff state: **ROOT-CAUSE + FIX READY**.
