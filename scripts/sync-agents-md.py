"""Regenerate AGENTS.md (Codex guidance) from CLAUDE.md (the source of truth).

CLAUDE.md and AGENTS.md are near-identical siblings: same rules, same commands,
with the agent name swapped. Per project convention the Future Feature Enhancements
section lives in CLAUDE.md only; AGENTS.md carries a pointer instead. Run this
script after every CLAUDE.md edit:

    python scripts/sync-agents-md.py
"""

from __future__ import annotations

import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CLAUDE_MD = REPO_ROOT / "CLAUDE.md"
AGENTS_MD = REPO_ROOT / "AGENTS.md"

FUTURE_SECTION_HEADING = "## Future Feature Enhancements"
FUTURE_POINTER = (
    "## Future Feature Enhancements\n"
    "\n"
    "Planned follow-on projects (RMC.TotalRisk.UI WPF layer, the RMC-TotalRisk desktop "
    "app shell, RMC.TotalRisk.Api REST API, FDA importer) are documented in CLAUDE.md "
    "only — see that file for scope and design criteria.\n"
)

# Lines describing the two guidance files in the Repository Layout tree are swapped
# rather than word-replaced, so each file correctly identifies itself.
LAYOUT_CLAUDE_LINE = (
    "├── CLAUDE.md                       ← this file (Claude Code guidance; source of truth)"
)
LAYOUT_AGENTS_LINE = (
    "├── AGENTS.md                       ← Codex sibling — GENERATED from CLAUDE.md, do not edit by hand"
)
LAYOUT_CLAUDE_LINE_IN_AGENTS = (
    "├── CLAUDE.md                       ← Claude Code sibling (source of truth — edit it, then regenerate this file)"
)
LAYOUT_AGENTS_LINE_IN_AGENTS = (
    "├── AGENTS.md                       ← this file (Codex guidance; generated from CLAUDE.md)"
)

GENERATED_BANNER = (
    "<!-- GENERATED FILE — do not edit. AGENTS.md is produced from CLAUDE.md by "
    "scripts/sync-agents-md.py. Edit CLAUDE.md and rerun the script. -->\n\n"
)


def remove_future_section(text: str) -> str:
    """Replace the Future Feature Enhancements section body with a pointer to CLAUDE.md."""
    start = text.index(FUTURE_SECTION_HEADING)
    next_heading = text.index("\n## ", start + len(FUTURE_SECTION_HEADING))
    return text[:start] + FUTURE_POINTER + text[next_heading + 1 :]


def main() -> None:
    text = CLAUDE_MD.read_text(encoding="utf-8")

    text = remove_future_section(text)
    text = text.replace(LAYOUT_CLAUDE_LINE, LAYOUT_CLAUDE_LINE_IN_AGENTS)
    text = text.replace(LAYOUT_AGENTS_LINE, LAYOUT_AGENTS_LINE_IN_AGENTS)

    # Swap the agent name. "CLAUDE.md" (all caps) is untouched by the word-boundary
    # match, so file references survive.
    text = re.sub(r"\bClaude Code\b", "Codex", text)
    text = re.sub(r"\bClaude\b", "Codex", text)

    AGENTS_MD.write_text(GENERATED_BANNER + text, encoding="utf-8", newline="\n")
    print(f"Wrote {AGENTS_MD} ({len(text.splitlines())} lines from CLAUDE.md).")


if __name__ == "__main__":
    main()
