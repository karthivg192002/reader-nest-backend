# Analytics KPI List (Sprint 0 finalization)

Source: Software.pdf ("Admin should see") + Reader_Nest_LMS WBS (Dashboard & KPIs,
Management Dashboard, Student Analytics, AI Reports).

## Admin / Management BI dashboard (Sprint 3–4)
| KPI | Definition |
|---|---|
| Total students | Count of active `Child` rows |
| Active students | Children with an Active `BatchEnrollment` |
| Revenue | Sum of successful `PaymentTransaction.Amount`, filterable by period & department |
| Course-wise revenue | Revenue grouped by `Invoice → Subscription/Course` |
| Enrollments | New `BatchEnrollment` per period |
| Conversion rate | `DemoBooking(Enrolled)` ÷ `DemoBooking(DemoCompleted+)` |
| Renewal rate | Re-enrollments after batch completion ÷ completed enrollments |
| Refund rate | Processed `Refund` amount ÷ revenue |
| Teacher utilization | Completed session hours ÷ available hours (leave-adjusted) |
| Attendance rate | Present ÷ expected from `SessionAttendance` |
| Batch occupancy | Enrolled ÷ `Batch.Capacity` |
| Active vs dormant batches | `Batch.Status` split |

## Student analytics (Sprint 4)
Participation, click activity, quiz attempts & scores, whiteboard interaction,
attention signals, engagement score, learning-outcome indicators, talk-time,
camera attentiveness, session completion — captured per session into an
engagement metrics table (schema lands with the gamification sprint).

## Exports
CSV for attendance, revenue, payout and conversion reports (Sprint 4).
