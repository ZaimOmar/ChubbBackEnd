# Domain glossary

Scope: the claim progress trail first; everything else minimal. Amounts are in the claim's currency unless stated.

| Term | Definition |
|---|---|
| Claim | A request by a policyholder for the insurer to pay for a loss under a policy. Motor or Property. Has one lifecycle status at a time. |
| First notice of loss (FNOL) | The first report of the incident. In this system: claim submission, creating status `Submitted` and the first trail entry. |
| Claimant | The policyholder (or their nominee) who reports and tracks a claim. Sees only their own claims. |
| Officer (claims officer) | Staff member who picks up, assesses and decides claims. Owns a claim while assigned. |
| Manager (claims manager) | Staff member who monitors workload, outstanding claims and exposure; can reassign claims; is the only role that can act as checker on decisions and settlements (BR-31). Does not propose decisions. |
| Policy | The insurance contract against which a claim is made: policy number, market, status, coverage limit. Seeded, fictitious data only. |
| Coverage limit | Maximum the insurer will pay under the policy, treated as an aggregate per policy period (assumption). Only used as a ceiling on the approved amount, via the remaining coverage limit (BR-19, BR-72). |
| Excess / deductible | Part of the loss the policyholder bears; deducted from the payout. Amounts are NET of excess: payable = assessed amount minus the policy excess, floored at 0 and never above the coverage limit (BR-19). Excess is a seeded policy attribute. |
| Reserve | The insurer's current estimate of what it still has to pay on an open claim (outstanding reserve). Starts at the estimated amount, moves to the net payable amount on assessment. Unchanged while a proposal is pending (BR-35). |
| Paid | Money actually paid out on the claim. Here: the payable, recorded when PaymentCompleted is received and the claim becomes Settled (BR-48). Reduced if the bank returns the payment (BR-52). |
| Incurred | Paid + outstanding reserve. Open claim: incurred = reserve. Settled: incurred = paid. Rejected: 0. |
| Liability exposure | The one definition we use: the sum of the outstanding reserve on open claims (not Rejected, not Settled), reported by market, claim type and status, normalised to USD for totals. Paid is excluded from exposure; incurred = exposure + paid. |
| Assessed amount | The officer's evaluated value of the loss after review (gross, before excess). The net payable derived from it becomes the reserve. |
| Approved amount | The net payable amount (assessed minus excess, capped at coverage limit) proposed by the officer and fixed when the checker confirms approval. |
| Settlement | Paying an Approved claim: requested by the officer, confirmed by a checker (claim moves to Settling), complete only when the bank-side payment succeeds (claim Settled). |
| Rejection reason code | A code from a fixed list (BR-17) plus free text, mandatory when a claim is rejected. |
| Information request | A question from the officer to the claimant that moves the claim to `InfoRequested` until answered. |
| Claim progress trail | The append-only, ordered record of everything that happened to a claim: who, when, what, why. Read through `GET /claims/{id}/timeline`. |
| Public entry | A trail entry the claimant may see (status milestones, questions asked of them, decisions and their reasons). |
| Internal entry | A trail entry only staff see (assessment notes, reassignments, reserve changes, justifications, audit of refused actions). |
| Market | One of the six operating countries: SG, HK, MY, TH, ID, AU. Fixes the claim's currency. |
| ISO currency | ISO 4217 three-letter code of the amount: SGD, HKD, MYR, THB, IDR, AUD (USD for reporting only). |
| Maker | The assigned officer who proposes a money-affecting step: the approve/reject decision, or a settlement request. |
| Checker | A user holding the Manager role who confirms or sends back a maker's proposal. Never the maker of that same proposal, even if the user also holds the Officer role. |
| Maker-checker (four-eyes) | Control under which a money-affecting step needs two different people: one proposes, another confirms. Scope here: decision and settlement. |
| Proposal / pending approval | A maker's submitted decision or settlement request awaiting the checker. The claim sits in `PendingDecisionApproval` or `PendingSettlementApproval`; nothing financial has changed yet. |
| Send-back | The checker's refusal of a proposal with a mandatory reason; the claim returns to its prior working status (`UnderReview` or `Approved`) for the maker to rework. Not a rejection of the claim. |
| Withdrawal | The maker cancelling their own pending proposal; same return as a send-back, reason required. |
| Pending checker queue | Claims in a Pending* status awaiting a checker, oldest first; the manager's work list. |
| Checker turnaround time | Time from the proposal entry to the checker's confirm or send-back entry (BR-40). |
| Settlement time | Time from the confirmed Approved entry to Settled (payment completed). A separate metric, excluded from handling time (BR-29, BR-51). |
| Settling | Claim state between PendingSettlementApproval and Settled: the payment has been dispatched or is being retried. Stays Settling if the payment fails after retries, until the manager acts (BR-01, BR-47). |
| Payable | The frozen amount owed to the payee: assessed amount minus excess, floored at 0, capped at the coverage limit, computed once at approval (BR-41). |
| Payee | The person or entity receiving the payout, with bank details supplied by the claimant after approval (BR-42). Has a type: Policyholder, Nominee, ThirdPartyProvider or Assignee; non-policyholder payees need an officer justification (BR-43). Account numbers shown as last 4 digits only. |
| Payment instruction | The record created at the manager's settlement confirmation: payee, bank details, amount, currency, idempotency key, status (BR-44). |
| Idempotency key | Unique key per payment attempt so retries or duplicate messages can never pay or post twice (BR-45, BR-59). |
| Reversal | Undoing a posted entry by a new opposite entry (BR-57), or a bank returning a Paid payment: payment Reversed, payable reinstated, claim stays Settled (BR-52). |
| Permanent / transient failure | Gateway failure classes: permanent (e.g. invalid account) is never retried automatically; transient is retried on the configured schedule (BR-47). |
| Status check | Query to the gateway by idempotency key after a timeout, to learn the real outcome before any re-send (BR-45). |
| Outcome unknown | A Processing payment whose result could not be confirmed; flagged to the manager, not cancellable or retryable (BR-45). |
| Reconciliation | Matching payments to the bank statement and the ledger and listing breaks (BR-53, BR-61). |
| General ledger | The append-only double-entry record of reserve, payable and cash movements (BR-54 to BR-63). |
| Chart of accounts | The fixed list of ledger accounts (code, name, type, normal side), per currency (BR-54). |
| Journal entry | One balanced set of debit and credit lines for a single business event, linked to claim, payment, event id and trail entry (BR-56, BR-58). |
| Debit | Ledger line on the left side: increases assets and expenses, decreases liabilities. |
| Credit | Ledger line on the right side: increases liabilities, decreases assets and expenses. |
| Clearing account | Account 2200 Payment in transit, a liability: holds money instructed but not yet confirmed by the bank (debt not yet discharged); nets to zero when completed or failed (BR-54). |
| Trial balance | Per-currency list of all account balances whose total debits must equal total credits (BR-60). |
| Pool fund | The claims disbursement bank account from which claims are paid, funded by finance (here the manager top-up); one per market and currency; its balance is ledger account 1000, with no separate record (BR-66). Not a claimant wallet. |
| Held - insufficient funds | Payment state when the funds check fails before dispatch; released automatically or by the manager once funds exist (BR-67). |
| Top-up (contribution) | Manager-only addition to a pool fund, posted Dr 1000 / Cr 3000 (BR-68, BR-69). |
| Minimum-balance threshold | Fund level (default 20% of opening balance, configurable, assumption) below which the manager is warned (BR-70). |
| Coverage ratio | Manager-only. Payables cover = fund balance / (payables + in transit), red below 1.0; reserve cover = fund balance / (reserve + payables + in transit), informational (BR-71). |
| Remaining coverage limit | What a claimant sees: policy coverage limit minus paid-to-date (Paid minus Reversed), read-only and derived; also the cap for new payables (BR-19, BR-72). |
