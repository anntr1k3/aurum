# Aurum website

A dependency-free static page. To preview it, serve this directory:

```powershell
python -m http.server 4173
```

Then open `http://localhost:4173/`.

## Deployment

The canonical URL, the Open Graph tags, `robots.txt` and `sitemap.xml` all assume
`https://anntr1k3.github.io/Aurum/`, which is what GitHub Pages serves when it is
pointed at the `website/` folder on `main`. If the site moves to a custom domain,
update the absolute URLs in the `<head>` of `index.html` plus both of those files.

Two things to know about that setup:

- **`robots.txt` is ignored at a subpath.** Crawlers only read it from the domain
  root, so `anntr1k3.github.io/Aurum/robots.txt` has no effect. The file is here so
  it becomes correct the moment a custom domain is used; until then, submit
  `sitemap.xml` directly in Search Console instead.
- **Links to project documentation are absolute.** They point at `blob/main` on
  GitHub rather than at relative paths, because relative paths only resolved when
  the page was opened from a local checkout.

## Known gaps

- `og:image` reuses the 256×256 application icon, so link previews render as a small
  square card (`twitter:card` is set to `summary` to match). A purpose-made 1200×630
  image would allow `summary_large_image`.
- `fonts/Unbounded-Variable.ttf` is 760 KB and is only used for headings and the
  wordmark. Converting to WOFF2 and subsetting to Cyrillic and Latin would remove
  most of that weight. The same two font files are also duplicated in
  `src/Aurum.App/Fonts/`, so each one is stored twice in the repository.

## Editing rules

The panel in the hero is an **interface preview**, not a working control. The page
must never claim to inspect the visitor's machine: an earlier version had a
"Проверить систему" button that only ran a timer and then displayed "Система
проверена", which contradicts the product's own claim that nothing is offered unless
it can be explained, verified and reversed. Keep the preview visibly inert.
