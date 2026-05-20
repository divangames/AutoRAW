#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$envFile = Join-Path $here 'deploy.env'

function Read-DeployEnv([string]$path) {
    $vars = @{}
    foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $k = $t.Substring(0, $i).Trim()
        $v = $t.Substring($i + 1).Trim().Trim('"').Trim("'")
        $vars[$k] = $v
    }
    return $vars
}

function Deploy-Ssh {
    param($Cfg)
    foreach ($key in @('SSH_HOST', 'SSH_USER', 'REMOTE_PATH')) {
        if (-not $Cfg[$key]) { throw "deploy.env missing: $key" }
    }

    $port = if ($Cfg['SSH_PORT']) { $Cfg['SSH_PORT'] } else { '22' }
    if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
        throw 'scp not found. Install OpenSSH Client in Windows Optional Features.'
    }

    $remote = '{0}@{1}:{2}/' -f $Cfg['SSH_USER'], $Cfg['SSH_HOST'], $Cfg['REMOTE_PATH'].TrimEnd('/')
    $scpArgs = @('-r', '-P', $port)
    if ($Cfg['SSH_KEY']) { $scpArgs += @('-i', $Cfg['SSH_KEY']) }
    $scpArgs += @(
        (Join-Path $here 'index.html'),
        (Join-Path $here 'style.css'),
        (Join-Path $here 'script.js'),
        (Join-Path $here 'images')
    )
    $scpArgs += $remote

    Write-Host "SSH deploy -> $remote"
    & scp @scpArgs
    if ($LASTEXITCODE -ne 0) { throw "scp exit code $LASTEXITCODE" }
    if ($Cfg['PUBLIC_URL']) { Write-Host "Done: $($Cfg['PUBLIC_URL'])" } else { Write-Host 'Done.' }
}

function Deploy-GitHubPages {
    $repoRoot = (Resolve-Path (Join-Path $here '..')).Path
    Push-Location $repoRoot
    try {
        $branch = git rev-parse --abbrev-ref HEAD 2>$null
        if ($branch -ne 'main') {
            Write-Warning "Branch is $branch (GitHub Pages expects main)."
        }

        $changes = git status --porcelain -- presentation/ .github/workflows/presentation-pages.yml 2>$null
        if ($changes) {
            Write-Host 'Committing presentation...'
            git add presentation/ .github/workflows/presentation-pages.yml .gitignore
            git commit -m "deploy(presentation): update investor landing"
            if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
        } else {
            Write-Host 'No file changes; push only to rebuild Pages.'
        }

        Write-Host 'git push origin main...'
        git push origin main
        if ($LASTEXITCODE -ne 0) { throw 'git push failed' }

        Write-Host ''
        Write-Host 'Pushed. Workflow: Actions -> Deploy presentation'
        Write-Host 'Site (after 1-2 min): https://divangames.github.io/AutoRAW/'
        Write-Host ''
        Write-Host 'If 404: Settings -> Pages -> Deploy from branch -> gh-pages / (root).'
    } finally {
        Pop-Location
    }
}

if (Test-Path -LiteralPath $envFile) {
    Deploy-Ssh (Read-DeployEnv $envFile)
} else {
    Write-Host 'No deploy.env -> GitHub Pages (git push).'
    Deploy-GitHubPages
}
