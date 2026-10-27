# ADR-007: General ledger design and consistency with Claims

Status: Accepted. Owner: architect.

**Phasing: the design stands; Finance (Payment, Ledger, Pool Fund) is Phase 2, designed but not built, under the 5-hour cap. Phase 1 builds Gateway, Identity, Claims, Reporting, Notification (five services). Claims keeps extension points so Phase 2 needs no rework (ARCHITECTURE section 0).**

## Context
The business wants a double-entry record of reserve, payable and cash (BR-54 to BR-63), with the pool fund balance equal to account 1000 (BR-66). Claims already keeps a per-claim reserve used by exposure. Two stores of "reserve" invite disagreement unless ownership is explicit.

## Decision
- **Tables (schema `ledger`):** `account` (code, currency, type, normal_side; seeded), `journal_entry` (id, source_event_id UNIQUE, claim_id, payment_id, trail_entry_id, event_type, currency, reverses_entry_id, posted_at), `journal_line` (entry_id, account_code, side, amount > 0). Balances are computed (SUM), with an as-of filter; no stored balance.
- **Append-only:** the application DB role has INSERT and SELECT only; a trigger raises on UPDATE/DELETE. Corrections are reversing entries.
- **Balanced:** the posting service builds an entry from a rule (BR-55 as code, one function per rule), checks total debit = total credit and a single currency, and refuses the whole entry otherwise.
- **Idempotent:** `source_event_id` is unique; a second delivery is a no-op acknowledgement. Payment attempts are separate events (`paymentId:attemptNo:sent`).
- **Postings fed by events only:** Claims events (`ClaimSubmitted`, `ClaimAssessed`, `ClaimRejected`, `SettlementConfirmed`) via the consumer; payment steps by the Payment module in the same transaction as the payment state change. Nobody posts manually.
- **Ownership:** **Claims owns the operational reserve** (it enforces the rules and exposure uses it). **The ledger is the financial record**, derived from Claims events; it never edits Claims and Claims never reads it. A daily reconciliation (Finance calls an internal Claims endpoint, compares reserve per claim with account 2000) reports breaks to the manager; correction is by reversing entry or by fixing the event flow, never by overwrite.
- **Pool fund:** `fund` schema holds only config (opening balance, threshold) and top-up audit; balance is read from the ledger.

## Alternatives considered
- **Ledger as the only reserve store (Claims reads it):** removes duplication but couples every claim command to Finance. Rejected.
- **Stored balances per account:** faster reads, but a second truth to keep consistent. Rejected; volume is tiny.
- **Event sourcing for the ledger:** the ledger already is an append-only log; a framework adds nothing.
- **Off-the-shelf ledger (TigerBeetle, Formance):** new infrastructure. Rejected; noted for production.

## Consequences
- Dashboards (Reporting) and ledger can differ by in-flight events; the reconciliation shows when they do.
- Multi-currency totals are never summed in the ledger.
- Period close, manual journals, FX revaluation: documented, not built.
