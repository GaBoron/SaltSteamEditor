param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$publishPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $publishPath) -and (Get-ChildItem -LiteralPath $publishPath -Force | Select-Object -First 1)) {
    throw '请选择新的或空的输出目录，以免混入旧文件。'
}

$projectPath = Join-Path $PSScriptRoot 'src/SaltSteamEditor/SaltSteamEditor.csproj'
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }

# Runtime packs keep their redistribution notices outside the published DLLs.
$assets = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'src/SaltSteamEditor/obj/project.assets.json') | ConvertFrom-Json
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $publishPath 'SaltSteamEditor.runtimeconfig.json') | ConvertFrom-Json
$licensePath = Join-Path $publishPath 'licenses'
New-Item -ItemType Directory -Path $licensePath -Force | Out-Null
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packagePath = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($packageId + '/' + $framework.version)
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packagePath = $candidate
            break
        }
    }
    if ($null -eq $packagePath) { throw "找不到运行时许可来源：$packageId" }
    $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
    if ($notices.Count -eq 0) { throw "运行时缺少许可文件：$packagePath" }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licensePath ($framework.name + '-' + $notice.BaseName + '.txt'))
    }
}
Write-Output "便携版已生成：$publishPath"
