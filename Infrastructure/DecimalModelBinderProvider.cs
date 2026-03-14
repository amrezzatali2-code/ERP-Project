using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Infrastructure
{
    /// <summary>
    /// يوفّر DecimalModelBinder لجميع خصائص النوع decimal.
    /// </summary>
    public class DecimalModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?))
                return new DecimalModelBinder();
            return null;
        }
    }
}
