[CmdletBinding()]
param(
    [ValidateSet('Baseline', 'SpikeA1', 'SpikeA2', 'P2_3A', 'P2_3B', 'P2_4', 'PreAcceptance', 'PostAcceptance')]
    [string]$Lifecycle = 'Baseline'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedDirectoryName = 'p2-video-recording-closed-loop-prototype'
$P24ExpectedDirectoryName = 'p2-4-outputcanvas-encoding-prototype'
$P23BFrozenDirectoryName = 'p2-video-recording-closed-loop-prototype'
$SourceDirectoryName = 'p1d-a2-region-crop-preview-prototype'
$P1dA1DirectoryName = 'p1d-a1-region-selection-prototype'

$ExpectedSourceCandidateFingerprint =
    '90F2066AAE76C189A90C4D451F5855A88DCB813997D70F67E832AA73C7BD6F86'
$ExpectedSourceRuntimeFingerprint =
    '6BD6377D53A87F5176103BF7BAB5EE020D576353A243A425E0CC0A776875A75B'
$ExpectedSourceSealSha256 =
    '55720A0789AE898DB1E62AB8B80D034C35C39F162C33BDD8CF19EF475C66C2B5'
$ExpectedSourceReportSha256 =
    '11F0320FCB9D3093CE2572DA105E907A12972012BE14E736A579445D0912AE4E'
$ExpectedSourceFrozenPayloadFingerprint =
    '761E9880D7993CF4D21A817536133797556713801D64B2FED4E7A57DA9C694A7'
$ExpectedSourceManifestSha256 =
    '0121F1D013F3DA93CEE839FE0B35CE40D131F366A5CED54A9F7621D218F99719'
$ExpectedSourceRecordSha256 =
    '6D998F4E3A72F4ED56800FF1E99DF1E372D330B818EAFC9E5CC9BDE513450B4F'
$ExpectedP1dA1ManifestSha256 =
    '7CA80A77641B99295800B85033BF66D051727BE987EBA394997FE10C29B850CD'

$UpstreamMetadataNames = @(
    'P1D-A2-PREACCEPTANCE-SEAL.json',
    'P1D-A2-ACCEPTANCE-REPORT.md',
    'P1D-A2-FREEZE-MANIFEST.json',
    'P1D-A2-FREEZE-RECORD.md'
)
$P2StageFiles = @(
    'P2-UPSTREAM-ORIGIN.json',
    'VERIFY-P2-STATIC.ps1',
    'SELF-TEST-P2.bat',
    'P2.1-BASELINE-ORIGIN.md'
)
$SpikeA1AddedFiles = @(
    'P2.2A-SPIKE-A1-IMPLEMENTATION.md',
    'spikes/P2.2A-MfSinkWriterGpuFrame/MfSinkWriterSession.cpp',
    'spikes/P2.2A-MfSinkWriterGpuFrame/MfSinkWriterSession.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/Mp4Validator.cpp',
    'spikes/P2.2A-MfSinkWriterGpuFrame/Mp4Validator.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/P2.SpikeA.MfSinkWriterGpuFrame.vcxproj',
    'spikes/P2.2A-MfSinkWriterGpuFrame/RUN-P2.2A-SPIKE.ps1',
    'spikes/P2.2A-MfSinkWriterGpuFrame/SpikeCommon.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/SpikeConfig.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/SpikeDiagnostics.cpp',
    'spikes/P2.2A-MfSinkWriterGpuFrame/SpikeDiagnostics.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/TrackedTexturePool.cpp',
    'spikes/P2.2A-MfSinkWriterGpuFrame/TrackedTexturePool.h',
    'spikes/P2.2A-MfSinkWriterGpuFrame/main.cpp'
)
$ExpectedP2StartingCandidateFingerprint =
    'F54C2FB948077905ACC8C65D6E5BB032BA9D012A625C501098B789BD1371EA30'
$ExpectedP2BaselinePayloadFingerprint =
    '680657842F67C364D64FFADB81388157E72AF467DDB47D810776457BCBF062C6'
$ExpectedSpikeA1CandidateFingerprint =
    '0D363CCC4816EFCD92CACBBFD9E868B60C5BC888827716F14BEEB6CAE7DCDC93'
$ExpectedSpikeA1VerifyHash =
    '2DAF841B827BA8A08DA264BFFE65076D4B20CCA2D02DEE922E8AECD85F81B380'
$ExpectedSpikeA1SelfTestHash =
    '830474B37E7B39238EDF0CF1AF00F4704A75603FBC67381C729D4F373DC803A6'
$SpikeA2AddedFiles = @(
    'P2.2B-SPIKE-A2-IMPLEMENTATION.md',
    'spikes/P2.2B-D3D11VideoProcessorNv12/P2.SpikeA2.D3D11VideoProcessorNv12.vcxproj',
    'spikes/P2.2B-D3D11VideoProcessorNv12/RUN-P2.2B-SPIKE.ps1',
    'spikes/P2.2B-D3D11VideoProcessorNv12/main.cpp'
)
$ExpectedSpikeA2CandidateFingerprint =
    '5B051F02CB14F6A870101601C227BC5977EC5314932DD8284112AFD097A4AFE1'
$ExpectedSpikeA2VerifyHash =
    'D8DB19F64E4CB15696016127FD30FDCA6C6FB5D77ABC56932EE585ACA17CFE42'
$ExpectedSpikeA2SelfTestHash =
    'D0BBCD8BF7646C08BC75BFD89F2B1AD2CEF7AD254C56CBC95B63BD98320E8126'
$P23AAddedFiles = @(
    'P2.3A-OUTPUT-CANVAS-IMPLEMENTATION.md',
    'XbPreview.Native/OutputCanvasTarget.cpp',
    'XbPreview.Native/OutputCanvasTarget.h'
)
$P23AInheritedModifiedPaths = @(
    'XbPreview.Native.Tests/NativeTests.cpp',
    'XbPreview.Native/CropTransform.h',
    'XbPreview.Native/PreviewEngine.cpp',
    'XbPreview.Native/PreviewRenderer.cpp',
    'XbPreview.Native/PreviewRenderer.h',
    'XbPreview.Native/XbPreview.Native.vcxproj'
)
$P23ARuntimeModifiedPaths = @(
    'XbPreview.Native/CropTransform.h',
    'XbPreview.Native/PreviewEngine.cpp',
    'XbPreview.Native/PreviewRenderer.cpp',
    'XbPreview.Native/PreviewRenderer.h',
    'XbPreview.Native/XbPreview.Native.vcxproj'
)
$P23ARuntimeAddedFiles = @(
    'XbPreview.Native/OutputCanvasTarget.cpp',
    'XbPreview.Native/OutputCanvasTarget.h'
)
$ExpectedP23ACandidateFingerprint =
    '20DDC26FA0A1291B9D79E15FB7BFDD011FC60D7D87DF51CE3A965293B6EB0015'
$ExpectedP23ARuntimeFingerprint =
    '20EE3B5928FCE7C881479A6D77F3DC83D9AC8B507314D7CC3DCA259F2F73EFB9'
$ExpectedP23AVerifyHash =
    '60C6A4F683FD6E6B23E4F6208D2EBAF01BB35A4A6914BD6094A739388131FFC9'
$ExpectedP23ASelfTestHash =
    '34EE07D8CCF4CFE2A7E19A081BBEB2E96C79D56E95E62CD7E52BA73B965D5984'
$P23AOriginalHashByPath = @{
    'XbPreview.Native.Tests/NativeTests.cpp' =
        '0E46B2E91444AF226B73FEF119AFD612BFBDD2DAC37CCFFB2B3EA631C6325184'
    'XbPreview.Native.Tests/XbPreview.Native.Tests.vcxproj' =
        'A4C2C457187B0DEAAD62FFF4B4C6F4DFFE017747A07BA693835EB43C82EA04AB'
    'XbPreview.Native/CropTransform.h' =
        'AB4BF66F2C4C9432B99313591B06162211B4C150F48CA7BD9229BD2FBB483624'
    'XbPreview.Native/PreviewEngine.cpp' =
        'C0665EFBB9B80C53DD935EE72E4BD8BF26616AC33F31B38A098C0BC1A400299F'
    'XbPreview.Native/PreviewEngine.h' =
        'EB13E192D1358CD7C9F7A587977CC4BA2ABBFA04BB65C07BF63AAA955383D329'
    'XbPreview.Native/PreviewRenderer.cpp' =
        '37765FF1501D462459594653A3AF7DE2A095720C7CE04D5F300DD4761A233386'
    'XbPreview.Native/PreviewRenderer.h' =
        'AEFB79757CB8653BD768F344EF00E1757A55AA67C2034FC1A183A3F010E32B4A'
    'XbPreview.Native/XbPreview.Native.vcxproj' =
        '1495DE0994F30CB8C1BA930206B36415E1B4706D517083F541162ED8A4909024'
}
$P23BAddedFiles = @(
    'P2.3B-RENDER-FRAME-TAP-IMPLEMENTATION.md',
    'XbPreview.Native/RenderFrameTap.cpp',
    'XbPreview.Native/RenderFrameTap.h'
)
$P23BInheritedModifiedPaths = @(
    'XbPreview.Native.Tests/NativeTests.cpp',
    'XbPreview.Native.Tests/XbPreview.Native.Tests.vcxproj',
    'XbPreview.Native/CropTransform.h',
    'XbPreview.Native/PreviewEngine.cpp',
    'XbPreview.Native/PreviewEngine.h',
    'XbPreview.Native/PreviewRenderer.cpp',
    'XbPreview.Native/PreviewRenderer.h',
    'XbPreview.Native/XbPreview.Native.vcxproj'
)
$P23BRuntimeModifiedPaths = @(
    'XbPreview.Native/CropTransform.h',
    'XbPreview.Native/PreviewEngine.cpp',
    'XbPreview.Native/PreviewEngine.h',
    'XbPreview.Native/PreviewRenderer.cpp',
    'XbPreview.Native/PreviewRenderer.h',
    'XbPreview.Native/XbPreview.Native.vcxproj'
)
$P23BRuntimeAddedFiles = @(
    'XbPreview.Native/OutputCanvasTarget.cpp',
    'XbPreview.Native/OutputCanvasTarget.h',
    'XbPreview.Native/RenderFrameTap.cpp',
    'XbPreview.Native/RenderFrameTap.h'
)
$FutureP2Metadata = @(
    'P2-PREACCEPTANCE-SEAL.json',
    'P2-ACCEPTANCE-REPORT.md',
    'P2-FREEZE-MANIFEST.json',
    'P2-FREEZE-RECORD.md'
)

function Get-UpperSha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToUpperInvariant()
}

function Get-OrdinalSorted([string[]]$Values) {
    $copy = [string[]]@($Values)
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    return $copy
}

function Test-OrdinalEqual([string[]]$Left, [string[]]$Right) {
    if ($Left.Count -ne $Right.Count) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Count; $index++) {
        if (-not [string]::Equals(
                $Left[$index],
                $Right[$index],
                [StringComparison]::Ordinal)) {
            return $false
        }
    }
    return $true
}

function Assert-CanonicalRelativePath([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains('\') -or
        $RelativePath.Contains(':') -or
        $RelativePath.StartsWith('/') -or
        $RelativePath.EndsWith('/') -or
        $RelativePath.Contains('//')) {
        throw "Malformed relative path: $RelativePath"
    }
    foreach ($segment in $RelativePath.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or
            $segment -eq '.' -or
            $segment -eq '..') {
            throw "Malformed relative path segment: $RelativePath"
        }
    }
}

function Get-RelativePath([string]$Root, [string]$FullName) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $fileFull = [IO.Path]::GetFullPath($FullName)
    $prefix = $rootFull + '\'
    if (-not $fileFull.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $fileFull"
    }
    $relative = $fileFull.Substring($prefix.Length).Replace('\', '/')
    Assert-CanonicalRelativePath $relative
    return $relative
}

function Test-IsGeneratedPath([string]$RelativePath) {
    foreach ($segment in $RelativePath.Split('/')) {
        if ($segment -ieq 'artifacts' -or
            $segment -ieq 'bin' -or
            $segment -ieq 'obj' -or
            $segment -ieq '.vs') {
            return $true
        }
    }
    return $false
}

function Get-FormalPhysicalPaths([string]$Root) {
    $paths = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object {
            Get-RelativePath $Root $_.FullName
        } |
        Where-Object {
            -not (Test-IsGeneratedPath $_)
        }
    )
    return Get-OrdinalSorted ([string[]]$paths)
}

function Get-FileEntries([string]$Root, [string[]]$RelativePaths) {
    $entries = @()
    foreach ($relative in (Get-OrdinalSorted $RelativePaths)) {
        Assert-CanonicalRelativePath $relative
        $entries += [pscustomobject][ordered]@{
            RelativePath = $relative
            Sha256 = Get-UpperSha256 (Join-Path $Root $relative)
        }
    }
    return [object[]]@($entries)
}

