using HourReport.Api.Models;
using HourReport.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HourReport.Api.Controllers;

/// <summary>Cài đặt mail (email đăng nhập, mật khẩu, người nhận) nhập từ giao diện.</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly SettingsStore _settings;
    private readonly MailService _mail;

    public SettingsController(SettingsStore settings, MailService mail)
    {
        _settings = settings;
        _mail = mail;
    }

    [HttpGet]
    public ActionResult<SettingsView> Get() => _settings.View();

    [HttpPut]
    public IActionResult Save([FromBody] SaveSettingsRequest req)
    {
        var errors = SettingsStore.Validate(req);
        if (errors.Count > 0) return BadRequest(new { message = "Cài đặt chưa hợp lệ.", errors });
        return Ok(_settings.Save(req));
    }

    /// <summary>Thử kết nối + đăng nhập SMTP bằng giá trị đang nhập trên form (chưa cần lưu).</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] SaveSettingsRequest req, CancellationToken ct)
    {
        try
        {
            await _mail.TestConnectionAsync(_settings.FromRequest(req), ct);
            return Ok(new { message = $"Đăng nhập {req.Host}:{req.Port} thành công." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var msg = ex is OperationCanceledException ? "Hết thời gian chờ máy chủ SMTP." : ex.Message;
            return BadRequest(new { message = $"Không kết nối được: {msg}" });
        }
    }
}
