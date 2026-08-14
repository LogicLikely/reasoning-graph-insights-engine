# Private frontend packages

This directory contains versioned package artifacts that are intentionally
consumed without a public package registry.

## GraphMap

- Artifact: `logiclikely-graphmap-0.2.0-next.3.tgz`
- Package version: `0.2.0-next.3`
- SHA-256: `4c80da07ee69deadeb7c620ee38f62a324667be626510907788ceaecdc4fb26a`

The dependency in `../package.json` must use the exact matching `file:vendor/...`
path. The archive and `package-lock.json` must be committed together so a clean
`npm ci` never needs access to the GraphMap repository.

For an updated GraphMap build:

1. Give GraphMap a new package version and create its tarball with `npm pack`.
2. Copy the new, versioned tarball into this directory. Do not replace the bytes
   of an existing versioned filename.
3. Update the exact tarball exception in the repository's root `.gitignore` so
   the new artifact is included in clean checkouts.
4. From `frontend`, install the new archive with
   `npm install --save-exact ./vendor/<new-tarball>.tgz`.
5. Record and verify the new SHA-256 checksum, then run the frontend test and
   build gates from a clean install.

These artifacts are private source dependencies. Do not publish them to a public
registry.
