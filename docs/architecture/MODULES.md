# Modules, Data Flow and Claimant Alerts

Status: **DRAFT** (owner: `architect`; domain input from `business-analyst`). Lifecycle, payment, ledger and pool fund reconciled with `docs/domain/business-rules.md` (BR-01 to BR-73). Decisions: `docs/adr/`. Mermaid renders in GitHub and the VS Code Markdown preview.

## 0. Phase boundary and cut line

Human decision: 5-hour cap kept. **Phase 1 (this build; what docker-compose runs today is in `docs/architecture/ARCHITECTURE.md` section 10):** Gateway, Identity, Claims (trail, maker-checker on the decision), Reporting, Notification; demo ends at **Approved**. **Phase 2 (designed, NOT built):** Finance service = Payment, General Ledger, Pool Fund; settlement maker-checker; `Settling`/`Settled`. In every diagram below, **dashed or "PHASE 2" labelled = designed, not built**; in tables see the Phase column. Finance is not in the Phase 1 docker-compose. **Cut line:** about 2:45-3:00 into the build, Claims + trail + decision maker-checker + Reporting + Notification work end to end; remaining time is hardening and tests, not Finance. Extension points left in Claims: see `docs/architecture/ARCHITECTURE.md` section 0.

## 1. Module map

```mermaid
flowchart TB
    subgraph Clients
        FE[Angular app<br/>Claimant / Officer / Manager areas]
    end

    GW[Gateway<br/>routing, JWT check, CORS]

    subgraph Identity
        ID1[Users and roles<br/>Claimant, Officer, Manager]
        ID2[Dev token issuing RS256]
    end

    subgraph Claims["Claims service (system of record for claims and reserve)"]
        C1[Claim intake<br/>policy check, claim number]
        C2[Assignment<br/>pick-up, reassign]
        C3[Assessment<br/>notes, assessed amount, info requests]
        C4[Maker-checker approvals<br/>decision and settlement proposals]
        C5[State machine<br/>valid transitions only]
        C6[Claim progress trail<br/>append-only history]
        C7[Outbox publisher]
        C8[Reminder scheduler<br/>info-request due dates]
        C9[Finance event consumer - PHASE 2<br/>Settled, back to Approved, trail]
    end

    K1[(Kafka claims.events + dlq)]
    K2[(Kafka finance.events + dlq<br/>PHASE 2)]

    subgraph Finance["Finance service - PHASE 2: designed, not built (one database, three schemas)"]
        direction TB
        P1[Payment module<br/>payee details, instruction,<br/>retries, manager actions]
        P2[Simulated bank gateway]
        L1[Ledger module<br/>append-only double entry,<br/>trial balance]
        F1[Pool Fund module<br/>fund view, top-up, thresholds]
        P1 --> P2
        P1 -->|IPostingService, one tx| L1
        F1 -->|IBalanceQuery| L1
        P1 -->|funds check| F1
        FO[Outbox publisher + event consumer]
    end

    subgraph Reporting["Reporting service (read models)"]
        R1[Outstanding claims]
        R2[Liability exposure + in settlement]
        R3[Officer workload and performance]
        R4[Checker queue for managers]
        R5[Time in status, settlement time]
        R6[Payment stats and exceptions count]
    end

    subgraph Notification["Notification service"]
        N1[Event consumer<br/>idempotent]
        N2[Alert rules and templates]
        N3[Claimant inbox + manager alerts]
        N4[Delivery stub<br/>email / SMS log]
    end

    classDef p2 stroke-dasharray: 6 4,stroke:#888,color:#666
    class P1,P2,L1,F1,FO,K2,C9 p2
    style Finance stroke-dasharray: 6 4,stroke:#888
    FE --> GW
    GW --> ID1
    GW --> C1 & C2 & C3 & C4 & C6
    GW --> P1 & F1 & L1
    GW --> R1 & R2 & R3 & R4 & R6
    GW --> N3
    C1 & C2 & C3 & C4 --> C5
    C5 --> C6
    C5 --> C7
    C8 --> C7
    C7 --> K1
    K1 --> FO
    FO --> P1
    FO --> L1
    P1 --> FO
    FO --> K2
    K2 --> C9
    K1 --> R1 & R2 & R3 & R4 & R5
    K2 --> R2 & R5 & R6
    K1 --> N1
    K2 --> N1
    N1 --> N2 --> N3
    N2 --> N4
```

Each service owns its database. Services talk only through REST (via the Gateway) or Kafka events. Inside Finance, modules own separate schemas and call each other only through in-process interfaces.

## 2. Data flow for one claim

