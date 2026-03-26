using System.IO;
using System.Text;

namespace ERP.Infrastructure;

/// <summary>
/// أسماء ملفات وورقات Excel بالعربية مع طابع زمني، مع تجنب أحرف غير مسموحة.
/// </summary>
public static class ExcelExportNaming
{
    /// <summary>اسم ملف للتحميل: &lt;اسم عربي&gt;_yyyyMMdd_HHmmss.&lt;امتداد&gt;</summary>
    public static string ArabicTimestampedFileName(string arabicBaseName, string extension)
    {
        var ext = string.IsNullOrEmpty(extension)
            ? ".xlsx"
            : (extension.Length > 0 && extension[0] == '.' ? extension : "." + extension);
        var safe = SanitizeFileName(arabicBaseName);
        if (string.IsNullOrEmpty(safe))
            safe = "تصدير";
        return $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
    }

    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "تصدير";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Trim().Length);
        foreach (var c in name.Trim())
        {
            if (Array.IndexOf(invalid, c) >= 0 || c == ':')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>اسم ورقة Excel: حد 31 حرفاً، بدون \ / ? * [ ]</summary>
    public static string SafeWorksheetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "البيانات";
        var s = name.Trim();
        foreach (var c in new[] { '\\', '/', '?', '*', '[', ']' })
            s = s.Replace(c, ' ');
        s = s.Trim();
        if (s.Length > 31)
            s = s[..31].TrimEnd();
        return string.IsNullOrEmpty(s) ? "البيانات" : s;
    }
}
