# Local Git hooks

`npm ci` installs this repository-local hook path automatically. The hooks reject agent or AI identities and attribution trailers before a commit is recorded and before commits are pushed.

CI performs the same verification for every pull request and push, so bypassing a local hook cannot bypass the protected `main` branch.
