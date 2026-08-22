# Web fonts

Aurum self-hosts two variable fonts so the website does not depend on a third-party font CDN:

- **Unbounded** — display headings and the Aurum wordmark. Source: [google/fonts](https://github.com/google/fonts/tree/main/ofl/unbounded).
- **Onest** — body copy, navigation, labels, and controls. Source: [google/fonts](https://github.com/google/fonts/tree/main/ofl/onest).

Both families are distributed under the SIL Open Font License 1.1. The corresponding license texts are stored in this directory.

The WOFF2 files are subsets (Latin + Cyrillic) of the upstream variable fonts. Full TTF copies used by the WPF app live in `src/Aurum.App/Fonts/` and are not published with the site.
