# Project skills

Agent skills scoped to this repository. Cursor discovers them automatically; see
[the skills documentation](https://cursor.com/docs/agent/skills) for how they are loaded.

## Vendored skills

These three were selected from [sickn33/agentic-awesome-skills](https://github.com/sickn33/agentic-awesome-skills),
a catalog of roughly 1,900 community skills. Only skills that carry knowledge specific
to this project's stack are vendored here, because every installed skill spends context
budget on every request.

| Skill | Upstream author | Upstream license | Why it is here |
|---|---|---|---|
| `windows-shell-reliability` | sickn33/agentic-awesome-skills | CC BY 4.0 | Build, test and publish all run through PowerShell on Windows |
| `seo-technical` | [AgriciDaniel/claude-seo](https://github.com/AgriciDaniel/claude-seo) | CC BY 4.0 | The landing page in `website/` has no robots.txt, sitemap or canonical URL |
| `fixing-accessibility` | [ibelick/ui-skills](https://github.com/ibelick/ui-skills) | MIT | The landing page is hand-written HTML and CSS, so ARIA and focus states are maintained by hand |

Catalog code is MIT licensed and catalog content is CC BY 4.0. Frontmatter was reduced to
the fields Cursor reads (`name`, `description`, `disable-model-invocation`); the upstream
`risk`, `allowed-tools`, `argument-hint` and `user-invokable` keys belong to other agent
runtimes and are ignored here. Bodies are otherwise unchanged apart from removing
slash-command syntax that does not exist in Cursor.

## Updating

Skills are vendored deliberately rather than pulled at runtime, so an upstream edit cannot
silently change how the agent behaves in this repository. To refresh one, re-fetch
`skills/<name>/SKILL.md` from the catalog, re-read it, and re-apply the frontmatter
reduction above.
