# ship-changes - Part 2

## Split Content

### Step 9: Push to Remote

The repo pre-configures `push.autoSetupRemote=true` and `push.default=simple`
locally during `npm run hooks:install` (and the devcontainer post-create), so
`git push` on a new branch sets upstream automatically — **do not** pass
`--set-upstream` / `-u` flags and never run wrapper scripts around `git push`.

Rules when pushing:

| Rule                           | Why                                                                                                                                                             |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Never redirect output**      | `git push 2> pre-push.txt` creates gitignored pollution that confuses agents                                                                                    |
| **Never use `--no-verify`**    | Bypassing the pre-push hook skips the last-resort local safety gate                                                                                             |
| **Let stderr stream normally** | Errors must be visible in the live output, not hidden in files                                                                                                  |
| **Never pass a credential**    | github.com resolves through the cached-only helper; a push that reports no credential means the cache is empty, not that the push needs one on the command line |

If `fatal: The current branch <x> has no upstream branch` appears, the local
config is missing. Remediation: `npm run agent:preflight:fix` (restores
`push.autoSetupRemote=true` and removes any stray
`<hook-name>.{txt,log,out,err,tmp}` artifact files). Do **not** work around it
with `git push -u origin <branch>`
— fix the config once so every future push is clean.

If a push is rejected for non-fast-forward reasons, prefer
`git pull --rebase`. Stash any unrelated local changes manually first; never
silently clobber history with `--force` without explicit user consent.

#### When the HTTPS push hangs: the forwarded SSH agent

`scripts/github-token.sh` exiting 3 does **not** mean the container cannot push.
It means the token cache is empty. There is a second path, and it needs no
token, no cache and no dialog: the Dev Containers extension forwards the
**host's SSH agent** into the container.

```bash
ssh-add -l                                                   # keys the host forwarded
ssh -o StrictHostKeyChecking=accept-new -T git@github.com    # "Hi <user>!" = authenticated
git push git@github.com:<owner>/<repo>.git HEAD:<branch>
```

Check this **before** concluding a push is blocked. Three sessions handed a
finished branch back unpushed on the strength of exit 3 alone; this session's
push succeeded on the first SSH attempt after four HTTPS attempts hung.

**Why the HTTPS push hangs rather than failing.** `/etc/gitconfig` installs the
Dev Containers credential helper for every URL, so it is tried before
`GIT_ASKPASS` and before the refuse script ever runs. `GIT_TRACE=1` shows the
push reaching `git-credential-helper get` and stopping there — that block is a
dialog on the owner's desktop waiting to be answered, one per attempt. Do not
retry it.

SSH covers git only. The **API** — a pull request body, an issue comment, a
label — still needs a token, so `github-token.sh` remains the path for those.

### Step 9b: Open the pull request yourself

A push alone runs almost nothing. The Unity matrix, the lint workflows and the
review bots are **`pull_request`-triggered**, so a branch sitting on the remote
with no pull request has proven only that `Spelling Check` passes. Opening it is
part of shipping, not a hand-back.

#### Title and body: short and plain

Someone reads the pull request to decide if it affects them. Write for that
person. Say what changed and why. Say nothing else.

**Title.** One line, 50 characters or fewer, imperative mood. Name the effect
the user sees, not the mechanism. Do not join two changes with "and" -- title
the one that matters most and let the body carry the rest.

**Body.** Copy this template. Add nothing to it.

```markdown
DISCLOSURE: LLM-GENERATED TEXT

**Why:** <the problem, in one sentence>

**What:**

- <one change, one line>
- <two to five bullets>

Fixes #123
```

The disclosure is mandatory for agent-written text and is not part of the sentence or bullet
limits. Follow the outside-contributor gate in
[github-operations](../skills/github-operations.md#authorship-and-outside-contributors) before creating,
editing, reviewing, or merging a pull request from another person.

| Limit            | Value         |
| ---------------- | ------------- |
| Title            | 50 characters |
| `**Why:**`       | 1 sentence    |
| `**What:**`      | 2 to 5 lines  |
| Words per bullet | 12            |

Write short sentences. Use the active voice. Use common words. Give a number
only when the number is the point (`60% smaller`, `2x faster`).

Write in Simplified Technical English. One idea per sentence -- about 15
words, never more than 25. Common words over jargon (`change`, not
`refactor/adjust/modify`). Active voice: someone did something. No filler --
cut `basically`, `additionally`, `in order to`, `it should be noted that`,
and every sentence that only warms up the next one. Answer **why**, **how**,
and **what**, in that order, then stop.

**Never put these in a pull request:** root causes, measurements, run IDs,
session numbers, CI results, a list of what you validated, byte traces, the
diff, or the file list. They go in the commit body or the linked issue. Local
progress notes stay ignored and must never be staged.

Count the title before you send it. Nine of the twelve titles before this rule
existed were over 60 characters, so count rather than judge:

```bash
printf '%s' "$TITLE" | wc -c # 50 or fewer
```

The same branch, written both ways:

| Verdict | Title                                                                                                                           |
| ------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Avoid   | `Stop writing a derived hash the reader recomputes, keep tag 4 for z so legacy payloads still parse, and widen the parity gate` |
| Prefer  | `Shrink grid payloads by 60%`                                                                                                   |

| Verdict | Body                                                                                                                 |
| ------- | -------------------------------------------------------------------------------------------------------------------- |
| Avoid   | Six paragraphs of measurement, a byte-level trace, the golden vectors that changed, and the local gates that passed. |
| Prefer  | `**Why:** every grid cell carried a hash the reader threw away.` then three bullets and `Fixes #519`.                |

Use the configured GitHub MCP server first to find or create the pull request, then verify the
returned number, URL, head, and base. Follow [github-operations](../skills/github-operations.md) for every
other remote GitHub read or mutation in this workflow.

The API is reachable from inside the devcontainer. The example below is a fallback only when the
current GitHub MCP toolset does not expose pull-request creation. `scripts/github-token.sh` is the
only supported source of the fallback credential and it **never prompts**: it reads a non-empty
`$GITHUB_TOKEN` / `$GH_TOKEN` or a 0600 cache, and exits 3 with the command that fixes it when there
is neither. Never run the credential helper directly — Dev Containers answers by raising a dialog
on the owner's desktop on every invocation, and the one deliberate prompt is a human running
`npm run github:token:bootstrap`. See the GitHub access notes in [context](../context.md).

```bash
GH_TOKEN="$(bash scripts/github-token.sh)" # exits 3, loudly, when there is none
export GH_TOKEN
python3 - <<'PY'
import json, os, pathlib, urllib.request
body = pathlib.Path("<body file>").read_text()
if not body.startswith("DISCLOSURE: LLM-GENERATED TEXT\n\n"):
    raise SystemExit("Pull request body is missing the first-line LLM disclosure")
payload = {
    "title": "<summary line>",
    "head": "<branch>",
    "base": "main",
    "body": body,
}
req = urllib.request.Request(
    "https://api.github.com/repos/Ambiguous-Interactive/unity-helpers/pulls",
    data=json.dumps(payload).encode(),
    headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"],
             "Accept": "application/vnd.github+json",
             "Content-Type": "application/json",
             "User-Agent": "unity-helpers-agent"},
    method="POST")
with urllib.request.urlopen(req, timeout=60) as r:
    print(json.load(r)["html_url"])
PY
```

Write the body to a file first rather than inlining it — a heredoc carrying
backticks and `$` through two layers of quoting is how a body arrives mangled.
The same call with `/issues` instead of `/pulls`, and `{"title", "body"}`, files
a follow-up issue. Apply the same first-line validation to that issue body and to every body edit.
