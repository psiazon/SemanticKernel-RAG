var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { ok = true, service = "MockSchedulingApi" }));

app.MapPost("/schedule/blooddraw", (ScheduleRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.PatientName))
        return Results.BadRequest(new { error = "PatientName is required" });

    var confirmation = new
    {
        test = "blood_draw",
        patient = req.PatientName,
        urgency = req.Urgency ?? "urgent",
        reason = req.Reason,
        appointmentTimeUtc = DateTime.UtcNow.AddHours((req.Urgency ?? "urgent") == "urgent" ? 2 : 48).ToString("o"),
        confirmationId = $"BD-{Random.Shared.Next(100000, 999999)}"
    };

    return Results.Ok(confirmation);
});

app.MapPost("/schedule/xray", (ScheduleRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.PatientName))
        return Results.BadRequest(new { error = "PatientName is required" });

    var confirmation = new
    {
        test = "xray",
        patient = req.PatientName,
        urgency = req.Urgency ?? "urgent",
        bodyPart = req.BodyPart ?? "Unknown",
        reason = req.Reason,
        appointmentTimeUtc = DateTime.UtcNow.AddHours((req.Urgency ?? "urgent") == "urgent" ? 4 : 72).ToString("o"),
        confirmationId = $"XR-{Random.Shared.Next(100000, 999999)}"
    };

    return Results.Ok(confirmation);
});

app.MapPost("/schedule/mri", (ScheduleRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.PatientName))
        return Results.BadRequest(new { error = "PatientName is required" });

    var confirmation = new
    {
        test = "mri",
        patient = req.PatientName,
        urgency = req.Urgency ?? "urgent",
        bodyPart = req.BodyPart ?? "Unknown",
        reason = req.Reason,
        appointmentTimeUtc = DateTime.UtcNow.AddHours((req.Urgency ?? "urgent") == "urgent" ? 8 : 120).ToString("o"),
        confirmationId = $"MRI-{Random.Shared.Next(100000, 999999)}"
    };

    return Results.Ok(confirmation);
});

app.Run();

internal sealed class ScheduleRequest
{
    public string PatientName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Urgency { get; set; } = "urgent";
    public string? BodyPart { get; set; }
}
