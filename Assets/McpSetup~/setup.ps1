# One-time Unity MCP setup for Grand Police Auto (run by Assets/GPA/Editor/Setup/UnityMcpSetup.cs).
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $here 'setup.log'
$project = (Resolve-Path (Join-Path $here '..\..')).Path
Start-Transcript -Path $log -Force | Out-Null
Write-Output "Project: $project"

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path','User') + ';' + [Environment]::GetEnvironmentVariable('Path','Machine')
}

Refresh-Path
$cli = Get-Command unity -ErrorAction SilentlyContinue
if (-not $cli -or $cli.Source -like '*\Editor\Unity.exe') {
    Write-Output '== Installing the Unity CLI from unity.com =='
    try { Invoke-RestMethod https://unity.com/install.ps1 | Invoke-Expression } catch { Write-Output "INSTALL FAILED: $_" }
    Refresh-Path
}
$cli = Get-Command unity -ErrorAction SilentlyContinue
if (-not $cli) { Write-Output 'STEP FAILED: unity CLI not found on PATH after install.'; Stop-Transcript | Out-Null; exit 1 }
Write-Output "CLI: $($cli.Source)"
& unity --version --no-banner

Write-Output '== Adding the Pipeline package =='
& unity pipeline install --project-path "$project" --non-interactive --no-banner
Write-Output "pipeline exit: $LASTEXITCODE"

Write-Output '== Registering the MCP server with the Claude desktop app =='
& unity mcp configure claude --project-path "$project" --yes --replace-legacy --non-interactive --no-banner
Write-Output "configure exit: $LASTEXITCODE"

Write-Output 'SETUP COMPLETE'
Stop-Transcript | Out-Null
