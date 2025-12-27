using ERP.Data;                  // سياق قاعدة البيانات AppDbContext
using ERP.Models;                // UserActivityLog, UserActionType
using Microsoft.AspNetCore.Http; // IHttpContextAccessor
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.Infrastructure
{
    /// <summary>
    /// خدمة لتسجيل نشاط المستخدمين في جدول UserActivityLogs.
    /// 
    /// سياسة التسجيل هنا (حسب طلبك):
    /// - نسجل "التعديل" و "الحذف" فقط
    /// - نتجاهل Create / View / Import / Export ... لتقليل الحمل
    /// </summary>
    public interface IUserActivityLogger
    {
        Task LogAsync(
            UserActionType actionType,   // متغير: نوع العملية
            string entityName,           // متغير: اسم الكيان (PurchaseInvoice / PILine / Product ...)
            int? entityId = null,        // متغير: رقم السجل لو موجود
            string? description = null,  // متغير: وصف مختصر
            string? oldValues = null,    // متغير: JSON للقيم قبل التعديل/قبل الحذف
            string? newValues = null     // متغير: JSON للقيم بعد التعديل
        );
    }

    public class UserActivityLogger : IUserActivityLogger
    {
        private readonly AppDbContext _db;           // متغير: سياق قاعدة البيانات
        private readonly IHttpContextAccessor _http; // متغير: للوصول للمستخدم الحالي + IP + UserAgent

        public UserActivityLogger(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public async Task LogAsync(
            UserActionType actionType,
            string entityName,
            int? entityId = null,
            string? description = null,
            string? oldValues = null,
            string? newValues = null)
        {
            // =========================================================
            // (1) فلترة: نسجل "التعديل" و"الحذف" فقط
            // =========================================================
            if (actionType != UserActionType.Edit && actionType != UserActionType.Delete)
                return; // ✅ تجاهل أي نوع آخر لتخفيف اللوج والبرنامج

            // =========================================================
            // (2) حماية بسيطة: لازم اسم كيان
            // =========================================================
            if (string.IsNullOrWhiteSpace(entityName))
                return;

            // =========================================================
            // (3) لو مفيش أي معلومة مفيدة (لا وصف ولا قبل/بعد) يبقى ملهاش معنى
            // =========================================================
            if (string.IsNullOrWhiteSpace(description) &&
                string.IsNullOrWhiteSpace(oldValues) &&
                string.IsNullOrWhiteSpace(newValues))
                return;

            // =========================================================
            // (4) قراءة بيانات المستخدم الحالي (اختياري)
            // =========================================================
            int? userId = null; // متغير: رقم المستخدم إن وُجد
            var ctx = _http.HttpContext;

            if (ctx?.User?.Identity?.IsAuthenticated == true)
            {
                var idClaim = ctx.User.FindFirst(ClaimTypes.NameIdentifier);
                if (idClaim != null && int.TryParse(idClaim.Value, out var parsed))
                    userId = parsed;
            }

            // متغيرات: IP و UserAgent
            var ip = ctx?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = ctx?.Request?.Headers["User-Agent"].ToString();

            // =========================================================
            // (5) إنشاء سجل اللوج وحفظه
            // =========================================================
            var log = new UserActivityLog
            {
                UserId = userId,
                ActionType = actionType,
                EntityName = entityName.Trim(),
                EntityId = entityId,
                Description = description,
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = ip,
                UserAgent = userAgent,
                ActionTime = DateTime.UtcNow
            };

            _db.UserActivityLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
