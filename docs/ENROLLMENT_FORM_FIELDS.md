# Enrollment Form — Proposed Field List (awaiting client sign-off)

The sprint plan flags the enrollment field list as a client-supplied blocking
dependency for Sprint 2. To unblock development, this is the proposed list sent
for confirmation; the `EnrollmentForm` entity stores answers as a JSON document
(`FormDataJson`), so client edits to this list need **no schema migration**.

## Parent / Guardian
| Field | Type | Required |
|---|---|---|
| Full name | text | ✔ |
| Relationship to child | select (Mother/Father/Guardian) | ✔ |
| Email | email | ✔ (prefilled from account) |
| Phone / WhatsApp | phone | ✔ |
| Time zone | select (IANA) | ✔ (prefilled) |
| Preferred contact method | select | – |

## Child
| Field | Type | Required |
|---|---|---|
| Full name | text | ✔ |
| Date of birth | date | ✔ |
| Gender | select | – |
| School name & grade | text | ✔ |
| First language / languages spoken | text | – |

## Academic
| Field | Type | Required |
|---|---|---|
| Programme interest | select (Phonics/Maths/Both) | ✔ |
| Current reading/maths level (parent's view) | textarea | ✔ |
| Prior tutoring or programme experience | textarea | – |
| Specific goals or concerns | textarea | – |
| Learning needs / accommodations | textarea | – |

## Scheduling & preferences
| Field | Type | Required |
|---|---|---|
| Preferred days | multi-select | ✔ |
| Preferred time window | select | ✔ |
| Batch preference | select (1:1 / Group / No preference) | – |

## Consents
| Field | Type | Required |
|---|---|---|
| Session recording consent (15-day parent playback) | checkbox | ✔ |
| Photo/celebration moments in-class display | checkbox | – |
| Terms & privacy acceptance | checkbox | ✔ |

**Status:** proposal drafted 2026-07-10; blocking dependency clears when the
client confirms or edits this list. Implementation (mandatory first-login form,
admin review/approve/download) is scheduled in Sprint 2 (Squad D).
