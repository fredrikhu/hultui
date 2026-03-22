---
name: Deferred UI fixes
description: Known visual bugs in TextInputElement that need fixing later
type: project
---

1. **Triple-click select-all doesn't work correctly** in TextInputElement. The OnClick handler for ClickCount >= 3 fires but something about the interaction is wrong.
2. **Caret extends too much vertically** in TextInputElement. After removing the Y insets, the caret fills the full content box height which is slightly too tall visually.

**Why:** User wants to focus on other priorities first.
**How to apply:** Fix these when returning to TextInputElement polish work.
