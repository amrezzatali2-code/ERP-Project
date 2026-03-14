using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Globalization;

namespace ERP.Infrastructure
{
    /// <summary>
    /// ربط القيم العشرية (decimal) باستخدام ثقافة ثابتة وقبول الفاصلة أو النقطة كفاصل عشري
    /// لتفادي خطأ "must be a number" عند الحفظ (الحد الائتماني، سعر الجمهور، إلخ).
    /// </summary>
    public class DecimalModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            var modelName = bindingContext.ModelName;
            var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
            if (valueProviderResult == ValueProviderResult.None)
                return Task.CompletedTask;

            bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);
            var value = valueProviderResult.FirstValue;
            if (string.IsNullOrWhiteSpace(value))
                return Task.CompletedTask;

            value = value.Trim();
            // استبدال الفاصلة بنقطة لضمان التحويل بغض النظر عن لغة المتصفح
            value = value.Replace(',', '.');
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            {
                var modelType = bindingContext.ModelMetadata.ModelType;
                if (modelType == typeof(decimal?))
                    bindingContext.Result = ModelBindingResult.Success((decimal?)result);
                else
                    bindingContext.Result = ModelBindingResult.Success(result);
                return Task.CompletedTask;
            }

            bindingContext.ModelState.TryAddModelError(modelName, "القيمة يجب أن تكون رقماً صحيحاً أو عشرياً.");
            return Task.CompletedTask;
        }
    }
}
