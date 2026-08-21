# Aurum website

The website is a dependency-free static build. Serve the repository root so the
links to project documentation continue to work:

```powershell
python -m http.server 4173
```

Then open `http://localhost:4173/website/`.

Before publishing, replace `data-repository-url="../"` on the `<html>` element
in `index.html` with the public repository URL. The source-code buttons all use
that single value.
