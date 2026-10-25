using System.Text.Json;

namespace iucs.readernest.api.Hubs
{
    /// <summary>
    /// The lasting ("Keep marks") annotations drawn over one class's video stage, kept so someone
    /// who joins late or reconnects sees what is still on everyone else's screen. Temporary ink
    /// fades by itself within seconds and is never kept. Ops are the same JSON the clients exchange
    /// (see AnnotationOverlay.tsx): a "stroke" starts a mark, "strokeAppend" extends it, a full
    /// "clear" wipes everything, and a "clear" with temporaryOnly leaves lasting marks alone.
    /// </summary>
    public sealed class AnnotationLog
    {
        /// <summary>Far more than a class ever draws; a ceiling so one client can't grow this without end.</summary>
        public const int MaxOps = 4000;
        public const int MaxOperationLength = 32_000;

        private readonly List<string> _ops = new();
        private readonly HashSet<string> _strokeIds = new(StringComparer.Ordinal);

        /// <summary>Rejects malformed or oversized operations before they are broadcast. A
        /// granted student must not be able to poison every viewer's canvas with arbitrary JSON.</summary>
        public static bool IsValidOperation(string opJson)
        {
            if (string.IsNullOrWhiteSpace(opJson) || opJson.Length > MaxOperationLength)
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(opJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kindElement))
                {
                    return false;
                }

                var kind = kindElement.GetString();
                if (kind == "clear") return true;
                if (kind == "stroke")
                {
                    return root.TryGetProperty("stroke", out var stroke)
                        && stroke.ValueKind == JsonValueKind.Object
                        && HasShortString(stroke, "id")
                        && HasReasonablePoints(stroke, "points");
                }

                return kind == "strokeAppend"
                    && HasShortString(root, "strokeId")
                    && HasReasonablePoints(root, "points");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return false;
            }
        }

        private static bool HasShortString(JsonElement parent, string property) =>
            parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= 128 };

        private static bool HasReasonablePoints(JsonElement parent, string property)
        {
            if (!parent.TryGetProperty(property, out var points) || points.ValueKind != JsonValueKind.Array || points.GetArrayLength() is < 1 or > 512)
            {
                return false;
            }

            foreach (var point in points.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object
                    || !point.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetDouble(out var xValue) || !double.IsFinite(xValue)
                    || !point.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetDouble(out var yValue) || !double.IsFinite(yValue))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Remembers the op if it belongs to a lasting mark, or forgets everything on a full clear.</summary>
        public void Record(string opJson)
        {
            string? kind;
            string? strokeId = null;
            var lasting = false;
            var temporaryOnly = false;
            try
            {
                using var doc = JsonDocument.Parse(opJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kindElement))
                {
                    return;
                }

                kind = kindElement.GetString();
                if (kind == "stroke" && root.TryGetProperty("stroke", out var stroke) && stroke.ValueKind == JsonValueKind.Object)
                {
                    strokeId = stroke.TryGetProperty("id", out var id) ? id.GetString() : null;
                    lasting = stroke.TryGetProperty("persistent", out var persistent) && persistent.ValueKind == JsonValueKind.True;
                }
                else if (kind == "strokeAppend")
                {
                    strokeId = root.TryGetProperty("strokeId", out var id) ? id.GetString() : null;
                }
                else if (kind == "clear")
                {
                    temporaryOnly = root.TryGetProperty("temporaryOnly", out var only) && only.ValueKind == JsonValueKind.True;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not an op this log understands (bad JSON, or "kind"/"id" that isn't a string) --
                // still relayed live, just not kept. It must never throw: SendAnnotation records
                // before it relays, so an exception here would keep a mark off every child's screen.
                return;
            }

            lock (_ops)
            {
                if (kind == "clear")
                {
                    if (!temporaryOnly)
                    {
                        _ops.Clear();
                        _strokeIds.Clear();
                    }

                    return;
                }

                if (strokeId is null || _ops.Count >= MaxOps)
                {
                    return;
                }

                if (kind == "stroke" && lasting)
                {
                    _strokeIds.Add(strokeId);
                    _ops.Add(opJson);
                }
                else if (kind == "strokeAppend" && _strokeIds.Contains(strokeId))
                {
                    _ops.Add(opJson);
                }
            }
        }

        /// <summary>Every kept op, in the order it was drawn.</summary>
        public string[] Snapshot()
        {
            lock (_ops)
            {
                return _ops.ToArray();
            }
        }
    }
}
