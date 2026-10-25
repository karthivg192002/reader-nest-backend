using System.Text.Json;

namespace iucs.readernest.api.Hubs
{
    /// <summary>Identifies bounded slide-mark operations sharing the durable Board channel.</summary>
    public static class SlideAnnotationOperation
    {
        public static bool IsValid(string opJson)
        {
            if (string.IsNullOrWhiteSpace(opJson) || opJson.Length > AnnotationLog.MaxOperationLength) return false;

            try
            {
                using var doc = JsonDocument.Parse(opJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("kind", out var kindElement)
                    || kindElement.ValueKind != JsonValueKind.String) return false;

                var kind = kindElement.GetString();
                if (kind == "slideClearAll") return true;
                if (!HasSlide(root)) return false;
                if (kind == "slideClear") return true;
                if (kind == "slideUndo") return HasShortString(root, "strokeId");
                if (kind == "slideStrokeAppend") return HasShortString(root, "strokeId") && HasReasonablePoints(root, "points");

                return kind == "slideStroke"
                    && root.TryGetProperty("stroke", out var stroke)
                    && stroke.ValueKind == JsonValueKind.Object
                    && HasShortString(stroke, "id")
                    && HasReasonablePoints(stroke, "points");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return false;
            }
        }

        private static bool HasSlide(JsonElement root) =>
            root.TryGetProperty("slide", out var slide)
            && slide.ValueKind == JsonValueKind.Number
            && slide.TryGetInt32(out var index)
            && index >= 0;

        private static bool HasShortString(JsonElement parent, string property) =>
            parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= 128 };

        private static bool HasReasonablePoints(JsonElement parent, string property)
        {
            if (!parent.TryGetProperty(property, out var points)
                || points.ValueKind != JsonValueKind.Array
                || points.GetArrayLength() is < 1 or > 512) return false;

            foreach (var point in points.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object
                    || !point.TryGetProperty("x", out var x) || !x.TryGetDouble(out var xValue) || !double.IsFinite(xValue)
                    || !point.TryGetProperty("y", out var y) || !y.TryGetDouble(out var yValue) || !double.IsFinite(yValue)) return false;
            }

            return true;
        }
    }
}