function Get-PathNulHashFingerprint([object[]]$Entries) {
    $stream = [IO.MemoryStream]::new()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $byPath = @{}
        foreach ($entry in $Entries) {
            $path = [string]$entry.RelativePath
            if ($byPath.ContainsKey($path)) {
                throw "Duplicate fingerprint entry: $path"
            }
            $byPath.Add($path, $entry)
        }
        $paths = Get-OrdinalSorted ([string[]]@(
            $Entries | ForEach-Object { [string]$_.RelativePath }))
        foreach ($path in $paths) {
            $entry = $byPath[$path]
            $row = (
                $path +
                [char]0 +
                ([string]$entry.Sha256).ToUpperInvariant() +
                "`n")
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($row)
            $stream.Write($bytes, 0, $bytes.Length)
        }
        $stream.Position = 0
        return ([BitConverter]::ToString(
            $sha.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Get-LegacyEntriesFingerprint([object[]]$Entries) {
    $byPath = @{}
    foreach ($entry in $Entries) {
        $path = [string]$entry.RelativePath
        if ($byPath.ContainsKey($path)) {
            throw "Duplicate legacy fingerprint entry: $path"
        }
        $byPath.Add($path, $entry)
    }
    $paths = Get-OrdinalSorted ([string[]]@(
        $Entries | ForEach-Object { [string]$_.RelativePath }))
    $rows = @(
        $paths | ForEach-Object {
            $entry = $byPath[$_]
            "$(([string]$entry.Sha256).ToUpperInvariant()) *$_"
        })
    $payload = [string]::Join("`n", $rows)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash(
                [Text.UTF8Encoding]::new($false).GetBytes($payload)))).
            Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-ExpectedHash(
    [string]$Path,
    [string]$Expected,
    [string]$Description) {
    $actual = Get-UpperSha256 $Path
    if ($actual -ne $Expected.ToUpperInvariant()) {
        throw (
            "$Description SHA-256 mismatch. Expected " +
            "$($Expected.ToUpperInvariant()); actual $actual.")
    }
}

function Assert-NoResidualProcess([string]$Checkpoint) {
    $processes = @(
        Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -like 'XbPreview*' -or
            $_.ProcessName -like 'P2.SpikeA*' -or
            $_.ProcessName -like 'P2.QualityAB*'
        })
    if ($processes.Count -ne 0) {
        $details = [string]::Join(
            ', ',
            @($processes | ForEach-Object {
                "$($_.ProcessName):$($_.Id)"
            }))
        throw "$Checkpoint found residual XbPreview process(es): $details"
    }
}

function Assert-PathSets(
    [string[]]$Expected,
    [string[]]$Actual,
    [string]$Description) {
    $expectedSorted = Get-OrdinalSorted $Expected
    $actualSorted = Get-OrdinalSorted $Actual
    $missing = @(
        $expectedSorted |
        Where-Object { $actualSorted -cnotcontains $_ })
    $unexpected = @(
        $actualSorted |
        Where-Object { $expectedSorted -cnotcontains $_ })
    $caseDuplicates = @(
        $actualSorted |
        Group-Object { $_.ToLowerInvariant() } |
        Where-Object Count -gt 1)
    if ($missing.Count -ne 0 -or
        $unexpected.Count -ne 0 -or
        $caseDuplicates.Count -ne 0 -or
        -not (Test-OrdinalEqual $actualSorted (Get-OrdinalSorted $actualSorted))) {
        throw (
            "$Description path-set mismatch. Missing=$($missing.Count); " +
            "Unexpected=$($unexpected.Count); " +
            "CaseDuplicate=$($caseDuplicates.Count).")
    }
}

function Assert-Contains(
    [string]$Path,
    [string]$Pattern,
    [string]$Description) {
    $content = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    if ($content -notmatch $Pattern) {
        throw "$Description was not found in $Path"
    }
}

function Get-CodeAndProjectFiles([string]$Root) {
    $roots = @(
        'XbPreview.Host',
        'XbPreview.Managed.Tests',
        'XbPreview.Native',
        'XbPreview.Native.Tests')
    $extensions = @(
        '.cs', '.cpp', '.c', '.h', '.hpp', '.hlsl',
        '.csproj', '.vcxproj', '.props', '.targets', '.sln')
    $files = @()
    foreach ($relativeRoot in $roots) {
        $directory = Join-Path $Root $relativeRoot
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "Required source directory is missing: $directory"
        }
        $files += @(
            Get-ChildItem -LiteralPath $directory -Recurse -File |
            Where-Object {
                $extensions -contains $_.Extension.ToLowerInvariant() -and
                -not (Test-IsGeneratedPath (
                    Get-RelativePath $Root $_.FullName))
            })
    }
    $solution = Join-Path $Root 'XbPreview.P1D-A1.sln'
    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw "Inherited solution is missing: $solution"
    }
    $files += Get-Item -LiteralPath $solution
    return @($files)
}

