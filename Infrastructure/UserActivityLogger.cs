using ERP.Data;                  // سياق قاعدة البيانات AppDbContext
using ERP.Models;                // UserActivityLog, UserActionType
using Microsoft.AspNetCore.Http; // IHttpContextAccessor
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.Infrastructure
{
    /// <summary>
    /// خدمة لتسجيل نشاط المستخدمين في جدول UserActivityLogs.
    /// </summary>
    public interface IUserActivityLogger
    {
        Task LogAsync(
            UserActionType actionType,   // نوع العملية (Login, Create,...)
            string entityName,           // اسم الكيان (PurchaseInvoice, Product,...)
            int? entityId = null,        // رقم السجل لو موجود
            string? description = null,  // وصف بالعربي
            string? oldValues = null,    // JSON اختياري للقيم القديمة
            string? newValues = null     // JSON اختياري للقيم الجديدة
        );
    }

    public class UserActivityLogger : IUserActivityLogger
    {
        private readonly AppDbContext _db;                 // متغير: سياق قاعدة البيانات
        private readonly IHttpContextAccessor _http;       // متغير: للوصول لليوزر والـ IP

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
            // متغير: المستخدم الحالي (لو مسجل دخول)
            int? userId = null;
            var httpContext = _http.HttpContext;

            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                var idClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (idClaim != null && int.TryParse(idClaim.Value, out var parsedId))
                {
                    userId = parsedId;
                }
            }

            // متغير: عنوان الـ IP
            var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();

            // متغير: نوع المتصفح / الجهاز
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();

            // متغير: سطر جديد في سجل النشاط
            var log = new UserActivityLog
            {
                UserId = userId,
                ActionType = actionType,
                EntityName = entityName,
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
