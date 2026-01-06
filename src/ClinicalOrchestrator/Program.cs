using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

internal sealed class ClinicalDecision
{
    public string urgency { get; set; } = "unknown"; // "urgent" | "non_urgent"
    public string urgency_reason { get; set; } = "";
    public TestsToSchedule tests_to_schedule { get; set; } = new();
}

internal sealed class TestsToSchedule
{
    public bool blood_draw { get; set; }
    public bool xray { get; set; }
    public bool mri { get; set; }
    public string notes { get; set; } = "";
}

internal sealed class SchedulingResults
{
    public List<JsonElement> scheduled { get; set; } = new();
    public List<object> skipped { get; set; } = new();
}

internal sealed class ScheduleRequest
{
    public string PatientName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Urgency { get; set; } = "urgent";
    public string? BodyPart { get; set; } // for imaging
}

internal static class Program
{
    private static async Task Main()
    {
        // -----------------------------
        // Example input
        // -----------------------------
        var patientName = "John Doe";
        var clinicalData =
@"58M with sudden onset left-sided weakness and slurred speech starting 45 minutes ago.
BP 190/110, history of atrial fibrillation, on/off anticoagulation.
Reports severe headache, nausea. No trauma reported.";

        // -----------------------------
        // Build Semantic Kernel
        // -----------------------------
        var kernel = BuildKernelFromEnv();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        // -----------------------------
        // Decide urgency + tests (LLM -> strict JSON)
        // -----------------------------
        var decision = await GetClinicalDecisionAsync(chat, patientName, clinicalData);

        // -----------------------------
        // Real HTTP scheduling calls (HttpClient)
        // -----------------------------
        var schedulingApiBaseUrl = Environment.GetEnvironmentVariable("SCHEDULING_API_BASE_URL")
                                   ?? "http://localhost:5246";

        using var http = new HttpClient { BaseAddress = new Uri(schedulingApiBaseUrl) };

        var scheduling = await ScheduleIfUrgentAsync(http, patientName, clinicalData, decision);

        // -----------------------------
        // Final LLM response summarizing everything
        // -----------------------------
        var finalResponse = await GetFinalLlmResponseAsync(chat, patientName, clinicalData, decision, scheduling);

        Console.WriteLine("==== Decision JSON ====");
        Console.WriteLine(JsonSerializer.Serialize(decision, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine("\n==== Scheduling Results ====");
        Console.WriteLine(JsonSerializer.Serialize(scheduling, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine("\n==== Final LLM Response ====");
        Console.WriteLine(finalResponse);
    }

    private static Kernel BuildKernelFromEnv()
    {
        // Choose OpenAI vs Azure OpenAI based on which env vars are present.
        // OpenAI:
        //   OPENAI_API_KEY, (optional) OPENAI_MODEL
        // Azure OpenAI:
        //   AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

        var builder = Kernel.CreateBuilder();

        if (!string.IsNullOrWhiteSpace(azureEndpoint) &&
            !string.IsNullOrWhiteSpace(azureKey) &&
            !string.IsNullOrWhiteSpace(azureDeployment))
        {
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: azureDeployment,
                endpoint: azureEndpoint,
                apiKey: azureKey);
        }
        else if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            builder.AddOpenAIChatCompletion(modelId: model, apiKey: openAiKey);
        }
        else
        {
            throw new InvalidOperationException(
                "No LLM credentials found. Set OPENAI_API_KEY (and optionally OPENAI_MODEL), " +
                "or set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");
        }

        return builder.Build();
    }

    private static async Task<ClinicalDecision> GetClinicalDecisionAsync(
        IChatCompletionService chat,
        string patientName,
        string clinicalData)
    {
        var system =
@"You are a clinical triage assistant.
Goal: decide if this case is URGENT and which tests to schedule.

Return ONLY valid JSON that matches this schema (no markdown, no extra text):
{
  ""urgency"": ""urgent|non_urgent"",
  ""urgency_reason"": ""string"",
  ""tests_to_schedule"": {
    ""blood_draw"": true|false,
    ""xray"": true|false,
    ""mri"": true|false,
    ""notes"": ""string""
  }
}

Guidance:
- Mark urgent if symptoms suggest immediate risk (e.g., stroke, MI, sepsis, severe neuro deficits, unstable vitals).
- Prefer the minimum necessary tests, but do not under-triage.
- Assume scheduling is allowed only when urgent; if non_urgent, set all tests false.";

        var user =
$@"Patient: {patientName}

Clinical data:
{clinicalData}";

        var history = new ChatHistory();
        history.AddSystemMessage(system);
        history.AddUserMessage(user);

        var result = await chat.GetChatMessageContentAsync(history);

        ClinicalDecision? decision;
        try
        {
            decision = JsonSerializer.Deserialize<ClinicalDecision>(result.Content ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"LLM did not return valid JSON. Raw content:\n{result.Content}", ex);
        }

        decision ??= new ClinicalDecision { urgency = "unknown" };

        // Guard: if non-urgent, force all tests false
        if (decision.urgency.Equals("non_urgent", StringComparison.OrdinalIgnoreCase))
        {
            decision.tests_to_schedule.blood_draw = false;
            decision.tests_to_schedule.xray = false;
            decision.tests_to_schedule.mri = false;
        }

        return decision;
    }

    private static async Task<SchedulingResults> ScheduleIfUrgentAsync(
        HttpClient http,
        string patientName,
        string clinicalData,
        ClinicalDecision decision)
    {
        var results = new SchedulingResults();

        if (!decision.urgency.Equals("urgent", StringComparison.OrdinalIgnoreCase))
        {
            results.skipped.Add(new { reason = "Case is not urgent; no tests scheduled." });
            return results;
        }

        var reason = $"{decision.urgency_reason}. Notes: {decision.tests_to_schedule.notes}. Clinical: {TrimForReason(clinicalData)}";

        // Blood draw
        if (decision.tests_to_schedule.blood_draw)
        {
            var payload = new ScheduleRequest
            {
                PatientName = patientName,
                Reason = reason,
                Urgency = "urgent"
            };

            results.scheduled.Add(await PostAndParseJsonAsync(http, "/schedule/blooddraw", payload));
        }
        else
        {
            results.skipped.Add(new { test = "blood_draw", reason = "Not selected by triage." });
        }

        // X-ray
        if (decision.tests_to_schedule.xray)
        {
            var payload = new ScheduleRequest
            {
                PatientName = patientName,
                Reason = reason,
                Urgency = "urgent",
                BodyPart = "Chest/Head (as indicated)"
            };

            results.scheduled.Add(await PostAndParseJsonAsync(http, "/schedule/xray", payload));
        }
        else
        {
            results.skipped.Add(new { test = "xray", reason = "Not selected by triage." });
        }

        // MRI
        if (decision.tests_to_schedule.mri)
        {
            var payload = new ScheduleRequest
            {
                PatientName = patientName,
                Reason = reason,
                Urgency = "urgent",
                BodyPart = "Brain (as indicated)"
            };

            results.scheduled.Add(await PostAndParseJsonAsync(http, "/schedule/mri", payload));
        }
        else
        {
            results.skipped.Add(new { test = "mri", reason = "Not selected by triage." });
        }

        return results;
    }

    private static async Task<JsonElement> PostAndParseJsonAsync(HttpClient http, string path, object payload)
    {
        using var resp = await http.PostAsJsonAsync(path, payload);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Scheduling API call failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nPath: {path}\nBody: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static async Task<string> GetFinalLlmResponseAsync(
        IChatCompletionService chat,
        string patientName,
        string clinicalData,
        ClinicalDecision decision,
        SchedulingResults scheduling)
    {
        var system =
@"You are a clinical assistant summarizing actions taken.
Write a short response that:
- echoes the clinical data context
- states urgency and why
- lists scheduled tests (with confirmation IDs and times if present)
- includes any tests not scheduled and why
Keep it concise and clear.";

        var user =
$@"Patient: {patientName}

Clinical data:
{clinicalData}

Triage decision JSON:
{JsonSerializer.Serialize(decision)}

Scheduling results JSON:
{JsonSerializer.Serialize(scheduling)}";

        var history = new ChatHistory();
        history.AddSystemMessage(system);
        history.AddUserMessage(user);

        var result = await chat.GetChatMessageContentAsync(history);
        return result.Content ?? "";
    }

    private static string TrimForReason(string text, int max = 240)
    {
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..max] + "...";
    }
}
