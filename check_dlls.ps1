$dlls = @('CRYPT32.dll','KERNEL32.dll','USER32.dll','GDI32.dll','COMDLG32.dll','ADVAPI32.dll','ole32.dll','OLEAUT32.dll','MFPlat.DLL','MF.dll','MFReadWrite.dll','dxgi.dll','d3d11.dll','SHLWAPI.dll','bcrypt.dll','WS2_32.dll')
foreach ($d in $dlls) {
    $p = Join-Path 'C:\Windows\System32' $d
    if (Test-Path $p) { Write-Host "OK: $d" } else { Write-Host "MISSING: $d" }
}
