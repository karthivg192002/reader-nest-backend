# Whiteboard Library Evaluation (Sprint 0)

## Recommendation: **Excalidraw** (embedded React component) layered beside the Jitsi session.

| Option | Verdict | Notes |
|---|---|---|
| **Excalidraw (chosen)** | ✅ | MIT, React-native embedding, infinite canvas, shapes/text/draw/sticky notes out of the box, collaborative via its own websocket room or our SignalR relay. Jitsi's built-in whiteboard is itself Excalidraw, which validates the pairing. |
| tldraw | ⚠️ | Excellent SDK but moved to a paid "business" license for production watermark removal — conflicts with white-label ownership. |
| Custom canvas (Konva/Fabric) | ⚠️ | Full control for the interactive-activity layer (drag & drop, tag & match, hotspots) but 4–6 weeks to reach Excalidraw parity on core tools. |
| Jitsi built-in whiteboard | ❌ standalone | No programmatic control over content, can't host the gamified activity layer. |

## Architecture
- Sprint 2: embed `@excalidraw/excalidraw` in the classroom UI; sync scene deltas through
  a lightweight websocket hub (SignalR on the API); teacher-controlled interaction
  permissions gate who can write.
- Sprint 3: the interactive activities (drag & drop, tag & match, hotspot click,
  select-correct-answer) are a **custom layer** (Konva) rendered as a board "page" —
  Excalidraw covers freehand teaching; activities need deterministic hit-testing
  and scoring that a drawing library shouldn't own.
- Multi-page board = array of Excalidraw scenes per session, persisted per page.
