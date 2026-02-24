#!/usr/bin/env pwsh
# ef-migrations.ps1 - EF Core migrations helper
# All migrations live in TodoApp.Infrastructure; TodoApp.Api is the startup project.
#
# Usage:
#   .\ef-migrations.ps1 add    <MigrationName>
#   .\ef-migrations.ps1 update
#   .\ef-migrations.ps1 list
#   .\ef-migrations.ps1 remove
#   .\ef-migrations.ps1 script [from] [to]

param(
    [Parameter(Mandatory, Position=0)]
    [ValidateSet("add","update","list","remove","script")]
    [string]$Command,

    [Parameter(Position=1)] [string]$Arg1,
    [Parameter(Position=2)] [string]$Arg2
)

$infrastructure = "$PSScriptRoot/TodoApp.Infrastructure"
$startup        = "$PSScriptRoot/TodoApp.Api"

function ef {
    dotnet ef @args `
        --project $infrastructure `
        --startup-project $startup
}

switch ($Command) {
    "add"    { ef migrations add $Arg1 }
    "update" { ef database update $Arg1 }
    "list"   { ef migrations list }
    "remove" { ef migrations remove }
    "script" { ef migrations script $Arg1 $Arg2 }
}