if ($Lifecycle -eq 'P2_4') {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if ((Split-Path -Leaf $root) -cne $P24ExpectedDirectoryName) {
        throw "VERIFY-P2-STATIC P2_4 must run in $P24ExpectedDirectoryName"
    }
    $frozen = Join-Path (Split-Path -Parent $root) $P23BFrozenDirectoryName
    if (-not (Test-Path -LiteralPath $frozen -PathType Container)) {
        throw "P2.3B frozen source is missing: $frozen"
    }
    Assert-NoResidualProcess 'P2.4 static gate'

    $expectedManifestHash =
        'C05AA40DD65A0148339DB5DD3A97C6F969B79A9F94708922DDC1B4E1936E49EA'
    $expectedRecordHash =
        'C8AC3FD6438AE29D4283FF7AD4E55D30DC2EAF2DFF97A2F8CEBE4F61322DF448'
    $expectedSealHash =
        '656D6C621AAF2EF0B60E7732A552F927BA689118072A3E4BA24A40BBA67B2082'
    $expectedReportHash =
        '69F54431873A44923D5169C759374A66BF70FC6C92F989A8A799AE1AF127A122'
    $manifestPath = Join-Path $frozen 'P2-FREEZE-MANIFEST.json'
    $recordPath = Join-Path $frozen 'P2-FREEZE-RECORD.md'
    Assert-ExpectedHash $manifestPath $expectedManifestHash 'P2.3B Freeze Manifest'
    Assert-ExpectedHash $recordPath $expectedRecordHash 'P2.3B Freeze Record'
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath |
        ConvertFrom-Json
    $manifestEntries = @($manifest.Entries)
    if ([int]$manifest.FrozenPayloadFileCount -ne 201 -or
        $manifestEntries.Count -ne 201 -or
        [string]$manifest.FrozenPayloadFingerprint -ne
            '65C9578B0B2DA1D5C9ABB2D3DD2303CF6B0EF05CC819B8AE19B4A817CBA722FB' -or
        [int]$manifest.CandidateFormalFileCount -ne 199 -or
        [string]$manifest.CandidateFingerprint -ne
            'E00545D333BA803AF83D5272B72CA4FB421ED39E0F3FDF8ECA594330674B64E8' -or
        [int]$manifest.ProductRuntimeFileCount -ne 77 -or
        [string]$manifest.ProductRuntimeFingerprint -ne
            'C5108BA640323D6A305B7AF044CA54722D71FC6625AEE36DA9FCB9BD5E556490') {
        throw 'P2.3B frozen manifest identity is invalid.'
    }
    $manifestPaths = [string[]]@($manifestEntries | ForEach-Object {
        Assert-CanonicalRelativePath ([string]$_.RelativePath)
        [string]$_.RelativePath
    })
    if (@($manifestPaths | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
        throw 'P2.3B Freeze Manifest contains duplicate paths.'
    }
    $frozenEntries = @()
    foreach ($entry in $manifestEntries) {
        $path = Join-Path $frozen ([string]$entry.RelativePath)
        $actual = Get-UpperSha256 $path
        if ($actual -ne ([string]$entry.Sha256).ToUpperInvariant()) {
            throw "P2.3B frozen payload mismatch: $($entry.RelativePath)"
        }
        $frozenEntries += [pscustomobject]@{
            RelativePath = [string]$entry.RelativePath
            Sha256 = $actual
        }
    }
    if ((Get-PathNulHashFingerprint $frozenEntries) -ne
        [string]$manifest.FrozenPayloadFingerprint) {
        throw 'P2.3B frozen payload fingerprint cannot be reproduced.'
    }
    $governedFrozen = @($manifestPaths) + @(
        'P2-FREEZE-MANIFEST.json', 'P2-FREEZE-RECORD.md')
    $readOnlyCount = 0
    foreach ($relative in $governedFrozen) {
        $item = Get-Item -LiteralPath (Join-Path $frozen $relative)
        if (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
            ++$readOnlyCount
        }
    }
    if ($governedFrozen.Count -ne 203 -or $readOnlyCount -ne 203) {
        throw "P2.3B frozen source is not 203/203 ReadOnly. Actual=$readOnlyCount"
    }

    Assert-ExpectedHash (Join-Path $root 'P2-PREACCEPTANCE-SEAL.json') `
        $expectedSealHash 'Copied P2.3B PreAcceptance Seal'
    Assert-ExpectedHash (Join-Path $root 'P2-ACCEPTANCE-REPORT.md') `
        $expectedReportHash 'Copied P2.3B Acceptance Report'
    Assert-ExpectedHash (Join-Path $root 'P2-FREEZE-MANIFEST.json') `
        $expectedManifestHash 'Copied P2.3B Freeze Manifest'
    Assert-ExpectedHash (Join-Path $root 'P2-FREEZE-RECORD.md') `
        $expectedRecordHash 'Copied P2.3B Freeze Record'

    $newFormalPaths = [string[]]@(
        'P2.4Q-CROP-UV-QUALITY-AB.md',
        'P2.4-OUTPUTCANVAS-ENCODING-IMPLEMENTATION.md',
        'spikes/P2.4Q-CropUvQualityAB/main.cpp',
        'spikes/P2.4Q-CropUvQualityAB/P2.QualityAB.CropUv.vcxproj',
        'spikes/P2.4Q-CropUvQualityAB/RUN-P2.4Q-CROP-UV-AB.ps1',
        'XbPreview.Native/D3D11Nv12Converter.cpp',
        'XbPreview.Native/D3D11Nv12Converter.h',
        'XbPreview.Native/MfH264SinkWriterSession.cpp',
        'XbPreview.Native/MfH264SinkWriterSession.h',
        'XbPreview.Native/Nv12TrackedTexturePool.cpp',
        'XbPreview.Native/Nv12TrackedTexturePool.h',
        'XbPreview.Native/VideoEncoderConfig.cpp',
        'XbPreview.Native/VideoEncoderConfig.h',
        'XbPreview.Native/VideoEncoderConsumer.cpp',
        'XbPreview.Native/VideoEncoderConsumer.h',
        'XbPreview.Native/VideoEncoderDiagnostics.cpp',
        'XbPreview.Native/VideoEncoderDiagnostics.h',
        'XbPreview.Native/VideoEncoderTimestamp.cpp',
        'XbPreview.Native/VideoEncoderTimestamp.h',
        'XbPreview.Host/ManagedStartupDiagnostics.cs'
    )
    $allowedModifiedPaths = [string[]]@(
        'SELF-TEST-P2.bat',
        'VERIFY-P2-STATIC.ps1',
        'XbPreview.Native.Tests/NativeTests.cpp',
        'XbPreview.Native.Tests/XbPreview.Native.Tests.vcxproj',
        'XbPreview.Managed.Tests/PreviewLifecycleTests.cs',
        'XbPreview.Managed.Tests/XbPreview.Managed.Tests.csproj',
        'XbPreview.Host/MainForm.cs',
        'XbPreview.Host/PreviewLifecycleController.cs',
        'XbPreview.Host/Program.cs',
        'XbPreview.Native/CropTransform.h',
        'XbPreview.Native/DiagnosticLogger.cpp',
        'XbPreview.Native/DiagnosticLogger.h',
        'XbPreview.Native/PreviewEngine.cpp',
        'XbPreview.Native/PreviewEngine.h',
        'XbPreview.Native/PreviewRenderer.cpp',
        'XbPreview.Native/PreviewRenderer.h',
        'XbPreview.Native/RenderFrameTap.cpp',
        'XbPreview.Native/RenderFrameTap.h',
        'XbPreview.Native/XbPreview.Native.vcxproj'
    )
    $baselineFormal = @($manifestEntries | Where-Object {
        [string]$_.Category -eq 'FormalContent'
    })
    if ($baselineFormal.Count -ne 199) {
        throw 'P2.3B formal baseline is not 199 files.'
    }
    $expectedFormalPaths = [string[]]@(
        $baselineFormal | ForEach-Object { [string]$_.RelativePath }) +
        $newFormalPaths
    $currentFormalPaths = [string[]]@(
        Get-FormalPhysicalPaths $root |
        Where-Object { $_ -notin @(
            'P2-PREACCEPTANCE-SEAL.json',
            'P2-ACCEPTANCE-REPORT.md',
            'P2-FREEZE-MANIFEST.json',
            'P2-FREEZE-RECORD.md') })
    Assert-PathSets $expectedFormalPaths $currentFormalPaths 'P2.4 formal files'
    foreach ($entry in $baselineFormal) {
        $relative = [string]$entry.RelativePath
        $actual = Get-UpperSha256 (Join-Path $root $relative)
        $changed = $actual -ne ([string]$entry.Sha256).ToUpperInvariant()
        if ($changed -ne ($allowedModifiedPaths -ccontains $relative)) {
            throw "P2.4 exact change scope mismatch: $relative changed=$changed"
        }
    }
    $candidateEntries = Get-FileEntries $root $currentFormalPaths
    $candidateFingerprint = Get-PathNulHashFingerprint $candidateEntries

    $copiedSeal = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'P2-PREACCEPTANCE-SEAL.json') | ConvertFrom-Json
    $runtimeNewPaths = [string[]]@($newFormalPaths | Where-Object {
        $_ -like 'XbPreview.Native/*' -or
        $_ -eq 'XbPreview.Host/ManagedStartupDiagnostics.cs'
    })
    $currentRuntimePaths = [string[]]@(
        @($copiedSeal.ProductRuntimeFiles | ForEach-Object {
            [string]$_.RelativePath
        }) + $runtimeNewPaths)
    if ($currentRuntimePaths.Count -ne 92) {
        throw "P2.4 ProductRuntime must contain 92 files; actual $($currentRuntimePaths.Count)."
    }
    $runtimeEntries = Get-FileEntries $root $currentRuntimePaths
    $runtimeFingerprint = Get-PathNulHashFingerprint $runtimeEntries

    $cropTransformPath = Join-Path $root 'XbPreview.Native/CropTransform.h'
    $cropTransformText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        $cropTransformPath)
    if ($cropTransformText -match
            'captureLeft\)\s*\+\s*0\.5|captureTop\)\s*\+\s*0\.5|' +
            'captureWidth\)\s*-\s*1|captureHeight\)\s*-\s*1|' +
            'captureLeft\s*\+\s*geometry\.captureWidth\)\s*-\s*0\.5|' +
            'captureTop\s*\+\s*geometry\.captureHeight\)\s*-\s*0\.5') {
        throw 'Product CropTransform still contains duplicate texel-center compensation.'
    }
    foreach ($requiredFormula in @(
        'static_cast<double>\(geometry\.captureLeft\)\s*/\s*sourceWidth',
        'static_cast<double>\(geometry\.captureTop\)\s*/\s*sourceHeight',
        'static_cast<double>\(geometry\.captureWidth\)\s*/\s*sourceWidth',
        'static_cast<double>\(geometry\.captureHeight\)\s*/\s*sourceHeight')) {
        if ($cropTransformText -notmatch $requiredFormula) {
            throw "Product CropTransform full-range formula is missing: $requiredFormula"
        }
    }
    $nativeTestsPath = Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp'
    foreach ($requiredPixelTest in @(
        'wide U first middle last pixel centers are exact',
        'wide V first middle last pixel centers are exact',
        'custom region keeps full width and height without one-pixel shrink',
        '1.6x and 2.0x camera bounds remain valid at all targets')) {
        Assert-Contains $nativeTestsPath ([regex]::Escape($requiredPixelTest)) `
            'formal CropTransform pixel-center regression'
    }
    $qualitySpikePath = Join-Path $root 'spikes/P2.4Q-CropUvQualityAB/main.cpp'
    Assert-Contains $qualitySpikePath '#include\s+"CropTransform\.h"' `
        'quality candidate includes product CropTransform'
    Assert-Contains $qualitySpikePath 'ResolveCropTransform\(geometry,\s*productCrop\)' `
        'quality candidate resolves product CropTransform'
    Assert-Contains (Join-Path $root (
        'spikes/P2.4Q-CropUvQualityAB/RUN-P2.4Q-CROP-UV-AB.ps1')) `
        'Product CropTransform candidate is not pixel-exact' `
        'pixel-exact quality regression assertion'
    Assert-Contains (Join-Path $root 'SELF-TEST-P2.bat') `
        'RUN-P2\.4Q-CROP-UV-AB\.ps1' `
        'P2.4 self-test invokes product Crop UV quality regression'

    $protectedP24Hashes = [ordered]@{
        'XbPreview.Native/RenderFrameTap.cpp' = '7805562DDDE9174EFD1F4E469D0B8B19E9F5594D0067EF54FBF80C72D356E327'
        'XbPreview.Native/D3D11Nv12Converter.cpp' = 'A648B4FB027E9DAE1FCDDAD286B0F02D19B9EBEF20AD581F095446BFABB9B00F'
        'XbPreview.Native/D3D11Nv12Converter.h' = '5F723A8D0E2E02FABE935A35B918245547E4E78AD96C41012F30FE6B39B32845'
        'XbPreview.Native/VideoEncoderConsumer.cpp' = 'B113ADA4C3810B62AF0FEE19D913FB8C43D334862C295720852B0B8B55C56F94'
        'XbPreview.Native/VideoEncoderConsumer.h' = '2F9B72A8B2E97C1DBFF79B64D7377D53AEA1C9AE51FF1AA2F0A662ABDAB50807'
        'XbPreview.Native/MfH264SinkWriterSession.cpp' = 'D17CC6AC2C2F0A281E40E77BDF5DDC6EC6DBC05020717887AE4DD51A8746B931'
        'XbPreview.Native/MfH264SinkWriterSession.h' = 'BEA5853323E89CCD34FC19D50940087E0BF798A32D6632292E477E36B3AB624A'
        'XbPreview.Native/VideoEncoderConfig.cpp' = '2E830D1CC7B91F2E786F52496AEF2C5F37FE5182DCA60F1C4877C543DD9C27E5'
        'XbPreview.Native/VideoEncoderConfig.h' = '833C8F3F7A2599485F7B10E99BC620DBE3E706E15D8C034855882DEF834EB2C8'
        'XbPreview.Native/XbPreviewApi.h' = '37CF9D589E3918460C966493344131EBDF12A947E63E9CF04A6A24E32EE2A19B'
        'XbPreview.Host/NativeMethods.cs' = '956A5764D6B8A6A71C90F7312D44810EBE4CCE9379DF8F0AF43DAC6DF0BEF418'
    }
    foreach ($protected in $protectedP24Hashes.GetEnumerator()) {
        Assert-ExpectedHash (Join-Path $root $protected.Key) $protected.Value `
            "P2.4 UV single-variable protection: $($protected.Key)"
    }

    $startupHeader = Join-Path $root 'XbPreview.Native/DiagnosticLogger.h'
    $startupSource = Join-Path $root 'XbPreview.Native/DiagnosticLogger.cpp'
    $engineStartupSource = Join-Path $root 'XbPreview.Native/PreviewEngine.cpp'
    $rendererStartupSource = Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp'
    $managedStartupSource = Join-Path $root (
        'XbPreview.Host/ManagedStartupDiagnostics.cs')
    $lifecycleStartupSource = Join-Path $root (
        'XbPreview.Host/PreviewLifecycleController.cs')
    foreach ($required in @(
        'class\s+StartupDiagnostics',
        'catch\s*\(const\s+winrt::hresult_error&',
        'catch\s*\(const\s+std::exception&',
        'catch\s*\(\.\.\.\)',
        'throw;',
        '__FILE__',
        '__LINE__')) {
        Assert-Contains $startupHeader $required `
            "startup executor preserves call site and exception semantics"
    }
    foreach ($eventName in @(
        'startup-step-begin', 'startup-step-success',
        'startup-step-failure', 'startup-fallback-begin',
        'startup-fallback-success', 'startup-fallback-failure',
        'startup-summary')) {
        Assert-Contains $startupSource ([regex]::Escape($eventName)) `
            "startup diagnostic event $eventName"
    }
    foreach ($fieldName in @(
        'SessionGuid', 'Event', 'Stage', 'Operation', 'ApiName',
        'SourceFile', 'SourceLine', 'ThreadId', 'Qpc', 'Utc',
        'ElapsedMs', 'EncoderEnabled', 'DeviceFlagsRequested',
        'AttemptIndex', 'FallbackFrom', 'Result', 'HResultHex',
        'Win32Code', 'ExceptionType', 'ExceptionMessage',
        'LastCompletedStage', 'ActiveStage', 'OriginalHResult',
        'CleanupStarted', 'CleanupCompleted')) {
        Assert-Contains $startupSource ([regex]::Escape($fieldName)) `
            "startup diagnostic field $fieldName"
    }
    foreach ($nativeBoundary in @(
        'winrt::init_apartment', 'DiagnosticLogger::Open',
        'GraphicsCaptureSession::IsSupported', 'GetMonitorInfoW',
        'CreateForMonitor', 'CreateFreeThreaded', 'FrameArrived',
        'CreateCaptureSession', 'StartCapture', 'CaptureUnhandled',
        'WriteSummary')) {
        Assert-Contains $engineStartupSource ([regex]::Escape($nativeBoundary)) `
            "instrumented Native worker boundary $nativeBoundary"
    }
    foreach ($rendererBoundary in @(
        'CreateDXGIFactory1', 'EnumAdapters1', 'EnumOutputs',
        'D3D11CreateDevice', 'CreateVideoSupportDevice',
        'CreateBgraFallbackDevice', 'FallbackBegin', 'FallbackSuccess',
        'QueryInterface<ID3D10Multithread>', 'SetMultithreadProtected',
        'CreateDirect3D11DeviceFromDXGIDevice',
        'CreateSwapChainForHwnd', 'IDXGISwapChain::GetBuffer',
        'CreateRenderTargetView', 'D3DCompile', 'CreateVertexShader',
        'CreatePixelShader', 'CreateBuffer', 'CreateSamplerState',
        'CreateRasterizerState', 'RenderFrameTap::Initialize',
        'DeferredUntilFirstFrame')) {
        Assert-Contains $rendererStartupSource ([regex]::Escape($rendererBoundary)) `
            "instrumented Renderer boundary $rendererBoundary"
    }
    foreach ($managedField in @(
        'StartupAttemptId', 'SessionGuid', 'ManagedStage', 'ThreadId',
        'MainFormIsHandleCreated', 'MainFormHandle',
        'PreviewSurfaceIsHandleCreated', 'PreviewSurfaceHandle',
        'Visible', 'WindowState', 'IsDisposed', 'Disposing',
        'LifecycleState', 'StartAttemptNumber', 'NativeHResult',
        'RetryAvailable')) {
        Assert-Contains $managedStartupSource ([regex]::Escape($managedField)) `
            "managed startup diagnostic field $managedField"
    }
    foreach ($managedBoundary in @(
        'NativeStartCallBegin', 'NativeStartReturnedSuccess',
        'NativeStartReturnedFailure', 'NativeStartThrew',
        'FailStartAndCleanupBegin', 'FailStartAndCleanupEnd')) {
        Assert-Contains $lifecycleStartupSource ([regex]::Escape($managedBoundary)) `
            "managed lifecycle diagnostic $managedBoundary"
    }
    Assert-Contains (Join-Path $root 'XbPreview.Host/Program.cs') `
        'Program\.MainEntered' 'managed Program entry diagnostic'
    foreach ($formBoundary in @(
        'MainFormConstructed', 'MainForm.OnShown',
        'PreviewSurfaceHandleConfirmed', 'AutomaticStartRequested',
        'UiEnteredErrorState', 'MainForm.FormClosing',
        'MainForm.FormClosed')) {
        Assert-Contains (Join-Path $root 'XbPreview.Host/MainForm.cs') `
            ([regex]::Escape($formBoundary)) `
            "managed MainForm diagnostic $formBoundary"
    }
    foreach ($testEvidence in @(
        'TestStartupStepDiagnostics', 'controlled 0x80070424',
        'D3D video-support success', 'D3D fallback success',
        'D3D second failure',
        'outer startup wrapper preserves the precise inner failure',
        'ManagedStartupDiagnosticsCorrelateFailureAndRetryAsync',
        'Error to Retry behavior remains unchanged')) {
        $testPath = if ($testEvidence -match 'Managed|Error to Retry') {
            Join-Path $root 'XbPreview.Managed.Tests/PreviewLifecycleTests.cs'
        } else {
            Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp'
        }
        Assert-Contains $testPath ([regex]::Escape($testEvidence)) `
            "startup diagnostic behavior test $testEvidence"
    }
    $startupProductText = [string]::Join("`n", @(
        Get-Content -Raw -Encoding UTF8 -LiteralPath $startupHeader
        Get-Content -Raw -Encoding UTF8 -LiteralPath $startupSource
        Get-Content -Raw -Encoding UTF8 -LiteralPath $engineStartupSource
        Get-Content -Raw -Encoding UTF8 -LiteralPath $rendererStartupSource
        Get-Content -Raw -Encoding UTF8 -LiteralPath $managedStartupSource
        Get-Content -Raw -Encoding UTF8 -LiteralPath $lifecycleStartupSource))
    if ($startupProductText -match
        'OpenSCManager|StartService|ControlService|RegSetValue|\bDISM\b|\bSFC\b') {
        throw 'Startup diagnostic patch contains forbidden service/registry/repair operations.'
    }

    foreach ($forbidden in @(
        'P2.4-PREACCEPTANCE-SEAL.json',
        'P2.4-ACCEPTANCE-REPORT.md',
        'P2.4-FREEZE-MANIFEST.json',
        'P2.4-FREEZE-RECORD.md')) {
        if (Test-Path -LiteralPath (Join-Path $root $forbidden)) {
            throw "P2.4 acceptance/freeze state must not exist: $forbidden"
        }
    }
    foreach ($required in $newFormalPaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) {
            throw "P2.4 required implementation file is missing: $required"
        }
    }
    $tapText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Native/RenderFrameTap.cpp')
    if ($tapText -match 'MFStartup|SinkWriter|WriteSample|VideoProcessorBlt|H264|NV12') {
        throw 'RenderFrameTap contains forbidden encoder/MF implementation logic.'
    }
    $rendererText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp')
    if ($rendererText -match 'WriteSample|Finalize\s*\(') {
        throw 'PreviewRenderer render path contains forbidden WriteSample/Finalize.'
    }
    $nativeProductText = [string]::Join("`n", @(
        Get-ChildItem -LiteralPath (Join-Path $root 'XbPreview.Native') -File |
        Where-Object Extension -in @('.h','.cpp') |
        ForEach-Object { Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName }))
    $encoderEnvironmentReadCount = [regex]::Matches(
        $nativeProductText, 'XB_PREVIEW_DIAGNOSTIC_ENCODER').Count
    if ($encoderEnvironmentReadCount -ne 1) {
        throw 'Encoder environment variable must have one product-code source.'
    }
    if ($nativeProductText -match 'D3D11_USAGE_STAGING|\bMap\s*\(|BitBlt|CopyFromScreen') {
        throw 'P2.4 product path contains forbidden CPU full-frame/readback mechanism.'
    }
    Assert-Contains (Join-Path $root 'XbPreview.Native/VideoEncoderConfig.h') `
        'VideoEncoderNv12PoolSize\s*=\s*6' 'fixed six-slot NV12 pool'
    Assert-Contains (Join-Path $root 'XbPreview.Native/Nv12TrackedTexturePool.cpp') `
        'DXGI_FORMAT_NV12' 'NV12 texture format'
    Assert-Contains (Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp') `
        'D3D11_CREATE_DEVICE_VIDEO_SUPPORT' 'conditional video-capable device'
    Assert-Contains (Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp') `
        'SetMultithreadProtected\(TRUE\)' 'immediate-context protection'
    Assert-Contains (Join-Path $root 'XbPreview.Native/RenderFrameTap.cpp') `
        'RegisterConsumer' 'single consumer registration'
    Assert-Contains (Join-Path $root 'XbPreview.Native/VideoEncoderConsumer.cpp') `
        'VideoProcessorBlt' 'VideoProcessor error stage'
    Assert-Contains (Join-Path $root 'XbPreview.Native/VideoEncoderConsumer.cpp') `
        'WaitForAllReturned' 'bounded tracked return wait'
    Assert-Contains (Join-Path $root 'XbPreview.Native/MfH264SinkWriterSession.cpp') `
        'MFCreateSinkWriterFromURL' 'H.264 Sink Writer'
    Assert-Contains (Join-Path $root 'XbPreview.Native/MfH264SinkWriterSession.cpp') `
        'MFCreateSourceReaderFromURL' 'Source Reader validation'
    Assert-Contains (Join-Path $root 'XbPreview.Native/MfH264SinkWriterSession.h') `
        'VideoEncoderQuickRuntimeValidationSampleLimit\s*=\s*8' `
        'fixed quick runtime validation sample limit'
    Assert-Contains (Join-Path $root 'XbPreview.Native/VideoEncoderConsumer.cpp') `
        'sink\.QuickRuntimeValidation\(diagnostics\)' `
        'runtime close uses bounded quick validation'
    $encoderConsumerText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Native/VideoEncoderConsumer.cpp')
    if ($encoderConsumerText -match 'FullTestValidation') {
        throw 'FullTestValidation must not enter the product runtime consumer.'
    }
    Assert-Contains (Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp') `
        'sink\.FullTestValidation\(fullValidation\)' `
        'full EOS validation remains explicit in automated tests'
    $mainFormText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Host/MainForm.cs')
    $lifecycleControllerText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Host/PreviewLifecycleController.cs')
    $managedTestsText = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $root 'XbPreview.Managed.Tests/PreviewLifecycleTests.cs')
    $prepareArgument = $mainFormText.IndexOf(
        'PrepareForImmediateClose,', [StringComparison]::Ordinal)
    $hideArgument = $mainFormText.IndexOf(
        'Hide,', $prepareArgument + 1, [StringComparison]::Ordinal)
    $cleanupArgument = $mainFormText.IndexOf(
        'CleanupAsync,', $hideArgument + 1, [StringComparison]::Ordinal)
    if ($prepareArgument -lt 0 -or $hideArgument -le $prepareArgument -or
        $cleanupArgument -le $hideArgument) {
        throw 'MainForm must pass synchronous Hide before Cleanup.'
    }
    if ($mainFormText -match 'ShowClosingFeedback|ShowInTaskbar\s*=|\bUpdate\(\)') {
        throw 'P2.4 immediate close must not repaint or recreate the top-level window before Hide.'
    }
    $hideCall = $lifecycleControllerText.IndexOf(
        'hide();', [StringComparison]::Ordinal)
    $cleanupCall = $lifecycleControllerText.IndexOf(
        'await cleanup();', [StringComparison]::Ordinal)
    if ($hideCall -lt 0 -or $cleanupCall -le $hideCall) {
        throw 'Managed close coordinator must synchronously Hide before awaiting Cleanup.'
    }
    foreach ($requiredCloseField in @(
        'SessionGuid', 'ManagedCloseRequestUtc', 'ImmediateHideRequestedUtc',
        'ImmediateHideAppliedUtc', 'VisibleAfterHide', 'CleanupStartUtc',
        'CleanupEndUtc', 'FinalClosePostedUtc', 'FormClosedUtc',
        'VisibleCloseLatencyMs', 'CloseRequestToFormClosedMs',
        'CleanupInvocationCount', 'HideInvocationCount',
        'ClosingFeedbackShown', 'CleanupSucceeded', 'CleanupExceptionType')) {
        if ($mainFormText -notmatch [regex]::Escape($requiredCloseField)) {
            throw "P2.4 close diagnostic field is missing: $requiredCloseField"
        }
    }
    foreach ($requiredTest in @(
        'ManagedCloseHidesBeforeBlockedCleanupAsync',
        'ManagedCloseIsSingleFlightAsync',
        'ManagedCloseFailureStillPostsFinalCloseAsync')) {
        if ($managedTestsText -notmatch [regex]::Escape($requiredTest)) {
            throw "P2.4 managed close behavior test is missing: $requiredTest"
        }
    }
    Assert-Contains (Join-Path $root 'XbPreview.Host/PreviewLifecycleController.cs') `
        'LastEngineStopDurationMs' 'managed Engine Stop duration metric'
    Assert-Contains (Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp') `
        'p2\.4-thirty-second' '30-second-equivalent product-module integration'
    Assert-Contains (Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp') `
        'p2\.4-consumer-lifecycle' 'five-session product consumer lifecycle integration'
    Assert-Contains (Join-Path $root 'P2.4-OUTPUTCANVAS-ENCODING-IMPLEMENTATION.md') `
        'same protected immediate context' 'GPU ordering invariant documentation'
    Assert-Contains (Join-Path $root 'P2.4Q-CROP-UV-QUALITY-AB.md') `
        'originB = L / S' 'test-only pixel-center derivation'
    Assert-Contains (Join-Path $root 'spikes/P2.4Q-CropUvQualityAB/main.cpp') `
        'D3D11_USAGE_STAGING' 'test-only OutputCanvas readback'
    Assert-Contains (Join-Path $root 'spikes/P2.4Q-CropUvQualityAB/main.cpp') `
        'D3D11_FILTER_MIN_MAG_MIP_LINEAR' 'product-equivalent quality sampler'
    Assert-Contains (Join-Path $root 'spikes/P2.4Q-CropUvQualityAB/main.cpp') `
        'MfH264SinkWriterSession' 'product encoder module reuse'
    if ((Get-Content -Raw -Encoding UTF8 -LiteralPath (
            Join-Path $root 'XbPreview.Native/CropTransform.h')) -match
            'originB|candidatePrecise|PASS-UV-ROOT-CAUSE') {
        throw 'Test-only Crop UV candidate leaked into product CropTransform.'
    }

    Write-Host 'VERIFY-P2-STATIC P2_4: PASS'
    Write-Host "P2CandidateFormalFileCount=$($candidateEntries.Count)"
    Write-Host "P2CandidateFingerprint=$candidateFingerprint"
    Write-Host "ProductRuntimeFileCount=$($runtimeEntries.Count)"
    Write-Host "ProductRuntimeFingerprint=$runtimeFingerprint"
    Write-Host 'P23BFrozenGovernedFileCount=203'
    Write-Host 'P23BFrozenReadOnlyFileCount=203'
    Write-Host 'Missing=0'
    Write-Host 'Unexpected=0'
    Write-Host 'Mismatch=0'
    Write-Host 'Malformed=0'
    Write-Host 'Duplicate=0'
    exit 0
}

if ($Lifecycle -notin @('Baseline','SpikeA1','SpikeA2','P2_3A','P2_3B')) {
    throw (
        "$Lifecycle is intentionally unavailable in P2.1. " +
        'Only implemented P2 lifecycles may pass.')
}

$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if ((Split-Path -Leaf $root) -cne $ExpectedDirectoryName) {
    throw "VERIFY-P2-STATIC must run in $ExpectedDirectoryName"
}
$parent = Split-Path -Parent $root
$source = Join-Path $parent $SourceDirectoryName
$p1dA1 = Join-Path $parent $P1dA1DirectoryName
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Frozen P1d-a2 source is missing: $source"
}
if (-not (Test-Path -LiteralPath $p1dA1 -PathType Container)) {
    throw "Frozen P1d-a1 source is missing: $p1dA1"
}

Assert-NoResidualProcess 'VERIFY-P2-STATIC-Before'

$sourceSealPath = Join-Path $source 'P1D-A2-PREACCEPTANCE-SEAL.json'
$sourceReportPath = Join-Path $source 'P1D-A2-ACCEPTANCE-REPORT.md'
$sourceManifestPath = Join-Path $source 'P1D-A2-FREEZE-MANIFEST.json'
$sourceRecordPath = Join-Path $source 'P1D-A2-FREEZE-RECORD.md'
Assert-ExpectedHash $sourceSealPath $ExpectedSourceSealSha256 (
    'P1d-a2 PreAcceptance Seal')
Assert-ExpectedHash $sourceReportPath $ExpectedSourceReportSha256 (
    'P1d-a2 Acceptance Report')
Assert-ExpectedHash $sourceManifestPath $ExpectedSourceManifestSha256 (
    'P1d-a2 Freeze Manifest')
Assert-ExpectedHash $sourceRecordPath $ExpectedSourceRecordSha256 (
    'P1d-a2 Freeze Record')

$seal = Get-Content -Raw -LiteralPath $sourceSealPath | ConvertFrom-Json
$sealEntries = @($seal.FormalFiles)
if ([int]$seal.FormalFileCount -ne 167 -or
    $sealEntries.Count -ne 167) {
    throw 'P1d-a2 Seal must contain exactly 167 FormalFiles.'
}
$sealPaths = [string[]]@(
    $sealEntries | ForEach-Object {
        $path = [string]$_.RelativePath
        Assert-CanonicalRelativePath $path
        $path
    })
if (@($sealPaths | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
    throw 'P1d-a2 Seal has duplicate FormalFiles paths.'
}
$sealedPathList = [string[]]@($seal.FormalFilePaths)
if (-not (Test-OrdinalEqual $sealPaths $sealedPathList) -or
    -not (Test-OrdinalEqual $sealPaths (Get-OrdinalSorted $sealPaths))) {
    throw 'P1d-a2 Seal paths are not in stable Ordinal order.'
}
$sealByPath = @{}
foreach ($entry in $sealEntries) {
    $sealByPath[[string]$entry.RelativePath] = $entry
}
$sourceMissing = 0
$sourceMismatch = 0
foreach ($entry in $sealEntries) {
    $path = Join-Path $source ([string]$entry.RelativePath)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $sourceMissing++
        continue
    }
    if ((Get-UpperSha256 $path) -ne
        ([string]$entry.Sha256).ToUpperInvariant()) {
        $sourceMismatch++
    }
}
if ($sourceMissing -ne 0 -or $sourceMismatch -ne 0) {
    throw (
        "P1d-a2 FormalFiles mismatch. Missing=$sourceMissing; " +
        "Mismatch=$sourceMismatch.")
}
if ((Get-LegacyEntriesFingerprint $sealEntries) -ne
    $ExpectedSourceCandidateFingerprint) {
    throw 'P1d-a2 CandidateFingerprint cannot be reproduced.'
}

$runtimeEntries = @($seal.ProductRuntimeFiles)
if ([int]$seal.ProductRuntimeFileCount -ne 73 -or
    $runtimeEntries.Count -ne 73) {
    throw 'P1d-a2 ProductRuntimeFiles must contain exactly 73 files.'
}
$runtimeMismatchPaths = @()
foreach ($entry in $runtimeEntries) {
    $relative = [string]$entry.RelativePath
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-UpperSha256 $path) -ne
        ([string]$entry.Sha256).ToUpperInvariant()) {
        $runtimeMismatchPaths += $relative
    }
}
if ((Get-LegacyEntriesFingerprint $runtimeEntries) -ne
    $ExpectedSourceRuntimeFingerprint) {
    throw 'P1d-a2 ProductRuntime fingerprint cannot be reproduced.'
}
if ($Lifecycle -eq 'P2_3B') {
    Assert-PathSets $P23BRuntimeModifiedPaths (
        [string[]]$runtimeMismatchPaths) 'P2.3B modified ProductRuntime'
}
elseif ($Lifecycle -eq 'P2_3A') {
    Assert-PathSets $P23ARuntimeModifiedPaths (
        [string[]]$runtimeMismatchPaths) 'P2.3A modified ProductRuntime'
}
elseif ($runtimeMismatchPaths.Count -ne 0) {
    throw (
        'P2 Product Runtime differs from P1d-a2 outside P2.3A. ' +
        "ChangedRuntimeFiles=$($runtimeMismatchPaths.Count)")
}

$manifest = Get-Content -Raw -LiteralPath $sourceManifestPath |
    ConvertFrom-Json
$manifestEntries = @($manifest.Entries)
if ([int]$manifest.FrozenPayloadFileCount -ne 169 -or
    $manifestEntries.Count -ne 169) {
    throw 'P1d-a2 Freeze Manifest must contain 169 payload entries.'
}
$manifestPathsInOrder = [string[]]@(
    $manifestEntries | ForEach-Object { [string]$_.RelativePath })
$manifestPathsSorted = Get-OrdinalSorted $manifestPathsInOrder
if (-not (Test-OrdinalEqual $manifestPathsInOrder $manifestPathsSorted)) {
    throw 'P1d-a2 Freeze Manifest is not in stable Ordinal path order.'
}
for ($ordinalIndex = 0;
    $ordinalIndex -lt $manifestEntries.Count;
    $ordinalIndex++) {
    if ([int]$manifestEntries[$ordinalIndex].Ordinal -ne
        ($ordinalIndex + 1)) {
        throw 'P1d-a2 Freeze Manifest Ordinal sequence is malformed.'
    }
}
$manifestMismatch = 0
foreach ($entry in $manifestEntries) {
    $path = Join-Path $source ([string]$entry.RelativePath)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-UpperSha256 $path) -ne
        ([string]$entry.Sha256).ToUpperInvariant()) {
        $manifestMismatch++
    }
}
if ($manifestMismatch -ne 0) {
    throw "P1d-a2 frozen payload mismatch: $manifestMismatch"
}
if ((Get-PathNulHashFingerprint $manifestEntries) -ne
    $ExpectedSourceFrozenPayloadFingerprint) {
    throw 'P1d-a2 FrozenPayloadFingerprint cannot be reproduced.'
}

$sourceGovernedPaths = [string[]]@(
    @($manifestEntries | ForEach-Object {
        [string]$_.RelativePath
    }) +
    @('P1D-A2-FREEZE-MANIFEST.json', 'P1D-A2-FREEZE-RECORD.md'))
Assert-PathSets $sourceGovernedPaths (Get-FormalPhysicalPaths $source) (
    'P1d-a2 governed source')
$sourceReadOnlyCount = 0
foreach ($relative in $sourceGovernedPaths) {
    if ((Get-Item -LiteralPath (Join-Path $source $relative)).IsReadOnly) {
        $sourceReadOnlyCount++
    }
}
if ($sourceGovernedPaths.Count -ne 171 -or
    $sourceReadOnlyCount -ne 171) {
    throw (
        'P1d-a2 governed/read-only count mismatch. ' +
        "Governed=$($sourceGovernedPaths.Count); " +
        "ReadOnly=$sourceReadOnlyCount")
}

$p1dA1Manifest = Join-Path $p1dA1 'P1D-A1-FROZEN-HASHES.sha256'
Assert-ExpectedHash $p1dA1Manifest $ExpectedP1dA1ManifestSha256 (
    'P1d-a1 frozen hashes')
$p1dA1Entries = @()
$p1dA1Malformed = 0
foreach ($line in @(Get-Content -LiteralPath $p1dA1Manifest)) {
    if ($line -match '^([0-9A-Fa-f]{64}) \*(.+)$') {
        $p1dA1Entries += [pscustomobject]@{
            Sha256 = $Matches[1].ToUpperInvariant()
            RelativePath = $Matches[2].Replace('\', '/')
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($line)) {
        $p1dA1Malformed++
    }
}
$p1dA1Mismatch = 0
foreach ($entry in $p1dA1Entries) {
    $path = Join-Path $p1dA1 $entry.RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-UpperSha256 $path) -ne $entry.Sha256) {
        $p1dA1Mismatch++
    }
}
if ($p1dA1Entries.Count -ne 146 -or
    $p1dA1Malformed -ne 0 -or
    $p1dA1Mismatch -ne 0) {
    throw (
        'P1d-a1 baseline mismatch. ' +
        "Count=$($p1dA1Entries.Count); " +
        "Malformed=$p1dA1Malformed; Mismatch=$p1dA1Mismatch")
}

$copyMismatchPaths = @()
foreach ($entry in $sealEntries) {
    $relative = [string]$entry.RelativePath
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-UpperSha256 $path) -ne
        ([string]$entry.Sha256).ToUpperInvariant()) {
        $copyMismatchPaths += $relative
    }
}
if ($Lifecycle -eq 'P2_3B') {
    Assert-PathSets $P23BInheritedModifiedPaths (
        [string[]]$copyMismatchPaths) 'P2.3B inherited modifications'
}
elseif ($Lifecycle -eq 'P2_3A') {
    Assert-PathSets $P23AInheritedModifiedPaths (
        [string[]]$copyMismatchPaths) 'P2.3A inherited modifications'
}
elseif ($copyMismatchPaths.Count -ne 0) {
    throw "P2 inherited formal content mismatch: $($copyMismatchPaths.Count)"
}

$upstreamDirectory = Join-Path $root 'upstream-p1d-a2'
if (-not (Test-Path -LiteralPath $upstreamDirectory -PathType Container)) {
    throw 'upstream-p1d-a2 directory is missing.'
}
$snapshotFiles = @(
    Get-ChildItem -LiteralPath $upstreamDirectory -File -Force)
if ($snapshotFiles.Count -ne 4) {
    throw (
        'upstream-p1d-a2 must contain exactly four files. Actual=' +
        $snapshotFiles.Count)
}
foreach ($name in $UpstreamMetadataNames) {
    $sourcePath = Join-Path $source $name
    $snapshotPath = Join-Path $upstreamDirectory $name
    if (-not (Test-Path -LiteralPath $snapshotPath -PathType Leaf) -or
        (Get-UpperSha256 $sourcePath) -ne
        (Get-UpperSha256 $snapshotPath)) {
        throw "Upstream metadata snapshot mismatch: $name"
    }
    if (-not (Get-Item -LiteralPath $snapshotPath).IsReadOnly) {
        throw "Upstream metadata snapshot is not ReadOnly: $name"
    }
    if (Test-Path -LiteralPath (Join-Path $root $name)) {
        throw "Upstream freeze metadata must not exist at P2 root: $name"
    }
}

foreach ($future in $FutureP2Metadata) {
    if (Test-Path -LiteralPath (Join-Path $root $future)) {
        throw "Future P2 acceptance/freeze metadata exists too early: $future"
    }
}

$originPath = Join-Path $root 'P2-UPSTREAM-ORIGIN.json'
$origin = Get-Content -Raw -LiteralPath $originPath | ConvertFrom-Json
if ([int]$origin.SchemaVersion -ne 1 -or
    [string]$origin.Stage -ne 'P2.1' -or
    [string]$origin.SourceStage -ne 'P1d-a2' -or
    [int]$origin.SourceCandidateFormalFileCount -ne 167 -or
    [string]$origin.SourceCandidateFingerprint -ne
        $ExpectedSourceCandidateFingerprint -or
    [int]$origin.SourceProductRuntimeFileCount -ne 73 -or
    [string]$origin.SourceProductRuntimeFingerprint -ne
        $ExpectedSourceRuntimeFingerprint -or
    [int]$origin.SourceFrozenPayloadFileCount -ne 169 -or
    [string]$origin.SourceFrozenPayloadFingerprint -ne
        $ExpectedSourceFrozenPayloadFingerprint -or
    [int]$origin.SourceGovernedFrozenFileCount -ne 171 -or
    [int]$origin.SourceReadOnlyFileCount -ne 171 -or
    [int]$origin.CopiedFormalContentFileCount -ne 167 -or
    [int]$origin.CopiedUpstreamMetadataFileCount -ne 4 -or
    [int]$origin.CopyMissingCount -ne 0 -or
    [int]$origin.CopyMismatchCount -ne 0 -or
    [int]$origin.CopyDuplicateCount -ne 0 -or
    [int]$origin.CopyMalformedCount -ne 0 -or
    [string]$origin.P1dA1Status -ne 'PASS' -or
    [int]$origin.P2StageAddedFileCount -ne 4 -or
    [int]$origin.InitialP2CandidateFormalFileCount -ne 175 -or
    [bool]$origin.InitialP2CandidateFingerprintExternal -ne $true -or
    [int]$origin.InitialP2ProductRuntimeFileCount -ne 73 -or
    [string]$origin.InitialP2ProductRuntimeFingerprint -ne
        $ExpectedSourceRuntimeFingerprint -or
    [int]$origin.P2BaselinePayloadFileCount -ne 174) {
    throw 'P2-UPSTREAM-ORIGIN.json metadata is inconsistent.'
}

$baselineP2Paths = [string[]]@(
    $sealPaths +
    @($UpstreamMetadataNames | ForEach-Object {
        "upstream-p1d-a2/$_"
    }) +
    $P2StageFiles)
$spikeA1Present = Test-Path -LiteralPath (
    Join-Path $root 'spikes/P2.2A-MfSinkWriterGpuFrame') -PathType Container
$expectedP2Paths = [string[]]@(
    $baselineP2Paths +
    $(if ($spikeA1Present -or $Lifecycle -eq 'SpikeA1') {
        $SpikeA1AddedFiles
    }) +
    $(if ((Test-Path -LiteralPath (Join-Path $root 'spikes/P2.2B-D3D11VideoProcessorNv12') -PathType Container) -or $Lifecycle -eq 'SpikeA2') {
        $SpikeA2AddedFiles
    }) +
    $(if ((Test-Path -LiteralPath (Join-Path $root 'XbPreview.Native/OutputCanvasTarget.h') -PathType Leaf) -or $Lifecycle -eq 'P2_3A') {
        $P23AAddedFiles
    }) +
    $(if ((Test-Path -LiteralPath (Join-Path $root 'XbPreview.Native/RenderFrameTap.h') -PathType Leaf) -or $Lifecycle -eq 'P2_3B') {
        $P23BAddedFiles
    }))
$actualP2Paths = Get-FormalPhysicalPaths $root
Assert-PathSets $expectedP2Paths $actualP2Paths 'P2 formal content'
if ($baselineP2Paths.Count -ne 175) {
    throw 'P2 starting formal path set is not exactly 175 files.'
}
if ($Lifecycle -eq 'SpikeA1' -and
    ($expectedP2Paths.Count -lt 189 -or $actualP2Paths.Count -lt 189)) {
    throw (
        'P2 SpikeA1 formal file count mismatch. Expected=189; Actual=' +
        $actualP2Paths.Count)
}

$stageHashMismatch = 0
$sealedStageEntries = @($origin.StageAddedFiles)
if ($sealedStageEntries.Count -ne 3) {
    throw (
        'Origin StageAddedFiles must seal the three non-Origin P2 files.')
}
$expectedSealedStagePaths = [string[]]@(
    $P2StageFiles |
    Where-Object { $_ -cne 'P2-UPSTREAM-ORIGIN.json' })
$actualSealedStagePaths = [string[]]@(
    $sealedStageEntries |
    ForEach-Object { [string]$_.RelativePath })
Assert-PathSets $expectedSealedStagePaths $actualSealedStagePaths (
    'Origin StageAddedFiles')
foreach ($entry in $sealedStageEntries) {
    $relative = [string]$entry.RelativePath
    if ($relative -eq 'P2-UPSTREAM-ORIGIN.json' -or
        $P2StageFiles -cnotcontains $relative) {
        $stageHashMismatch++
    }
    if ($relative -eq 'P2.1-BASELINE-ORIGIN.md' -and
        (Get-UpperSha256 (Join-Path $root $relative)) -ne
            ([string]$entry.Sha256).ToUpperInvariant()) {
        $stageHashMismatch++
    }
}
if ($stageHashMismatch -ne 0) {
    throw "P2 stage-file hash mismatch: $stageHashMismatch"
}

$baselinePayloadPaths = [string[]]@(
    $baselineP2Paths |
    Where-Object { $_ -cne 'P2-UPSTREAM-ORIGIN.json' })
$virtualBaselineEntries = @()
$sealedStageByPath = @{}
foreach ($entry in $sealedStageEntries) {
    $sealedStageByPath[[string]$entry.RelativePath] = $entry
}
foreach ($relative in (Get-OrdinalSorted $baselinePayloadPaths)) {
    if ($sealedStageByPath.ContainsKey($relative)) {
        $virtualBaselineEntries += [pscustomobject][ordered]@{
            RelativePath = $relative
            Sha256 = ([string]$sealedStageByPath[$relative].Sha256).
                ToUpperInvariant()
        }
    }
    elseif ($sealByPath.ContainsKey($relative)) {
        $virtualBaselineEntries += [pscustomobject][ordered]@{
            RelativePath = $relative
            Sha256 = ([string]$sealByPath[$relative].Sha256).
                ToUpperInvariant()
        }
    }
    else {
        $virtualBaselineEntries += [pscustomobject][ordered]@{
            RelativePath = $relative
            Sha256 = Get-UpperSha256 (Join-Path $root $relative)
        }
    }
}
$payloadFingerprint =
    Get-PathNulHashFingerprint $virtualBaselineEntries
if ($virtualBaselineEntries.Count -ne 174 -or
    $payloadFingerprint -ne $ExpectedP2BaselinePayloadFingerprint -or
    $payloadFingerprint -ne
        ([string]$origin.P2BaselinePayloadFingerprint).ToUpperInvariant()) {
    throw (
        'P2 baseline payload fingerprint mismatch. ' +
        "Count=$($virtualBaselineEntries.Count); " +
        "Fingerprint=$payloadFingerprint")
}

$originEntry = Get-FileEntries $root @('P2-UPSTREAM-ORIGIN.json')
$virtualStartingCandidateEntries = [object[]]@(
    $virtualBaselineEntries + $originEntry)
$startingCandidateFingerprint =
    Get-PathNulHashFingerprint $virtualStartingCandidateEntries
if ($startingCandidateFingerprint -ne
    $ExpectedP2StartingCandidateFingerprint) {
    throw (
        'P2 starting 175-file candidate fingerprint mismatch. Actual=' +
        $startingCandidateFingerprint)
}

$candidateEntries = Get-FileEntries $root $expectedP2Paths
$candidateFingerprint = Get-PathNulHashFingerprint $candidateEntries
$currentRuntimePaths = [string[]]@(
    @($runtimeEntries | ForEach-Object { [string]$_.RelativePath }) +
    $(if ($Lifecycle -in @('P2_3A', 'P2_3B')) { $P23ARuntimeAddedFiles }) +
    $(if ($Lifecycle -eq 'P2_3B') {
        @($P23BRuntimeAddedFiles | Where-Object {
            $P23ARuntimeAddedFiles -cnotcontains $_
        })
    }))
$currentRuntimeEntries = Get-FileEntries $root $currentRuntimePaths
$currentRuntimeFingerprint =
    Get-LegacyEntriesFingerprint $currentRuntimeEntries
$spikeA1Paths = [string[]]@($baselineP2Paths + $SpikeA1AddedFiles)
$virtualSpikeA1Entries = @()
foreach ($relative in (Get-OrdinalSorted $spikeA1Paths)) {
    $hash = if ($relative -eq 'VERIFY-P2-STATIC.ps1') {
        $ExpectedSpikeA1VerifyHash
    } elseif ($relative -eq 'SELF-TEST-P2.bat') {
        $ExpectedSpikeA1SelfTestHash
    } elseif ($sealByPath.ContainsKey($relative)) {
        ([string]$sealByPath[$relative].Sha256).ToUpperInvariant()
    } else {
        Get-UpperSha256 (Join-Path $root $relative)
    }
    $virtualSpikeA1Entries += [pscustomobject][ordered]@{
        RelativePath=$relative; Sha256=$hash
    }
}
$spikeA1Fingerprint = Get-PathNulHashFingerprint $virtualSpikeA1Entries
if ($spikeA1Fingerprint -ne $ExpectedSpikeA1CandidateFingerprint) {
    throw "P2.2A 189-file baseline mismatch: $spikeA1Fingerprint"
}

$spikeA2Paths = [string[]]@(
    $baselineP2Paths + $SpikeA1AddedFiles + $SpikeA2AddedFiles)
$virtualSpikeA2Entries = @()
foreach ($relative in (Get-OrdinalSorted $spikeA2Paths)) {
    $hash = if ($relative -eq 'VERIFY-P2-STATIC.ps1') {
        $ExpectedSpikeA2VerifyHash
    } elseif ($relative -eq 'SELF-TEST-P2.bat') {
        $ExpectedSpikeA2SelfTestHash
    } elseif ($sealByPath.ContainsKey($relative)) {
        ([string]$sealByPath[$relative].Sha256).ToUpperInvariant()
    } else {
        Get-UpperSha256 (Join-Path $root $relative)
    }
    $virtualSpikeA2Entries += [pscustomobject][ordered]@{
        RelativePath = $relative
        Sha256 = $hash
    }
}
$spikeA2Fingerprint = Get-PathNulHashFingerprint $virtualSpikeA2Entries
if ($spikeA2Fingerprint -ne $ExpectedSpikeA2CandidateFingerprint) {
    throw "P2.2B 193-file baseline mismatch: $spikeA2Fingerprint"
}

$p23APaths = [string[]]@(
    $baselineP2Paths +
    $SpikeA1AddedFiles +
    $SpikeA2AddedFiles +
    $P23AAddedFiles)
$virtualP23AEntries = @()
foreach ($relative in (Get-OrdinalSorted $p23APaths)) {
    $hash = if ($relative -eq 'VERIFY-P2-STATIC.ps1') {
        $ExpectedP23AVerifyHash
    } elseif ($relative -eq 'SELF-TEST-P2.bat') {
        $ExpectedP23ASelfTestHash
    } elseif ($P23AOriginalHashByPath.ContainsKey($relative)) {
        [string]$P23AOriginalHashByPath[$relative]
    } elseif ($sealByPath.ContainsKey($relative)) {
        ([string]$sealByPath[$relative].Sha256).ToUpperInvariant()
    } else {
        Get-UpperSha256 (Join-Path $root $relative)
    }
    $virtualP23AEntries += [pscustomobject][ordered]@{
        RelativePath = $relative
        Sha256 = $hash
    }
}
$p23AFingerprint = Get-PathNulHashFingerprint $virtualP23AEntries
if ($virtualP23AEntries.Count -ne 196 -or
    $p23AFingerprint -ne $ExpectedP23ACandidateFingerprint) {
    throw (
        'P2.3A 196-file baseline mismatch. ' +
        "Count=$($virtualP23AEntries.Count); Fingerprint=$p23AFingerprint")
}
$p23ARuntimePaths = [string[]]@(
    @($runtimeEntries | ForEach-Object { [string]$_.RelativePath }) +
    $P23ARuntimeAddedFiles)
$virtualP23ARuntimeEntries = @()
foreach ($relative in (Get-OrdinalSorted $p23ARuntimePaths)) {
    $hash = if ($P23AOriginalHashByPath.ContainsKey($relative)) {
        [string]$P23AOriginalHashByPath[$relative]
    } elseif ($sealByPath.ContainsKey($relative)) {
        ([string]$sealByPath[$relative].Sha256).ToUpperInvariant()
    } else {
        Get-UpperSha256 (Join-Path $root $relative)
    }
    $virtualP23ARuntimeEntries += [pscustomobject][ordered]@{
        RelativePath = $relative
        Sha256 = $hash
    }
}
$p23ARuntimeFingerprint =
    Get-LegacyEntriesFingerprint $virtualP23ARuntimeEntries
if ($virtualP23ARuntimeEntries.Count -ne 75 -or
    $p23ARuntimeFingerprint -ne $ExpectedP23ARuntimeFingerprint) {
    throw (
        'P2.3A 75-file ProductRuntime baseline mismatch. ' +
        "Count=$($virtualP23ARuntimeEntries.Count); " +
        "Fingerprint=$p23ARuntimeFingerprint")
}

$productFeatures = Join-Path $root 'XbPreview.Host/ProductFeatures.cs'
$mainForm = Join-Path $root 'XbPreview.Host/MainForm.cs'
$lifecyclePath = Join-Path $root (
    'XbPreview.Host/PreviewLifecycleController.cs')
$nativeApi = Join-Path $root 'XbPreview.Native/XbPreviewApi.h'
$managedGeometryTests = Join-Path $root (
    'XbPreview.Managed.Tests/SessionGeometryTests.cs')
$nativeTests = Join-Path $root (
    'XbPreview.Native.Tests/NativeTests.cpp')
$hotkeys = Join-Path $root 'XbPreview.Host/HotkeyBindings.cs'

Assert-Contains $productFeatures (
    'RegionCaptureEnabled\s*=\s*false') (
    'RegionCaptureEnabled=false')
Assert-Contains $mainForm (
    '_selectRegionButton\.Visible\s*=\s*policy\.Visible') (
    'sealed custom-region entry')
Assert-Contains $mainForm (
    '_fullScreenButton\.Visible\s*=\s*policy\.Visible') (
    'sealed full-screen switch entry')
Assert-Contains $lifecyclePath 'StartAsync\s*\(' (
    'PreviewLifecycle StartAsync')
Assert-Contains (Join-Path $root (
        'XbPreview.Managed.Tests/PreviewLifecycleTests.cs')) (
    'concurrent Start single-flight') (
    'PreviewLifecycle single-flight test')
Assert-Contains $nativeApi (
    'sizeof\(XbPreviewSessionGeometryV1\)\s*==\s*56') (
    'native SessionGeometry V1 ABI size')
Assert-Contains $managedGeometryTests (
    'Marshal\.SizeOf<SessionGeometryNativeV1>\(\)\s*==\s*56') (
    'managed SessionGeometry V1 ABI size')
Assert-Contains $nativeTests 'TestCropTransform\s*\(' (
    'native GPU crop test')
Assert-Contains $hotkeys 'VkF9\s*=\s*0x78' 'F9 hotkey binding'
Assert-Contains $hotkeys 'VkF10\s*=\s*0x79' 'F10 hotkey binding'

foreach ($required in @(
        'XbPreview.Host/RegionSelectionController.cs',
        'XbPreview.Host/RegionSelectionMath.cs',
        'XbPreview.Host/SessionGeometry.cs',
        'XbPreview.Host/ComfortZoneMath.cs',
        'XbPreview.Host/CameraMath.cs',
        'XbPreview.Native/CameraTransform.h',
        'XbPreview.Native/CropTransform.h',
        'XbPreview.Native/CustomCursorRenderer.cpp')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) {
        throw "Inherited capability file is missing: $required"
    }
}

$codeFiles = Get-CodeAndProjectFiles $root
$forbiddenPatterns = @(
    '\bMFStartup\b',
    '\bIMFSinkWriter\b',
    '\bIMFTransform\b',
    '\bMFCreateDXGISurfaceBuffer\b',
    '\bCLSID_CMSH264EncoderMFT\b',
    '\bRecordingController\b',
    '\bRecordingState\b',
    '\bRecordingFramePool\b',
    '\bFrameQueue\b',
    '\bOutputSession\b',
    '\bIAudioClient\b',
    '\bIAudioCaptureClient\b',
    '\bTaskbarMinimizeWindowPresentation\b',
    '\bCleanPreview\b',
    'mfplat\.lib',
    'mfreadwrite\.lib',
    'mfuuid\.lib',
    '\bStartRecording\b')
if ($Lifecycle -ne 'P2_3B') {
    $forbiddenPatterns += '\bRenderFrameTap\b'
}
$forbiddenHits = @()
foreach ($file in $codeFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            $forbiddenHits += (
                "$(Get-RelativePath $root $file.FullName):$pattern")
        }
    }
}
if ($forbiddenHits.Count -ne 0) {
    throw (
        'P2 recording or failed-presentation symbols exist before ' +
        "construction: $([string]::Join(', ', $forbiddenHits))")
}

$spikeProjectFiles = @(
    $codeFiles |
    Where-Object { $_.Name -match '(?i)spike' })
if ($spikeProjectFiles.Count -ne 0) {
    throw 'A Spike project exists before P2.2.'
}

if ($Lifecycle -eq 'SpikeA1') {
    $spikeRoot = Join-Path $root 'spikes/P2.2A-MfSinkWriterGpuFrame'
    $spikePaths = [string[]]@(
        $SpikeA1AddedFiles |
        Where-Object { $_.StartsWith('spikes/', [StringComparison]::Ordinal) })
    $actualSpikePaths = [string[]]@(
        Get-FormalPhysicalPaths $spikeRoot |
        ForEach-Object { "spikes/P2.2A-MfSinkWriterGpuFrame/$_" })
    Assert-PathSets $spikePaths $actualSpikePaths 'P2.2A Spike directory'

    $spikeTextFiles = @(
        $spikePaths |
        Where-Object {
            $_ -match '\.(cpp|h|ps1|vcxproj)$'
        } |
        ForEach-Object { Join-Path $root $_ })
    $spikeCombined = [string]::Join(
        "`n",
        @($spikeTextFiles | ForEach-Object {
            Get-Content -Raw -LiteralPath $_
        }))
    $forbiddenSpikePatterns = [ordered]@{
        WgcIntegration = 'Windows\.Graphics\.Capture|CreateFreeThreaded'
        PreviewIntegration = '\bPreviewEngine\b|\bPreviewRenderer\b|\bMainForm\b'
        ProductRecording = '\bRecordingController\b|\bRenderFrameTap\b'
        Audio = '\bIAudioClient\b|\bIAudioCaptureClient\b'
        Ffmpeg = '(?i)\bffmpeg\b'
        CpuReadback = '\bD3D11_USAGE_STAGING\b|\bMap\s*\(|\bCopyFromScreen\b|\bBitBlt\b'
        ExplicitNv12 = '\bMFVideoFormat_NV12\b|\bDXGI_FORMAT_NV12\b'
        NormalFlush = '->\s*Flush\s*\('
    }
    foreach ($name in $forbiddenSpikePatterns.Keys) {
        if ($spikeCombined -match $forbiddenSpikePatterns[$name]) {
            throw "P2.2A forbidden Spike path detected: $name"
        }
    }

    $requiredSpikePatterns = [ordered]@{
        D3D11Device = '\bD3D11CreateDevice\s*\('
        BgraSupport = '\bD3D11_CREATE_DEVICE_BGRA_SUPPORT\b'
        VideoSupport = '\bD3D11_CREATE_DEVICE_VIDEO_SUPPORT\b'
        MultithreadProtection = '\bSetMultithreadProtected\s*\(\s*TRUE\s*\)'
        DeviceManager = '\bMFCreateDXGIDeviceManager\s*\('
        ResetDevice = '->\s*ResetDevice\s*\('
        WriterD3DManager = '\bMF_SINK_WRITER_D3D_MANAGER\b'
        HardwareTransforms = '\bMF_READWRITE_ENABLE_HARDWARE_TRANSFORMS\b'
        AdapterMftEnumeration = '\bMFTEnum2\s*\('
        AdapterLuidFilter = '\bMFT_ENUM_ADAPTER_LUID\b'
        DxgiSurfaceBuffer = '\bMFCreateDXGISurfaceBuffer\s*\('
        TrackedSample = '\bMFCreateTrackedSample\s*\(|\bIMFTrackedSample\b'
        TrackedAllocator = '->\s*SetAllocator\s*\('
        Finalize = '->\s*Finalize\s*\('
        SourceReader = '\bMFCreateSourceReaderFromURL\s*\('
        H264 = '\bMFVideoFormat_H264\b'
        Bgra = '\bDXGI_FORMAT_B8G8R8A8_UNORM\b'
        Width1920 = 'Width\s*=\s*1920'
        Height1080 = 'Height\s*=\s*1080'
        FrameRate30 = 'FrameRateNumerator\s*=\s*30'
        FrameCount150 = 'FrameCount\s*=\s*150'
        PoolSize6 = 'DefaultPoolSize\s*=\s*6'
        IntegerPts = 'frame\)\s*\*\s*TimeUnitsPerSecond\s*/'
        ArtifactOutput = 'artifacts\\spikes\\P2\.2A'
    }
    foreach ($name in $requiredSpikePatterns.Keys) {
        if ($spikeCombined -notmatch $requiredSpikePatterns[$name]) {
            throw "P2.2A required Spike evidence is missing: $name"
        }
    }

    $implementation = Join-Path $root 'P2.2A-SPIKE-A1-IMPLEMENTATION.md'
    foreach ($heading in @(
            'P2.2A Spike A1',
            'Sink Writer',
            'Tracked Sample',
            'CFR',
            'Source Reader',
            'MFTEnumEx',
            'UNKNOWN',
            'RenderFrameTap')) {
        Assert-Contains $implementation ([regex]::Escape($heading)) (
            "P2.2A implementation section $heading")
    }
}

if ($Lifecycle -eq 'SpikeA2') {
    $a2Root = Join-Path $root 'spikes/P2.2B-D3D11VideoProcessorNv12'
    $expectedA2Paths = [string[]]@($SpikeA2AddedFiles | Where-Object { $_ -like 'spikes/*' })
    $actualA2Paths = [string[]]@(Get-FormalPhysicalPaths $a2Root | ForEach-Object { "spikes/P2.2B-D3D11VideoProcessorNv12/$_" })
    Assert-PathSets $expectedA2Paths $actualA2Paths 'P2.2B Spike directory'
    $a2Text = [string]::Join("`n",@($actualA2Paths|ForEach-Object{Get-Content -Raw -LiteralPath (Join-Path $root $_)}))
    foreach($required in @(
        'ID3D11VideoDevice','ID3D11VideoContext','CreateVideoProcessorEnumerator',
        'CheckVideoProcessorFormat','DXGI_FORMAT_B8G8R8A8_UNORM','DXGI_FORMAT_NV12',
        'VideoProcessorBlt','MFVideoFormat_NV12','MFCreateDXGISurfaceBuffer',
        'MFCreateTrackedSample','MF_SINK_WRITER_D3D_MANAGER','Finalize',
        'MFCreateSourceReaderFromURL','ColorValidation','requested')) {
        if($a2Text -notmatch [regex]::Escape($required)){throw "A2 evidence missing: $required"}
    }
    foreach($forbidden in @('MFVideoFormat_ARGB32','MFVideoFormat_RGB32.*SetInputMediaType','D3D11_USAGE_STAGING','->Flush(','FFmpeg','Windows.Graphics.Capture','RecordingController','RenderFrameTap')) {
        if($a2Text -match [regex]::Escape($forbidden)){throw "A2 forbidden evidence: $forbidden"}
    }
}

if ($Lifecycle -in @('P2_3A', 'P2_3B')) {
    if ($virtualSpikeA2Entries.Count -ne 193) {
        throw "P2.2B historical baseline count is not 193."
    }
    foreach ($requiredPath in $P23AAddedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $requiredPath) -PathType Leaf)) {
            throw "P2.3A required file is missing: $requiredPath"
        }
    }

    $outputHeader = Join-Path $root 'XbPreview.Native/OutputCanvasTarget.h'
    $outputSource = Join-Path $root 'XbPreview.Native/OutputCanvasTarget.cpp'
    $rendererHeader = Join-Path $root 'XbPreview.Native/PreviewRenderer.h'
    $rendererSource = Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp'
    $engineSource = Join-Path $root 'XbPreview.Native/PreviewEngine.cpp'
    $cropHeader = Join-Path $root 'XbPreview.Native/CropTransform.h'
    $projectPath = Join-Path $root 'XbPreview.Native/XbPreview.Native.vcxproj'
    $nativeTestsPath = Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp'
    $implementationPath = Join-Path $root (
        'P2.3A-OUTPUT-CANVAS-IMPLEMENTATION.md')

    Assert-Contains $outputHeader 'class\s+OutputCanvasTarget\s+final' (
        'owned OutputCanvas target')
    Assert-Contains $outputSource (
        'D3D11_BIND_RENDER_TARGET\s*\|\s*D3D11_BIND_SHADER_RESOURCE') (
        'OutputCanvas render-target and shader-resource bindings')
    Assert-Contains $outputSource (
        'SameOutputCanvas\s*\(\s*description_\s*,\s*description\s*\)') (
        'OutputCanvas resize independence')
    Assert-Contains $rendererHeader 'OutputCanvasTarget\s+outputCanvas_' (
        'PreviewRenderer OutputCanvas ownership')
    Assert-Contains $rendererSource (
        'outputCanvas_\.Ensure\s*\(\s*device_\.get\(\)\s*,\s*outputDescription') (
        'fixed OutputCanvas creation')
    Assert-Contains $rendererSource (
        'DrawFullscreenPass\s*\(\s*outputCanvas_\.RenderTargetView\(\)') (
        'composition pass targets OutputCanvas')
    Assert-Contains $rendererSource (
        'customCursorRenderer_\.Draw\s*\(\s*\*cursorCommand\s*,\s*outputViewport') (
        'custom cursor targets OutputCanvas viewport')
    Assert-Contains $rendererSource (
        'DrawFullscreenPass\s*\(\s*previewRenderTargetView_\.get\(\)\s*,\s*outputCanvas_\.ShaderResourceView\(\)') (
        'Preview-only OutputCanvas blit')
    Assert-Contains $rendererSource (
        'CalculateLetterbox\s*\(\s*outputDescription\.width\s*,\s*outputDescription\.height') (
        'Preview-only letterbox')
    Assert-Contains $rendererSource 'outputCanvas_\.Shutdown\s*\(\s*\)' (
        'OutputCanvas shutdown')
    Assert-Contains $cropHeader (
        'static_cast<std::uint32_t>\(geometry\.outputWidth\)') (
        'SessionGeometry OutputCanvas width')
    Assert-Contains $cropHeader (
        'static_cast<std::uint32_t>\(geometry\.outputHeight\)') (
        'SessionGeometry OutputCanvas height')
    Assert-Contains $engineSource (
        'static_cast<float>\(activeCrop_\.outputWidth\)') (
        'cursor OutputCanvas viewport width')
    Assert-Contains $engineSource (
        'static_cast<float>\(activeCrop_\.outputHeight\)') (
        'cursor OutputCanvas viewport height')
    Assert-Contains $projectPath 'OutputCanvasTarget\.h' (
        'OutputCanvas header project inclusion')
    Assert-Contains $projectPath 'OutputCanvasTarget\.cpp' (
        'OutputCanvas source project inclusion')
    Assert-Contains $nativeTestsPath 'TestOutputCanvasDescription\s*\(' (
        'OutputCanvas native tests')
    Assert-Contains $nativeTestsPath (
        'output canvas is independent from capture dimensions') (
        'CaptureRegion and OutputCanvas separation test')

    $resizeMatch = [regex]::Match(
        (Get-Content -Raw -LiteralPath $rendererSource),
        'bool\s+PreviewRenderer::Resize\s*\([\s\S]*?\n\s*\}',
        [Text.RegularExpressions.RegexOptions]::None)
    if (-not $resizeMatch.Success -or
        $resizeMatch.Value -match 'outputCanvas_') {
        throw 'Preview Resize must not recreate or mutate OutputCanvas.'
    }

    foreach ($heading in @(
            'P2.3A',
            'OutputCanvas',
            'Preview Letterbox',
            'Custom Cursor',
            'Resize',
            'RenderFrameTap')) {
        Assert-Contains $implementationPath ([regex]::Escape($heading)) (
            "P2.3A implementation section $heading")
    }
    Assert-Contains $implementationPath 'H\.264.*MP4' (
        'P2.3A implementation non-goals evidence')
    $productRuntimeText = [string]::Join(
        "`n",
        @($currentRuntimePaths | ForEach-Object {
            Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $_)
        }))
    $p23AForbiddenRuntime = @(
            'MFStartup',
            'IMFSinkWriter',
            'MFCreateDXGISurfaceBuffer',
            'RecordingController',
            'RecordingState')
    if ($Lifecycle -eq 'P2_3A') {
        $p23AForbiddenRuntime += 'RenderFrameTap'
    }
    foreach ($forbidden in $p23AForbiddenRuntime) {
        if ($productRuntimeText -match [regex]::Escape($forbidden)) {
            throw "P2.3A product runtime contains forbidden recording evidence: $forbidden"
        }
    }
}

