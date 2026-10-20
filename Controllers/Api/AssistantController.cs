using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api;

[ApiController, Authorize(Policy = "AssistantAccess"), Route("api/assistant"), EnableRateLimiting("assistant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantController(IVoiceAssistantService service, IAntiforgery antiforgery, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("session"), AllowAnonymous]
    public IActionResult Session() => Ok(new {
        configured = service.IsConfigured,
        demo = !service.IsConfigured && environment.IsDevelopment(),
        csrfToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
    });

    [HttpPost("chat"), ValidateAntiForgeryToken, RequestSizeLimit(32768)]
    public Task<IActionResult> Chat(AssistantChatRequest input, CancellationToken ct) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(input.Message) || (input.History ?? []).Any(turn => turn == null || string.IsNullOrWhiteSpace(turn.Content)))
            return BadRequest(new { message = "اكتب سؤالاً واضحاً." });
        if (!service.IsConfigured && environment.IsDevelopment())
            return Ok(new { reply = "هذا رد تجريبي: أنا رواد، معاك خطوة بخطوة. يمكنك الاستماع إلى الترحيب والدعاء العام. المحادثة الذكية تحتاج تفعيل الخدمة.", demo = true, actions = Array.Empty<string>() });
        var answer = await service.ChatAsync(input, ct);
        return Ok(new { reply = answer.Reply, demo = false, actions = answer.Actions });
    }, ct);

    [HttpPost("transcribe"), ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> Transcribe(IFormFile file, CancellationToken ct) => Guard(async () =>
    {
        var formats = new[] { "audio/webm", "audio/mp4", "audio/ogg", "audio/wav", "audio/mpeg" };
        if (file.Length is <= 0 or > 5 * 1024 * 1024 || !formats.Contains(file.ContentType.Split(';')[0]))
            return BadRequest(new { message = "اختر تسجيلاً صوتياً مدعوماً لا يتجاوز 5 ميغابايت." });
        return Ok(new { text = await service.TranscribeAsync(file, ct) });
    }, ct);

    [HttpPost("speech"), ValidateAntiForgeryToken, RequestSizeLimit(32768)]
    public Task<IActionResult> Speech(AssistantSpeechRequest input, CancellationToken ct) => Guard(async () =>
        string.IsNullOrWhiteSpace(input.Text) ? BadRequest(new { message = "النص فارغ." }) : File(await service.SpeechAsync(input.Text, ct), "audio/mpeg"), ct);

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        try { return await action(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(504, new { message = "انتهت مهلة المساعد. حاول مرة أخرى." }); }
        catch (Exception ex) when (ex is VoiceProviderException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        { return StatusCode(503, new { message = "المساعد الصوتي غير متاح الآن. حاول لاحقاً." }); }
    }
}