```mermaid
flowchart LR
    A[Claimant submits claim] --> B[Claims: validate, save,<br/>write trail entry,<br/>write outbox row<br/>one transaction]
    B --> C[(claims.events)]
    C --> D[Reporting<br/>update read models]
    C --> E[Notification<br/>alert claimant]
    C --> FL[Finance Ledger - PHASE 2<br/>post reserve]
    B --> F[Claimant sees timeline<br/>GET /claims/id/timeline]
    G[Officer / Manager action] --> B
    C -->|SettlementConfirmed, PHASE 2| PY[Finance Payment - PHASE 2<br/>instruction, funds check,<br/>bank, retries]
    PY --> FL
    PY --> K2[(finance.events PHASE 2)]
    K2 -->|PaymentCompleted| B
    K2 --> D
    K2 --> E
    D --> H[Manager dashboards<br/>exposure, in settlement,<br/>workload, checker queue]
    E --> I[Claimant inbox<br/>and email/SMS stub]
```

Every action follows the same path: command (or consumed event) → state machine → trail entry + outbox row (one transaction) → Kafka → other services react.

## 3. Claim lifecycle with maker-checker and Settling

```mermaid
stateDiagram-v2
    [*] --> Submitted : claimant submits
    Submitted --> UnderReview : officer picks up (or manager assigns)
    UnderReview --> InfoRequested : officer asks claimant
    InfoRequested --> UnderReview : claimant responds
    UnderReview --> PendingDecisionApproval : officer proposes approve or reject (maker)
    PendingDecisionApproval --> Approved : manager confirms (checker)
    PendingDecisionApproval --> Rejected : manager confirms (checker)
    PendingDecisionApproval --> UnderReview : checker sends back with reason, or maker withdraws
    Approved --> PendingSettlementApproval : officer requests settlement (maker, payee details on file)
    PendingSettlementApproval --> Settling : manager confirms (checker): payable frozen, payment created
    PendingSettlementApproval --> Approved : checker sends back, or maker withdraws
    note right of PendingSettlementApproval : PHASE 2 designed, not built
    Settling --> Settled : PaymentCompleted (System)
    Settling --> Approved : manager cancels payment, reserve restored
    Rejected --> [*]
    Settled --> [*]
```

Rules (BR-31 to BR-39, BR-48, BR-50): the checker is always a Manager and never the maker (user-id comparison, so a dual-role user cannot self-approve), and can only confirm or send back, never edit. A pending proposal cannot be edited; the maker can withdraw it, and reassigning the maker auto-withdraws it. Reserve and exposure do not change until the checker confirms. A claim is Settled only when the bank-side payment succeeded. Failed or Held payments leave the claim in Settling.

### Payment states (Finance / Payment module) - PHASE 2: designed, not built

```mermaid
stateDiagram-v2
    [*] --> Pending : SettlementConfirmed consumed
    Pending --> Held : funds check fails (no retry used)
    Held --> Pending : top-up posted, or manager release
    Pending --> Processing : funds ok, rule 5 posted
    Processing --> Paid : bank ok, rule 6 posted
    Processing --> Failed : 4 attempts failed
    Failed --> Processing : manager retry or payee corrected
    Paid --> Reversed : bank returns funds
    Pending --> Cancelled : manager
    Held --> Cancelled : manager
    Failed --> Cancelled : manager
    Paid --> [*]
    Reversed --> [*]
    Cancelled --> [*]
```

## 4. How each process alerts the claimant (and the manager)

