powershell
Get-ChildItem -Path . -Recurse -Include *.cs,*.csproj -Exclude bin,obj |
    ForEach-Object {
        "=== $($_.FullName) ===`n"
        Get-Content $_.FullName -Raw
        "`n"
    } | Out-File -Encoding utf8 dump.txt