if ($Lifecycle -eq 'P2_3B') {
    if ($virtualP23AEntries.Count -ne 196 -or
        $p23AFingerprint -ne $ExpectedP23ACandidateFingerprint -or
        $virtualP23ARuntimeEntries.Count -ne 75 -or
        $p23ARuntimeFingerprint -ne $ExpectedP23ARuntimeFingerprint) {
        throw 'P2.3B did not preserve the exact P2.3A historical baseline.'
    }
    if ($candidateEntries.Count -ne 199) {
        throw "P2.3B formal file count must be 199. Actual=$($candidateEntries.Count)"
    }
    foreach ($requiredPath in $P23BAddedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $requiredPath) -PathType Leaf)) {
            throw "P2.3B required file is missing: $requiredPath"
        }
    }

    $tapHeader = Join-Path $root 'XbPreview.Native/RenderFrameTap.h'
    $tapSource = Join-Path $root 'XbPreview.Native/RenderFrameTap.cpp'
    $rendererHeader = Join-Path $root 'XbPreview.Native/PreviewRenderer.h'
    $rendererSource = Join-Path $root 'XbPreview.Native/PreviewRenderer.cpp'
    $engineHeader = Join-Path $root 'XbPreview.Native/PreviewEngine.h'
    $engineSource = Join-Path $root 'XbPreview.Native/PreviewEngine.cpp'
    $nativeProject = Join-Path $root 'XbPreview.Native/XbPreview.Native.vcxproj'
    $nativeTestProject = Join-Path $root (
        'XbPreview.Native.Tests/XbPreview.Native.Tests.vcxproj')
    $nativeTestsPath = Join-Path $root 'XbPreview.Native.Tests/NativeTests.cpp'
    $implementationPath = Join-Path $root (
        'P2.3B-RENDER-FRAME-TAP-IMPLEMENTATION.md')

    Assert-Contains $tapHeader (
        'RenderFrameTapPoolSize\s*=\s*6') 'P2.3B fixed pool size'
    Assert-Contains $tapHeader (
        'RenderFrameTapQueueCapacity\s*=\s*4') 'P2.3B fixed queue capacity'
    Assert-Contains $tapHeader 'enum\s+class\s+RenderFrameTapConsumerMode' (
        'P2.3B typed diagnostic consumer mode')
    Assert-Contains $tapHeader (
        'RenderFrameTapConsumerMode\s+consumerMode') (
        'P2.3B effective consumer mode in shared configuration')
    Assert-Contains $tapHeader 'class\s+GpuFrameLease\s+final' (
        'P2.3B move-only GPU lease')
    Assert-Contains $tapHeader 'class\s+RenderFrameTap\s+final' (
        'P2.3B frame tap module')
    foreach ($metric in @(
            'framesObservedAtTapPoint',
            'framesCopied',
            'framesEnqueued',
            'framesConsumed',
            'framesReturned',
            'framesDroppedNoFreeSlot',
            'framesDroppedQueueFull',
            'framesDroppedGenerationMismatch',
            'framesDroppedDisabledOrStopping',
            'timestampValidCount',
            'timestampMissingCount',
            'timestampRegressionCount',
            'queueDepthCurrent',
            'queueDepthHighWatermark',
            'freeSlotsCurrent',
            'consumerOwnedCurrent',
            'outstandingCurrent',
            'outstandingHighWatermark',
            'generationChangeCount',
            'staleFramesFlushed',
            'lateReturnsFromOldGeneration',
            'doubleReturnDetected',
            'invalidStateTransitionDetected',
            'shutdownDurationMilliseconds')) {
        Assert-Contains $tapHeader ([regex]::Escape($metric)) (
            "P2.3B diagnostic metric $metric")
    }

    Assert-Contains $tapSource (
        'std::array<TapSlot,\s*RenderFrameTapPoolSize>') (
        'fixed six-slot GPU pool storage')
    Assert-Contains $tapSource (
        'std::array<QueuedTapFrame,\s*RenderFrameTapQueueCapacity>') (
        'fixed bounded ring queue storage')
    Assert-Contains $tapSource 'std::try_to_lock' (
        'non-blocking producer lock')
    Assert-Contains $tapSource (
        'CopyResource\s*\(\s*freeSlot->texture\.get\(\)\s*,\s*outputCanvas\s*\)') (
        'GPU OutputCanvas copy into recorder-owned slot')
    Assert-Contains $tapSource (
        'state->queueCount\s*>=\s*RenderFrameTapQueueCapacity') (
        'queue-full drop-new gate')
    Assert-Contains $tapSource 'TapSlotState::Free' 'free slot state'
    Assert-Contains $tapSource 'TapSlotState::Queued' 'queued slot state'
    Assert-Contains $tapSource 'TapSlotState::ConsumerOwned' (
        'consumer-owned slot state')
    Assert-Contains $tapSource 'retired\.size\(\)\s*>=\s*2' (
        'bounded retired generation protection')
    Assert-Contains $tapSource 'XB_PREVIEW_DIAGNOSTIC_TAP' (
        'internal-only diagnostic enable switch')
    Assert-Contains $tapSource 'p2\.3b-tap-' (
        'P2.3B diagnostic JSONL path')
    Assert-Contains $tapSource (
        '\\"ConsumerMode\\":\\"') (
        'P2.3B JSONL effective ConsumerMode field')
    Assert-Contains $tapSource (
        '\\"ConsumerDelayMs\\":') (
        'P2.3B JSONL typed ConsumerDelayMs field')
    Assert-Contains $tapSource (
        'state->consumerMode\s*=\s*configuration\.consumerMode') (
        'consumer thread and log share the parsed mode configuration')
    Assert-Contains $tapSource (
        'state->consumerDelay\s*=\s*configuration\.consumerDelay') (
        'consumer thread and log share the parsed delay configuration')

    $tapText = Get-Content -Raw -Encoding UTF8 -LiteralPath $tapSource
    $observeMatch = [regex]::Match(
        $tapText,
        'void\s+RenderFrameTap::ObserveAndCopy[\s\S]*?(?=\n\s*std::optional<GpuFrameLease>\s+RenderFrameTap::TryAcquireForTest)')
    if (-not $observeMatch.Success) {
        throw 'P2.3B producer method could not be isolated.'
    }
    foreach ($forbiddenProducerPattern in @(
            '\bWaitForSingleObject\b',
            '\.wait\s*\(',
            '\.wait_for\s*\(',
            '\bSleep\s*\(',
            '\bjoin\s*\(')) {
        if ($observeMatch.Value -match $forbiddenProducerPattern) {
            throw "P2.3B Render producer contains blocking evidence: $forbiddenProducerPattern"
        }
    }
    foreach ($forbiddenTapPattern in @(
            '\bD3D11_USAGE_STAGING\b',
            '->\s*Map\s*\(',
            '\bBitBlt\b',
            '\bCopyFromScreen\b')) {
        if ($tapText -match $forbiddenTapPattern) {
            throw "P2.3B Tap contains forbidden CPU readback evidence: $forbiddenTapPattern"
        }
    }

    $rendererText = Get-Content -Raw -Encoding UTF8 -LiteralPath $rendererSource
    $cursorIndex = $rendererText.IndexOf('customCursorRenderer_.Draw(')
    $tapIndex = $rendererText.IndexOf('frameTap_.ObserveAndCopy(')
    $previewClearIndex = $rendererText.IndexOf(
        'context_->ClearRenderTargetView(',
        $tapIndex)
    $previewBlitIndex = $rendererText.IndexOf(
        'outputCanvas_.ShaderResourceView()',
        $tapIndex)
    if ($cursorIndex -lt 0 -or $tapIndex -le $cursorIndex -or
        $previewClearIndex -le $tapIndex -or
        $previewBlitIndex -le $tapIndex) {
        throw 'P2.3B Tap is not strictly after Cursor and before Preview Letterbox.'
    }
    Assert-Contains $rendererHeader 'RenderFrameTap\s+frameTap_' (
        'PreviewRenderer owns RenderFrameTap')
    Assert-Contains $rendererSource (
        'frameTap_\.ObserveAndCopy\s*\(\s*outputCanvas_\.Texture\(\)') (
        'Tap source is the completed OutputCanvas texture')
    Assert-Contains $rendererSource (
        'OMSetRenderTargets\s*\(\s*1\s*,\s*&nullTapTarget\s*,\s*nullptr\s*\)' +
        '[\s\S]*frameTap_\.ObserveAndCopy') (
        'OutputCanvas RTV is unbound before the Tap GPU copy')
    Assert-Contains $rendererSource (
        'frameTap_\.Shutdown\s*\(\s*\)[\s\S]*customCursorRenderer_\.Shutdown') (
        'Tap stops before renderer resources are released')

    $resizeMatch = [regex]::Match(
        $rendererText,
        'bool\s+PreviewRenderer::Resize[\s\S]*?(?=\n\s*void\s+PreviewRenderer::InitializeCustomCursorLayer)')
    if (-not $resizeMatch.Success -or $resizeMatch.Value -match 'frameTap_') {
        throw 'Preview SwapChain Resize must not mutate RenderFrameTap generation.'
    }

    Assert-Contains $engineHeader 'systemRelativeTimeValid' (
        'explicit WGC timestamp validity')
    Assert-Contains $engineSource (
        'frame\.SystemRelativeTime\(\)\.count\(\)') (
        'raw WGC SystemRelativeTime source')
    Assert-Contains $engineSource (
        'pending\.systemRelativeTimeValid\s*=\s*pending\.systemRelativeTime100ns\s*>\s*0') (
        'missing timestamp is explicit and not fabricated')
    Assert-Contains $engineSource 'ReadRenderFrameTapConfiguration' (
        'internal diagnostic configuration only')

    Assert-Contains $nativeProject 'RenderFrameTap\.h' (
        'P2.3B header in native project')
    Assert-Contains $nativeProject 'RenderFrameTap\.cpp' (
        'P2.3B source in native project')
    Assert-Contains $nativeTestProject 'RenderFrameTap\.cpp' (
        'P2.3B implementation in native tests')
    foreach ($testName in @(
            'TestRenderFrameTapConsumerConfigurationAndJson',
            'TestRenderFrameTapDisabled',
            'TestRenderFrameTapMetadataAndReturn',
            'TestRenderFrameTapQueueAndNoFreeDrops',
            'TestRenderFrameTapGenerationAndLateReturn',
            'TestRenderFrameTapTimestampPolicy',
            'TestRenderFrameTapSlowConsumer',
            'TestRenderFrameTapStopStartAndHeldLeaseShutdown',
            'TestRenderFrameTapDoubleReturnDetector',
            'RunRenderFrameTapThirtySecondDiagnostic')) {
        Assert-Contains $nativeTestsPath ($testName + '\s*\(') (
            "P2.3B native test $testName")
    }

    foreach ($heading in @(
            'P2.3B',
            'RenderFrameTap',
            'Drop-New',
            'Generation',
            'SystemRelativeTime',
            'Shutdown',
            '30')) {
        Assert-Contains $implementationPath ([regex]::Escape($heading)) (
            "P2.3B implementation section $heading")
    }

    foreach ($forbiddenRuntime in @(
            'MFStartup',
            'IMFSinkWriter',
            'IMFSourceReader',
            'MFCreateDXGISurfaceBuffer',
            'MFVideoFormat_H264',
            'DXGI_FORMAT_NV12',
            'RecordingController',
            'StartRecording',
            'PauseRecording',
            'ResumeRecording')) {
        if ($productRuntimeText -match [regex]::Escape($forbiddenRuntime)) {
            throw "P2.3B product runtime contains forbidden future-stage evidence: $forbiddenRuntime"
        }
    }
}

