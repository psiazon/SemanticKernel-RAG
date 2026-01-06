# ClinicalSkUrgencySolution

Two projects:

1) **ClinicalOrchestrator** (Console)
   - Uses **Semantic Kernel** to evaluate clinical data for urgency.
   - If urgent, schedules tests via **real HTTP calls** (HttpClient) to the mock API.
   - Produces a final LLM response summarizing the clinical data + actions taken.

2) **MockSchedulingApi** (ASP.NET Core Minimal API)
   - Provides local HTTP endpoints:
     - POST /schedule/blooddraw
     - POST /schedule/xray
     - POST /schedule/mri
   - Returns confirmation IDs + appointment times.

## Prereqs
- .NET SDK 8.x
- OpenAI or Azure OpenAI credentials

## Run (debug locally)

### 1) Bootstrap the solution
Windows:
```bat
bootstrap.bat
```

macOS/Linux:
```bash
chmod +x bootstrap.sh
./bootstrap.sh
```

### 2) Start the Mock API
```bash
cd src/MockSchedulingApi
dotnet run
```

By default it listens on:
- https://localhost:7246
- http://localhost:5246

### 3) Configure the Console app
Set environment variables:

**OpenAI**
- `OPENAI_API_KEY`
- `OPENAI_MODEL` (default: gpt-4o-mini)

**Azure OpenAI**
- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_API_KEY`
- `AZURE_OPENAI_DEPLOYMENT`

Scheduling API base URL:
- `SCHEDULING_API_BASE_URL` (default: http://localhost:5246)

### 4) Run the Orchestrator
```bash
cd src/ClinicalOrchestrator
dotnet run
```

## Notes
- The triage step asks the LLM to return strict JSON; if it returns invalid JSON, the app throws an error. In production, implement retry/repair logic.
- Demo only; not medical advice.
