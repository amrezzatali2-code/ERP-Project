using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace ERP.Services
{
    /// <summary>
    /// إدارة كود برنامج تطبيق الصيدلي (قراءة/حفظ) من ملف إعداد داخلي.
    /// </summary>
    public class MobileAppProgramCodeService
    {
        private const string DefaultProgramCode = "YOUR-ERP-CODE";
        private const string DefaultCompanyName = "شركة التوزيع";
        private const int MaxProgramCodeLength = 100;
        private const int MaxCompanyNameLength = 120;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        private readonly string _settingsFilePath;
        private readonly IConfiguration _configuration;

        public MobileAppProgramCodeService(IHostEnvironment hostEnvironment, IConfiguration configuration)
        {
            _configuration = configuration;
            _settingsFilePath = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "mobile-app-settings.json");
        }

        public async Task<string> GetProgramCodeAsync()
        {
            var settings = await GetSettingsAsync();
            return settings.ProgramCode;
        }

        public async Task<string> GetCompanyNameAsync()
        {
            var settings = await GetSettingsAsync();
            return settings.CompanyName;
        }

        public async Task<MobileAppSettingsSnapshot> GetSettingsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var fromFile = await ReadFromFileUnsafeAsync();
                if (fromFile != null)
                {
                    return fromFile;
                }

                var configProgramCode = (_configuration["MobileApp:ProgramCode"] ?? DefaultProgramCode).Trim();
                var configCompanyName = (_configuration["MobileApp:CompanyName"] ?? DefaultCompanyName).Trim();
                return new MobileAppSettingsSnapshot(
                    NormalizeProgramCode(configProgramCode),
                    NormalizeCompanyName(configCompanyName));
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveProgramCodeAsync(string? programCode)
        {
            var settings = await GetSettingsAsync();
            await SaveSettingsAsync(settings.CompanyName, programCode);
        }

        public async Task SaveSettingsAsync(string? companyName, string? programCode)
        {
            var normalizedProgramCode = NormalizeProgramCode(programCode);
            var normalizedCompanyName = NormalizeCompanyName(companyName);

            await _lock.WaitAsync();
            try
            {
                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var payload = new MobileAppProgramCodeStore
                {
                    ProgramCode = normalizedProgramCode,
                    CompanyName = normalizedCompanyName,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_settingsFilePath, json);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static string NormalizeProgramCode(string? raw)
        {
            var value = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultProgramCode;
            }

            if (value.Length > MaxProgramCodeLength)
            {
                value = value[..MaxProgramCodeLength];
            }

            return value;
        }

        public static string NormalizeCompanyName(string? raw)
        {
            var value = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultCompanyName;
            }

            if (value.Length > MaxCompanyNameLength)
            {
                value = value[..MaxCompanyNameLength];
            }

            return value;
        }

        private async Task<MobileAppSettingsSnapshot?> ReadFromFileUnsafeAsync()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var payload = JsonSerializer.Deserialize<MobileAppProgramCodeStore>(json);
                if (payload == null)
                {
                    return null;
                }

                return new MobileAppSettingsSnapshot(
                    NormalizeProgramCode(payload.ProgramCode),
                    NormalizeCompanyName(payload.CompanyName));
            }
            catch
            {
                return null;
            }
        }

        public sealed record MobileAppSettingsSnapshot(string ProgramCode, string CompanyName);

        private sealed class MobileAppProgramCodeStore
        {
            public string? ProgramCode { get; set; }
            public string? CompanyName { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
        }
    }
}
