# Repository workflow

## Two-repository policy

This workspace intentionally uses two GitHub repositories:

| Remote | Repository | Purpose |
| --- | --- | --- |
| `origin` | `https://github.com/buptrain/emuxr-private.git` | Private development upstream. All normal development, feature branches, and pull requests go here. |
| `public` | `https://github.com/buptrain/emuxr.git` | Future public mirror. Only reviewed, release-safe `master` history is pushed here. |

`master` tracks `origin/master`. A plain `git push` therefore targets the private upstream.

## Daily development

Create and publish feature branches only to the private upstream:

```powershell
git checkout -b codex/feature-name
git push -u origin codex/feature-name
```

Do not push feature branches to `public`.

## Publishing the public mirror

Before any public push, complete the checks in [PUBLIC_RELEASE_GATE.md](PUBLIC_RELEASE_GATE.md), including the clean-clone audit and licensing review. Then push only the reviewed `master` branch:

```powershell
git checkout master
git push public master
```

Never use `git push --mirror` for either repository. It can expose private branches, recovery refs, or other unintended refs.

## Content boundary

Keep proprietary work, commercial integrations, credentials, paid assets, ROMs, BIOS files, save states, and locally built core binaries out of public history. Instrumented-core source and its release artifacts are governed separately by the core repository and its license obligations.
