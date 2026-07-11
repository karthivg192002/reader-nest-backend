// k6 load test: live-classroom concurrency against the Reader Nest API.
// Simulates a class start: teachers pull their agenda and open the session
// (join metadata), while the calendar endpoints take parallel read load.
//
//   k6 run load-tests/live-classroom-load.js \
//     -e BASE_URL=http://localhost:5288 -e TOKEN=<teacher-jwt>
//
// Media-plane load (actual video/audio through the JVB) is generated separately
// with jitsi-meet-torture against meet.techmisai.com; this script covers the
// platform side of a class start. See docs/LONG_DURATION_SESSIONS.md.

import http from "k6/http";
import { check, sleep } from "k6";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5288";
const TOKEN = __ENV.TOKEN || "";

export const options = {
  scenarios: {
    class_start_spike: {
      // 30 concurrent group classes x ~6 participants joining within 2 minutes
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "2m", target: 180 },
        { duration: "5m", target: 180 },
        { duration: "1m", target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<800"],
  },
};

const params = {
  headers: {
    Accept: "application/json",
    ...(TOKEN ? { Authorization: `Bearer ${TOKEN}` } : {}),
  },
};

export default function () {
  const health = http.get(`${BASE_URL}/health`);
  check(health, { "health 200": (r) => r.status === 200 });

  if (TOKEN) {
    const from = new Date(Date.now() - 86400_000).toISOString();
    const to = new Date(Date.now() + 86400_000).toISOString();
    const agenda = http.get(`${BASE_URL}/api/sessions/mine?fromUtc=${from}&toUtc=${to}`, params);
    check(agenda, { "agenda 200": (r) => r.status === 200 });
  }

  sleep(1);
}
