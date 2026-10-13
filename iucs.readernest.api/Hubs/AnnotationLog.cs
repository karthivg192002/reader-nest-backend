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

        private readonly List<string> _ops = new();
        private readonly HashSet<string> _strokeIds = new(StringComparer.Ordinal);

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
            catch (JsonException)
            {
                return; // not an op this log understands -- still relayed live, just not kept
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
