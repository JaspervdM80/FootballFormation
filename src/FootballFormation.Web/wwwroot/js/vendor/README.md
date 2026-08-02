# Vendored third-party scripts

Checked in rather than fetched from a CDN at runtime. The app is an installable PWA used at the
side of a pitch, so anything it needs has to be served from our own origin — and a CDN request on
a production page is a third party we would be trusting with our users on every export.

| File | Version | License | Source |
| --- | --- | --- | --- |
| `html2canvas.min.js` | 1.4.1 | MIT | https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js |

`html2canvas.min.js` — SHA-256 `e87e550794322e574a1fda0c1549a3c70dae5a93d9113417a429016838eab8cb`

Used by `../screenshot.js` to export the formation overview as a PNG. Loaded lazily, so only
people who actually use the share button pay for it.

To update: download the new minified build, replace the file, record the version and hash above,
and check the export still works on `/games/{id}/overview`.
