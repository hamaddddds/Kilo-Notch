# Kilo Notch working rules

- Never build, publish, package, or restore the desktop app or installer on this computer. Windows compilation, packaging, and executable tests run only in GitHub Actions. Local QC must remain source-only; a static website preview is allowed.
- Run source QC and review the diff before every commit. For authorized changes, commit and push automatically; do not wait for a separate commit request. Watch the resulting Actions run and fix failures. Only publish release assets after all required CI checks pass. Passing QC is evidence, not a promise that no bugs exist.
- All visible application, installer, and uninstaller UI must use Kilo's own templates or custom rendering. No stock Windows dialogs, file pickers, message boxes, installer wizards, or unstyled controls. Native OS services and Windows rendering APIs are allowed underneath the custom UI.
- Keep Windows installation per-user, preserve user data on uninstall, validate installation paths, and never terminate unrelated processes. Test upgrade rollback and uninstall boundaries in CI.
- Keep the existing GitHub Pages address and repository. Do not upload local builds or generated QA artifacts to Git.

Local quality check: `pwsh -File scripts/qc.ps1` (no compilation).