Assert-NoResidualProcess 'VERIFY-P2-STATIC-After'

Write-Host "VERIFY-P2-STATIC $Lifecycle`: PASS"
Write-Host 'SourceCandidateFormalFileCount=167'
Write-Host "SourceCandidateFingerprint=$ExpectedSourceCandidateFingerprint"
Write-Host 'SourceProductRuntimeFileCount=73'
Write-Host "SourceProductRuntimeFingerprint=$ExpectedSourceRuntimeFingerprint"
Write-Host 'SourceGovernedFrozenFileCount=171'
Write-Host "SourceReadOnlyFileCount=$sourceReadOnlyCount"
Write-Host 'SourceMissing=0'
Write-Host 'SourceMismatch=0'
Write-Host 'P1dA1=146/146'
Write-Host 'InheritedFormalContentFileCount=167'
Write-Host 'UpstreamMetadataSnapshotFileCount=4'
Write-Host "P2StartingCandidateFormalFileCount=$($virtualStartingCandidateEntries.Count)"
Write-Host "P2StartingCandidateFingerprint=$startingCandidateFingerprint"
Write-Host "P2SpikeA1FormalFileCount=$($virtualSpikeA1Entries.Count)"
Write-Host "P2SpikeA1Fingerprint=$spikeA1Fingerprint"
Write-Host "P2SpikeA2FormalFileCount=$($virtualSpikeA2Entries.Count)"
Write-Host "P2SpikeA2Fingerprint=$spikeA2Fingerprint"
Write-Host "P23ABaselineFormalFileCount=$($virtualP23AEntries.Count)"
Write-Host "P23ABaselineFingerprint=$p23AFingerprint"
Write-Host "P23ABaselineProductRuntimeFileCount=$($virtualP23ARuntimeEntries.Count)"
Write-Host "P23ABaselineProductRuntimeFingerprint=$p23ARuntimeFingerprint"
Write-Host "P2StageAddedFileCount=$(4 + $SpikeA1AddedFiles.Count + $SpikeA2AddedFiles.Count + $(if ($Lifecycle -in @('P2_3A','P2_3B')) { $P23AAddedFiles.Count } else { 0 }) + $(if ($Lifecycle -eq 'P2_3B') { $P23BAddedFiles.Count } else { 0 }))"
Write-Host "P2CandidateFormalFileCount=$($candidateEntries.Count)"
Write-Host "P2CandidateFingerprint=$candidateFingerprint"
Write-Host "P2BaselinePayloadFingerprint=$payloadFingerprint"
Write-Host "ProductRuntimeFileCount=$($currentRuntimeEntries.Count)"
Write-Host "ProductRuntimeFingerprint=$currentRuntimeFingerprint"
Write-Host "InheritedModifiedCount=$($copyMismatchPaths.Count)"
Write-Host 'Missing=0'
Write-Host 'Unexpected=0'
Write-Host 'Mismatch=0'
Write-Host 'Duplicate=0'
Write-Host 'Malformed=0'
Write-Host 'ResidualProcessCount=0'
