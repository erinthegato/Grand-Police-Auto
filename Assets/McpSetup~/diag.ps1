$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $here 'configure.log'
$env:Path = [Environment]::GetEnvironmentVariable('Path','User') + ';' + [Environment]::GetEnvironmentVariable('Path','Machine')
$project = (Resolve-Path (Join-Path $here '..\..')).Path
"== unity mcp configure claude --project-path `"$project`" --yes ==" | Out-File $log -Encoding utf8
& unity mcp configure claude --project-path "$project" --yes --no-banner --no-color 2>&1 | Out-File $log -Append -Encoding utf8
"exit: $LASTEXITCODE" | Out-File $log -Append -Encoding utf8
"== unity status ==" | Out-File $log -Append -Encoding utf8
& unity status --project-path "$project" --no-banner --no-color 2>&1 | Out-File $log -Append -Encoding utf8
"DONE" | Out-File $log -Append -Encoding utf8
