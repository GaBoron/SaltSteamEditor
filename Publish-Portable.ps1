param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$publishPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $publishPath) -and (Get-ChildItem -LiteralPath $publishPath -Force | Select-Object -First 1)) {
    throw '请选择新的或空的输出目录，以免混入旧文件。'
}

$projectPath = Join-Path $PSScriptRoot 'src/SaltSteamEditor/SaltSteamEditor.csproj'
& dotnet restore $projectPath -r win-x64 -p:PublishSingleFile=true --nologo
if ($LASTEXITCODE -ne 0) { throw '还原依赖失败。' }

# Embed the notices from the same runtime packs that the SDK selected for this build.
$assets = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'src/SaltSteamEditor/obj/project.assets.json') | ConvertFrom-Json
$noticePath = Join-Path $PSScriptRoot ('src/SaltSteamEditor/obj/single-file-notices/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $noticePath -Force | Out-Null
$runtimeNames = @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')
$runtimePacks = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object name -In $runtimeNames)
if ($runtimePacks.Count -ne 2) { throw '无法确定所选运行时版本。' }
foreach ($pack in $runtimePacks) {
    if ($pack.version -notmatch '^\[([^,]+),\s*\1\]$') { throw "运行时版本不明确：$($pack.version)" }
    $runtimeVersion = $Matches[1]
    $packagePath = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($pack.name.ToLowerInvariant() + '/' + $runtimeVersion)
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packagePath = $candidate
            break
        }
    }
    if ($null -eq $packagePath) { throw "找不到运行时许可来源：$($pack.name)" }
    $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
    if ($notices.Count -eq 0) { throw "运行时缺少许可文件：$packagePath" }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticePath ($pack.name + '-' + $notice.BaseName + '.txt'))
    }
}

& dotnet publish $projectPath -c Release --no-restore -p:PublishProfile=SingleFile "-p:RuntimeNoticeDirectory=$noticePath" -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
$files = @(Get-ChildItem -LiteralPath $publishPath -Force)
if ($files.Count -ne 1 -or $files[0].Name -ne 'SaltSteamEditor.exe') {
    throw '单文件构建产生了额外文件，请检查输出。'
}
$executablePath = Join-Path $publishPath 'Salt Steam 状态编辑器.exe'
Move-Item -LiteralPath $files[0].FullName -Destination $executablePath
Write-Output "单文件程序已生成：$executablePath"
