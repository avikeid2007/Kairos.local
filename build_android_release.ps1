param (
    [string]$KeystorePath,
    [string]$KeystorePassword,
    [string]$KeyAlias
)

if ([string]::IsNullOrEmpty($KeystorePath) -or [string]::IsNullOrEmpty($KeystorePassword) -or [string]::IsNullOrEmpty($KeyAlias)) {
    Write-Host "Usage: ./build_android_release.ps1 -KeystorePath <path> -KeystorePassword <pass> -KeyAlias <alias>" -ForegroundColor Red
    exit
}

Write-Host "Building KaiROS Mobile for Android Release..." -ForegroundColor Cyan

dotnet publish KaiROS.Mobile/KaiROS.Mobile.csproj -f net9.0-android -c Release `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=$KeystorePath `
    -p:AndroidSigningKeyAlias=$KeyAlias `
    -p:AndroidSigningKeyPass=$KeystorePassword `
    -p:AndroidSigningStorePass=$KeystorePassword

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build Successful!" -ForegroundColor Green
    
    # Define paths
    $publishDir = "KaiROS.Mobile/bin/Release/net9.0-android/publish"
    $sourceFile = Join-Path $publishDir "com.avnishkumar.kairosai-Signed.aab"
    
    # Create better name: KaiROS_AI_v1.0.0.aab
    $targetName = "KaiROS_AI_v1.0.0.aab"
    $targetFile = Join-Path $publishDir $targetName

    if (Test-Path $sourceFile) {
        Rename-Item -Path $sourceFile -NewName $targetName -Force
        Write-Host "Renamed artifact to: $targetName" -ForegroundColor Green
        Write-Host "Location: $publishDir\$targetName" -ForegroundColor Cyan
        
        # Open folder for user
        explorer.exe $publishDir
    }
    else {
        Write-Host "Could not find signed AAB file to rename." -ForegroundColor Yellow
        Write-Host "Check folder: $publishDir" -ForegroundColor Cyan
    }

}
else {
    Write-Host "Build Failed!" -ForegroundColor Red
}
