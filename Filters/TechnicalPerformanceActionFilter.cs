using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ERP.Infrastructure.TechnicalLogging;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Filters
{
    /// <summary>
    /// قياس زمن تنفيذ أكشنات MVC وتسجيلها كـ Performance (منفصل عن Audit).
    /// </summary>
    public sealed class TechnicalPerformanceActionFilter : IAsyncActionFilter
    {
        private readonly ILogger<TechnicalPerformanceActionFilter> _logger;
        private readonly TechnicalLoggingOptions _options;

        public TechnicalPerformanceActionFilter(
            ILogger<TechnicalPerformanceActionFilter> logger,
            IOptions<TechnicalLoggingOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!_options.EnableMvcPerformance)
            {
                await next();
                return;
            }

            var route = $"{context.RouteData.Values["controller"]}/{context.RouteData.Values["action"]}";
            var user = context.HttpContext.User?.Identity?.IsAuthenticated == true
                ? (context.HttpContext.User.Identity?.Name ?? "?")
                : "anonymous";

            var sw = Stopwatch.StartNew();
            try
            {
                var executed = await next();
                sw.Stop();

                var hasErr = executed.Exception != null;
                var unhandled = hasErr && !executed.ExceptionHandled;
                if (unhandled && executed.Exception != null)
                {
                    _logger.LogError(
                        executed.Exception,
                        "ERP.Technical.Perf MVC {Route} DurationMs={DurationMs} Success=false User={User}",
                        route, sw.ElapsedMilliseconds, user);
                    return;
                }

                var minMs = _options.MvcPerformanceMinDurationMs;
                if (minMs > 0 && sw.ElapsedMilliseconds < minMs)
                    return;

                var success = !hasErr || executed.ExceptionHandled;
                _logger.LogInformation(
                    "ERP.Technical.Perf MVC {Route} DurationMs={DurationMs} Success={Success} User={User}",
                    route, sw.ElapsedMilliseconds, success, user);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "ERP.Technical.Perf MVC {Route} DurationMs={DurationMs} Success=false User={User}",
                    route, sw.ElapsedMilliseconds, user);
                throw;
            }
        }
    }
}
