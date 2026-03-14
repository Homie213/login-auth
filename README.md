# login-auth

Single-repo customer and admin portal for GitHub Pages.

## Repo layout

- `index.html`: landing page
- `customer.html`: customer login and access request page
- `admin.html`: admin login, customer management, password reset, and license controls
- `database/herax_licenses.json`: shared JSON data
- `exe/`: place shipped desktop packages here
- `website/index.html`: backward-compatible redirect to `../admin.html`

## GitHub Pages

Enable Pages from:

- Branch: `main`
- Folder: `/root`

## Notes

- The customer page reads the published JSON file.
- The admin page caches edits in browser local storage automatically.
- Use the GitHub token fields in `admin.html` when you want to write changes back to `database/herax_licenses.json`.
- Default seed admin login: `admin / change-me-now`.
