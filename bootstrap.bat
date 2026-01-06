@echo off
REM Bootstrap solution + restore packages
dotnet new sln -n ClinicalSkUrgencySolution
dotnet sln ClinicalSkUrgencySolution.sln add src\ClinicalOrchestrator\ClinicalOrchestrator.csproj
dotnet sln ClinicalSkUrgencySolution.sln add src\MockSchedulingApi\MockSchedulingApi.csproj
dotnet restore ClinicalSkUrgencySolution.sln
echo Done. Open ClinicalSkUrgencySolution.sln in Visual Studio.
