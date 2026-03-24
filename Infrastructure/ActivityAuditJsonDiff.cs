using System.Collections.Generic;
using System.Text.Json;

namespace ERP.Infrastructure
{
    /// <summary>
    /// يقلّص لقطتي JSON (قبل/بعد) إلى الحقول التي تغيّرت فقط — لسجل النشاط.
    /// </summary>
    public static class ActivityAuditJsonDiff
    {
        private static readonly JsonSerializerOptions SerializeOpts = new()
        {
            WriteIndented = false
        };

        /// <summary>
        /// إن لم يكونا كائني JSON بسيطين { ... } يُعاد النص الأصلي دون تغيير.
        /// </summary>
        public static (string? oldReduced, string? newReduced) ReduceToChangedProperties(string? oldJson, string? newJson)
        {
            if (string.IsNullOrWhiteSpace(oldJson) || string.IsNullOrWhiteSpace(newJson))
                return (oldJson, newJson);

            var oTrim = oldJson.Trim();
            var nTrim = newJson.Trim();
            if (!oTrim.StartsWith('{') || !nTrim.StartsWith('{'))
                return (oldJson, newJson);

            try
            {
                using var oldDoc = JsonDocument.Parse(oTrim);
                using var newDoc = JsonDocument.Parse(nTrim);
                var oldRoot = oldDoc.RootElement;
                var newRoot = newDoc.RootElement;
                if (oldRoot.ValueKind != JsonValueKind.Object || newRoot.ValueKind != JsonValueKind.Object)
                    return (oldJson, newJson);

                var names = new HashSet<string>();
                foreach (var p in oldRoot.EnumerateObject())
                    names.Add(p.Name);
                foreach (var p in newRoot.EnumerateObject())
                    names.Add(p.Name);

                var oldOut = new Dictionary<string, object?>();
                var newOut = new Dictionary<string, object?>();

                foreach (var name in names)
                {
                    var hasO = oldRoot.TryGetProperty(name, out var elO);
                    var hasN = newRoot.TryGetProperty(name, out var elN);
                    if (!hasO && !hasN)
                        continue;
                    if (hasO && hasN && JsonValuesEqual(elO, elN))
                        continue;

                    oldOut[name] = hasO ? JsonElementToObject(elO) : null;
                    newOut[name] = hasN ? JsonElementToObject(elN) : null;
                }

                if (oldOut.Count == 0)
                    return (null, null);

                return (
                    JsonSerializer.Serialize(oldOut, SerializeOpts),
                    JsonSerializer.Serialize(newOut, SerializeOpts));
            }
            catch
            {
                return (oldJson, newJson);
            }
        }

        private static bool JsonValuesEqual(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind)
                return false;

            return a.ValueKind switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.String => string.Equals(a.GetString(), b.GetString(), System.StringComparison.Ordinal),
                JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
                JsonValueKind.Number => NumbersEqual(a, b),
                _ => a.GetRawText() == b.GetRawText()
            };
        }

        private static bool NumbersEqual(JsonElement a, JsonElement b)
        {
            if (a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db))
                return da == db;
            return a.GetRawText() == b.GetRawText();
        }

        private static object? JsonElementToObject(JsonElement e)
        {
            return e.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => e.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => e.TryGetInt64(out var l) ? l : (object)e.GetDecimal(),
                _ => e.GetRawText()
            };
        }
    }
}