| # | Process step | Trail entry | Claimant sees (Public) | Alert to claimant | Channel | Who is NOT told |
|---|---|---|---|---|---|---|
| 1 | Claim submitted | Claim received | "Claim received, number CLM-…" | **Yes**: confirmation with claim number | In-app + email stub | – |
| 2 | Officer picks up / manager assigns | Assigned | "Your claim is being reviewed" | **Yes** (first pick-up only) | In-app | Reassignment later: no alert, trail only |
| 3 | Officer requests information | Information requested | Question and due date (14 days) | **Yes, action needed** | In-app + email stub | – |
| 4 | Reminder before due date | Reminder sent | Reminder | **Yes** (reminder from scheduler) | In-app + email stub | – |
| 5 | Claimant responds | Information provided | "We received your reply" | **Yes**, acknowledgement | In-app | – |
| 6 | Officer proposes decision (maker) | Decision proposed (Internal) | "Decision under review" | **Optional**, no outcome revealed | In-app | Outcome, amount, reason, names hidden until confirmed |
| 7 | Manager sends decision back, or maker withdraws | Sent back / Withdrawn (Internal) | Nothing new ("Decision under review" stays) | **No** | – | Claimant never told of a send-back |
| 8 | Manager confirms approve | Approved | "Approved" with approved amount | **Yes**: approved, please provide bank details | In-app + email stub | – |
| 9 | Manager confirms reject | Rejected | "Rejected" with reason code and text | **Yes** | In-app + email stub | – |
| 10 | Officer requests settlement (maker) | Settlement requested (Internal) | "Settlement under review" | **Optional** | In-app | – |
| 11 | Payment first dispatched | Payment dispatched | "Payment on the way" | **Yes**, once per payment | In-app | Not repeated on retries |
| 12 | Payment completed, claim Settled | Settled | "Paid" with amount, currency, reference, date | **Yes** | In-app + email stub | – |
| 13 | Payment attempt failed, retries, Held, Failed, cancelled, reversed | Internal entries | Nothing (status stays Settling) | **No** | – | Claimant never sees failure, Held reason or fund data |
| 14 | Refused attempt (invalid transition, wrong user, maker=checker) | Refused attempt (Internal) | Nothing | **No** | – | Never shown to claimant |
| 15 | No reply by the due date | Closed or flagged (BR to be confirmed) | Notice | **Yes** | In-app + email stub | Pending BA confirmation of NO_RESPONSE handling |

Alert rules in one line: the claimant is alerted on **Public** trail entries that change what they must do or what they are owed. **Internal** entries never produce a claimant alert. The "Paid" alert is driven by `ClaimSettled` (Claims), so it is never sent before the claim is really Settled.

### Manager alerts (BR-65)
Payment failed after retries; payment Held for insufficient funds; payment reversed by bank; reconciliation break or DLQ message; ledger out of balance; fund below minimum threshold. One alert per event (idempotent), shown in the manager inbox, never sent to claimant or officer.

## 5. Alert sequence (information request, approval, payment)

```mermaid
sequenceDiagram
    autonumber
    actor OF as Officer
    actor CL as Claimant
    actor MG as Manager
    participant CS as Claims
    participant K as Kafka
    participant FN as Finance (PHASE 2)
    participant NT as Notification
    participant IN as Inbox

    OF->>CS: POST info-request
    CS->>CS: InfoRequested, trail, outbox (one tx)
    CS-)K: InformationRequested
    K-)NT: consume (dedupe by eventId)
    NT->>IN: "Action needed, reply within 14 days"

    CL->>CS: POST info-response
    CS-)K: InformationProvided
    NT->>IN: "We received your reply"

    OF->>CS: POST decision proposal (maker)
    CS-)K: DecisionProposed
    MG->>CS: POST confirm (checker, not the maker)
    CS-)K: ClaimApproved
    NT->>IN: "Approved, please provide your bank details"

    Note over FN,NT: Phase 1 sequence ends at ClaimApproved. Below: PHASE 2
    CL->>FN: PUT payee-details
    FN-)K: PayeeDetailsProvided (masked)
    OF->>CS: POST settlement proposal
    MG->>CS: POST confirm settlement
    CS-)K: SettlementConfirmed
    K-)FN: create payment, funds check, dispatch
    FN-)K: PaymentDispatched
    NT->>IN: "Payment on the way"
    FN-)K: PaymentCompleted
    K-)CS: Settling to Settled
    CS-)K: ClaimSettled
    NT->>IN: "Paid: amount, reference"
```

## 6. Events and who consumes them

Columns Finance Payment and Finance Ledger, and every row after ClaimApproved that involves them, are **Phase 2: designed, not built**. Settlement and Payment* events, FundToppedUp and the finance alerts are Phase 2; all rows above SettlementRequested are Phase 1 (publisher and Reporting/Notification consumers).

Full contract table (payload, producer, topic) is in `docs/architecture/ARCHITECTURE.md` section 7. Summary by consumer:

