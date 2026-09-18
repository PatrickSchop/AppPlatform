# Run this script after closing VS Code to complete the directory renames
# This script renames the remaining project directories from Wisdi to Ps

$renames = @(
    @{ src = 'C:\Dev\AppPlatform\src\Wisdi.AppPlatform'; dest = 'C:\Dev\AppPlatform\src\Ps.AppPlatform' },
    @{ src = 'C:\Dev\AppPlatform\src\Wisdi.AppPlatform.Functions'; dest = 'C:\Dev\AppPlatform\src\Ps.AppPlatform.Functions' },
    @{ src = 'C:\Dev\AppPlatform\tests\Wisdi.AppPlatform.Tests'; dest = 'C:\Dev\AppPlatform\tests\Ps.AppPlatform.Tests' }
)

foreach ($rename in $renames) {
    if (Test-Path $rename.src) {
        $newName = Split-Path $rename.dest -Leaf
        try {
            Rename-Item -Path $rename.src -NewName $newName -ErrorAction Stop
            Write-Host "✓ Successfully renamed: $(Split-Path $rename.src -Leaf) → $newName"
        } catch {
            Write-Host "✗ Failed to rename $($rename.src): $($_.Exception.Message)"
        }
    } else {
        Write-Host "⊘ Directory not found: $($rename.src)"
    }
}

Write-Host "`nAfter renaming, update the .sln and .slnx files to reference the new directory names."
