using HourReport.Api.Models;
using HourReport.Api.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<HourReportOptions>(builder.Configuration.GetSection("HourReport"));
builder.Services.PostConfigure<HourReportOptions>(o =>
{
    // Không cấu hình Slots → dùng mặc định 09..17, nghỉ trưa 12:00-13:00.
    if (o.Slots.Count == 0)
        o.Slots = new[] { 9, 10, 11, 12, 14, 15, 16, 17 }
            .Select(h => new SlotOptions { ReportTime = h.ToString("00"), StartingTime = $"{h - 1:00}:00" })
            .ToList();
});

// Khóa mã hóa mật khẩu mail lưu cạnh dữ liệu; trên Windows khóa được bảo vệ thêm bằng DPAPI.
var dataFolder = builder.Configuration["HourReport:DataFolder"] ?? "Data";
var dataRoot = Path.IsPathRooted(dataFolder) ? dataFolder : Path.Combine(builder.Environment.ContentRootPath, dataFolder);
var dp = builder.Services.AddDataProtection()
    .SetApplicationName("HourReport")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot, "keys")));
if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

builder.Services.AddSingleton<ReportClock>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<ReportSendService>();
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors("web");
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// Phục vụ frontend đã build (vite build → wwwroot).
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();