| Event | Claims | Finance Payment | Finance Ledger | Reporting | Notification |
|---|---|---|---|---|---|
| ClaimSubmitted | – | – | rule 1 | counts, exposure | alert #1 |
| ClaimAssigned / ClaimReassigned | – | – | – | workload | alert #2 first only |
| InformationRequested / Provided / ReminderDue | – | – | – | status counts, time in status | #3, #4, #5 |
| ClaimAssessed | – | – | rule 2 | exposure | – |
| DecisionProposed / DecisionSentBack / ProposalWithdrawn | – | – | – | checker queue | none (optional #6) |
| ClaimApproved | – | stores claim_ref | – | exposure | #8 |
| ClaimRejected | – | – | rule 3 | exposure released | #9 |
| SettlementRequested / SettlementSentBack | – | – | – | checker queue | none (optional #10) |
| SettlementConfirmed | – | creates instruction | rule 4 | in-settlement | optional |
| ClaimSettled | – | – | – | exposure to paid, settlement time | #12 |
| SettlementCancelled | – | – | – | exposure up | none |
| PayeeDetailsProvided | payee flag, trail | – | – | – | – |
| PaymentRequested | trail | – | – | payment pipeline | – |
| PaymentHeld | trail | – | – | exceptions count | manager alert |
| PaymentDispatched | trail | – | (rule 5, in process) | – | #11 |
| PaymentAttemptFailed | trail | – | (rule 7, in process) | first-attempt rate | none |
| PaymentFailed | trail | – | – | exceptions queue | manager alert |
| PaymentCompleted | Settling to Settled | – | (rule 6, in process) | success rate | none (waits for ClaimSettled) |
| PaymentCancelled | Settling to Approved, reserve restored | – | (rule 9, in process) | exposure | none |
| PaymentReversed | trail only | – | (rule 8, in process) | – | manager alert |
| FundToppedUp | – | releases Held (in process) | (rule 10, in process) | optional | – |
| FundBelowMinimum / LedgerOutOfBalance / ReconciliationBreak | – | – | – | – | manager alert |
| RefusedAttempt | not published | | | | |

## 7. Modules and where they are built

Phase column: numbers 0-3 in older rows mean Phase 1 build steps (ARCHITECTURE section 11); "2" = Phase 2, not built. Settlement maker-checker is Phase 2; the maker-checker row below is Phase 1 for the **decision** only.

| Module | Service | Priority | Phase |
|---|---|---|---|
| Claim intake, state machine (9 states), progress trail, outbox | Claims | MUST | 1 |
| Assignment (pick-up, manager reassign) | Claims | MUST | 1 |
| Assessment (notes, assessed amount, info requests) | Claims | MUST | 1 |
| Maker-checker approvals (decision, settlement) | Claims | MUST | 1 |
| Finance event consumer (Settled, trail) | Claims | Phase 2, designed not built | 2 |
| Reminder scheduler for info requests | Claims | COULD, document | – |
| Read models: outstanding, exposure, workload | Reporting | MUST | 2 |
| Checker queue, time in status | Reporting | SHOULD | 2 |
| Payment stats and exceptions count | Reporting | Phase 2 | 2 |
| Alert rules, claimant inbox | Notification | MUST | 2 |
| Templates, stub delivery, manager alerts | Notification | SHOULD | 2/4 |
| Users, roles, dev JWT | Identity | MUST (thin) | 0 |
| Routing, JWT validation, CORS | Gateway | MUST (thin) | 0 |
| **Payment**: payee details, instruction, simulated bank, idempotency, 3 retries, Failed state | Finance | Phase 2, designed not built | 2 (F1) |
| **Ledger**: accounts, posting rules 1 to 7 and 10, idempotent, trial balance | Finance | Phase 2, designed not built | 2 (F1) |
| **Pool Fund**: seeded openings; balance = account 1000 | Finance | Phase 2, designed not built | 2 (F1) |
| Funds check and Held, fund view, top-up, auto-release, manager retry and queue | Finance | Phase 2, designed not built | 2 (F2) |
| Cancel and compensation, reversal, payee correction, reconciliation, threshold, coverage ratio | Finance | Phase 2, designed not built | 2 (F3) |

### Finance module contracts (in-process, no cross-schema SQL)

| Interface | Provided by | Used by | Notes |
|---|---|---|---|
| `IPostingService.Post(rule, sourceEventId, claimId, paymentId, amounts)` | Ledger | Payment, Fund, event consumer | Idempotent on `sourceEventId`; refuses unbalanced or mixed currency |
| `IBalanceQuery.Balance(account, currency, claimId?, asOf?)` | Ledger | Payment (funds check), Fund, REST views | Computed from lines |
| `IFundsGate.CheckAndReserve(currency, amount)` | Pool Fund | Payment | Holds the per-currency advisory lock for the duration of the transaction |
| `IBankGateway.Pay(idempotencyKey, payee, amount)` | simulated bank | Payment | Mock; scripted outcomes |

## 8. Open points
- Whether alerts #6 and #10 ("under review") are sent: BA/PO to confirm.
- Rule for a claimant who never replies (alert #15).
- Email/SMS are logged only; real delivery is documented as "not built".
- BR-42 allows the manager correction screen to show full bank details; the security design shows masked data on every read and makes correction write-only. BA/human to confirm.
- BR-44 "same transaction" is satisfied across services by outbox + unique-per-claim instruction (ARCHITECTURE section 8). BA to accept.
