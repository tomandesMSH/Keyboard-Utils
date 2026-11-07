# Builds BasicPitch-module.zip: the optional chord detection add-on that Keyboard Utils downloads on demand
# (see src/KeyboardUtils/Modules/BasicPitchModule.cs). Upload the zip as the asset of the GitHub release that
# BasicPitchModule.DownloadUrl points to.
#
# Usage (from the repo root):  powershell -File tools/pack-basic-pitch-module.ps1 [-Out <folder>]

param([string]$Out = "artifacts")

$ErrorActionPreference = "Stop"
$onnxVersion = "1.20.1"   # keep in step with src/BasicPitch/BasicPitch.csproj

$publish = Join-Path $Out "basic-pitch-publish"
$stage = Join-Path $Out "basic-pitch-module"
Remove-Item -Recurse -Force $publish, $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish src/BasicPitch/BasicPitch.csproj -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

foreach ($file in "BasicPitch.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll", "nmp.onnx", "BasicPitch-LICENSE.txt", "BasicPitch-NOTICE.txt") {
    Copy-Item (Join-Path $publish $file) $stage
}

# ONNX Runtime is a PrivateAssets reference (so it stays out of the app's own build), which also leaves its managed
# wrapper out of the publish output - take it, and its license, straight from the NuGet packages.
$packages = Join-Path $env:USERPROFILE ".nuget\packages"
Copy-Item (Join-Path $packages "microsoft.ml.onnxruntime.managed\$onnxVersion\lib\net8.0\Microsoft.ML.OnnxRuntime.dll") $stage
Copy-Item (Join-Path $packages "microsoft.ml.onnxruntime\$onnxVersion\LICENSE") (Join-Path $stage "OnnxRuntime-LICENSE.txt")
Copy-Item (Join-Path $packages "microsoft.ml.onnxruntime\$onnxVersion\ThirdPartyNotices.txt") (Join-Path $stage "OnnxRuntime-ThirdPartyNotices.txt")

$zip = Join-Path $Out "BasicPitch-module.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Get-ChildItem $stage | ForEach-Object { "{0,-36} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB) }
"{0}: {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB)
