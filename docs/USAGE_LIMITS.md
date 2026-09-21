# Usage windows and reset credits

Verified against official OpenAI documentation on 2026-09-21.

- [Pricing](https://learn.chatgpt.com/docs/pricing) describes five-hour allowance estimates for Plus, Pro, and standard Business, with weekly limits potentially also applying. Legacy Enterprise/Edu follows Plus for most features; flexible Enterprise/Edu uses credits without fixed rate limits. Free and Go include Codex, but this page does not establish their exact window combination. Actual account windows take precedence over plan-name assumptions, including weekly-only high-tier accounts.
- Plus/Pro can buy additional credits. These purchased usage credits are distinct from earned rate-limit reset credits. Referral rewards are promotion-dependent; the documented June promotion is not a current entitlement guarantee.
- [App Server](https://learn.chatgpt.com/docs/app-server) exposes each window's duration and next reset independently, plus one authoritative available reset-credit count. It documents the generic `codexRateLimits` reset type, not separate five-hour and weekly credit balances. The service decides reset eligibility; the consume response does not establish which windows changed. Re-read limits afterward. Wingman only displays the balance and never redeems it.

## Interface decisions

Render returned short-window usage before weekly usage. Hide absent windows, never fabricate limits from a subscription tier, and use each window's own percentage, elapsed time, warning state and reset timestamp. Hover or focus gives that dial's own enlarged preview and details. Both popups show the same reset count, labelled Shared balance when both windows exist. Missing reset data remains unavailable, not zero.

The composer ring must have a uniform radial thickness. It uses the original filled circle strokes (radius 7, stroke width 2 SVG units, unused-capacity opacity 0.4) from before the outline feature; the popup retains the owner's accepted 2px outline. The outline treatment applies only to the enlarged popup.

Account-specific limitation: a lower-tier account is not available for authenticated live testing here. Dual-window behavior is covered by the native-cache fixture and rendered browser simulation; owner-account live acceptance covers the weekly-only case.

Miniature acceptance must include a tightly cropped 1:1 screenshot beside the native context dial. Uniform mathematical bounds alone are insufficient: the unused-capacity segment must remain visibly distinct from the darker unused-time segment. The previous 0.4px clipped outline failed that check.

Owner correction: preserve the pre-outline miniature exactly; no endpoint tick, brighter grey, clipping, or outlined miniature. Only the large popup uses the hollow outline.
