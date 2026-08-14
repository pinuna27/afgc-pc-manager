[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'AFGCPCManager.slnx'

[xml]$solution = Get-Content -LiteralPath $solutionPath -Raw
$listedProjects = @($solution.Solution.Folder.Project.Path) |
    ForEach-Object { $_.Replace('\', '/').ToLowerInvariant() } |
    Sort-Object -Unique

$sourceRoots = 'src', 'installer', 'tests', 'tools'
$maintainedProjects = foreach ($sourceRoot in $sourceRoots) {
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $sourceRoot) `
        -Filter '*.csproj' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $_.FullName.Substring($repositoryRoot.Length).
                TrimStart('\', '/').Replace('\', '/').ToLowerInvariant()
        }
}
$maintainedProjects = @($maintainedProjects | Sort-Object -Unique)

$missingFromSolution = @($maintainedProjects | Where-Object {
    $_ -notin $listedProjects
})
$staleSolutionEntries = @($listedProjects | Where-Object {
    $_ -notin $maintainedProjects
})
if ($missingFromSolution.Count -gt 0 -or $staleSolutionEntries.Count -gt 0) {
    $detail = @()
    if ($missingFromSolution.Count -gt 0) {
        $detail += "Projects missing from the solution: $($missingFromSolution -join ', ')"
    }
    if ($staleSolutionEntries.Count -gt 0) {
        $detail += "Stale solution entries: $($staleSolutionEntries -join ', ')"
    }
    throw ($detail -join [Environment]::NewLine)
}

foreach ($relativeProject in $maintainedProjects) {
    $projectPath = Join-Path $repositoryRoot $relativeProject
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $packageReferences = @($project.Project.ItemGroup.PackageReference)
    $versionedReferences = @($packageReferences | Where-Object {
        $_.Version -or $_.VersionOverride
    })
    if ($versionedReferences.Count -gt 0) {
        throw "$relativeProject declares package versions locally. Use Directory.Packages.props."
    }

    if ($project.Project.PropertyGroup.ImplicitUsings `
        -or $project.Project.PropertyGroup.Nullable) {
        throw "$relativeProject duplicates repository-wide compiler settings."
    }

    $targetFramework = [string]$project.Project.PropertyGroup.TargetFramework
    if ($targetFramework -notin '$(BaseTargetFramework)', '$(WindowsTargetFramework)') {
        throw "$relativeProject must use a centrally defined target framework."
    }
}

Write-Host "Repository structure is consistent: $($maintainedProjects.Count) projects are centrally configured and included in the solution."
