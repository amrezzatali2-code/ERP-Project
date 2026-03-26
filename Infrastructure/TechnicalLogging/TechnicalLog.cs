using System;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.TechnicalLogging
{
    /// <summary>
    /// مساعد لتنسيق رسائل التسجيل الفني (أخطاء + سياق) دون تكرار القوالب.
    /// </summary>
    public static class TechnicalLog
    {
        public static void LogTechnicalError(
            this ILogger logger,
            Exception exception,
            string operation,
            string? component,
            string? userName = null)
        {
            if (exception == null) return;

            var inner = exception.InnerException;
            logger.LogError(
                exception,
                "ERP.Technical.Error Operation={Operation} Component={Component} User={User} Message={Message} Inner={Inner}",
                operation,
                component ?? "",
                userName ?? "anonymous",
                exception.Message,
                inner?.Message ?? "");
        }
    }
}
