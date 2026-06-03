# Quickstart Wrapper Plus Makefile Plan

## Assumptions

- Default evaluator path should be published images, not source build.
- Source build remains available for contributors.
- Real orchestration lives in Bash; Makefile only delegates.
- No Helm work is included in this slice.

## Success Criteria

A new user can run one primary command from the README and get the same local OAuth stack that currently requires several copied commands. The Makefile exposes convenient aliases, but the wrapper remains the source of behavior.

## Task 1: Define Wrapper Contract

**Description:** Add `scripts/quickstart.sh` with a small command surface:

```bash
./scripts/quickstart.sh published [--tag latest]
./scripts/quickstart.sh source
./scripts/quickstart.sh down
./scripts/quickstart.sh logs
```

**Acceptance criteria:**

- `published` starts `deploy/local-oauth/compose.release.yaml`.
- `source` starts `deploy/local-oauth/compose.yaml --build`.
- `down` tears down both known quickstart Compose files safely.
- `logs` prints recent logs for the active/local stack.

**Verification:**

- `./scripts/quickstart.sh --help`
- `bash -n scripts/quickstart.sh`

**Dependencies:** None

**Files likely touched:**

- `scripts/quickstart.sh`

**Estimated scope:** Small

## Task 2: Implement Prerequisite Checks

**Description:** Check only what the selected mode needs.

Published mode needs:

- `docker compose`
- `kubectl`
- reachable current cluster/minikube through existing `create-demo-kubeconfig.sh`

Source mode also needs:

- `dotnet`
- source env generation through `scripts/generate-env.sh`

**Acceptance criteria:**

- Missing Docker Compose gives a direct error.
- Missing `.NET` only blocks `source`, not `published`.
- The script delegates cluster/RBAC setup to `create-demo-kubeconfig.sh --compose`.

**Verification:**

- `bash -n scripts/quickstart.sh`
- Manual dry review of each failure branch.

**Dependencies:** Task 1

**Files likely touched:**

- `scripts/quickstart.sh`

**Estimated scope:** Small

## Task 3: Wire Existing Commands Without Duplicating Logic

**Description:** Call existing scripts instead of reimplementing them.

Published mode:

```bash
./scripts/create-demo-kubeconfig.sh --compose
TAG="${tag}" docker compose --env-file deploy/local-oauth/release.env.example \
  -f deploy/local-oauth/compose.release.yaml up
```

Source mode:

```bash
./scripts/create-demo-kubeconfig.sh --compose
./scripts/generate-env.sh local-compose
docker compose --env-file deploy/generated/local-compose.env \
  -f deploy/local-oauth/compose.yaml up --build
```

**Acceptance criteria:**

- RunProfiles remain the config source of truth.
- No new duplicate env-generation path is introduced.
- Existing Compose files remain the deployment source.

**Verification:**

- `./scripts/quickstart.sh source` reaches Compose config/start phase.
- `./scripts/quickstart.sh published --tag latest` reaches Compose config/start phase.

**Dependencies:** Tasks 1 and 2

**Files likely touched:**

- `scripts/quickstart.sh`

**Estimated scope:** Medium

## Task 4: Add Tiny Makefile Aliases

**Description:** Add a root `Makefile` that delegates only:

```make
quickstart:
	./scripts/quickstart.sh published

quickstart-source:
	./scripts/quickstart.sh source

quickstart-down:
	./scripts/quickstart.sh down

quickstart-logs:
	./scripts/quickstart.sh logs
```

**Acceptance criteria:**

- No prerequisite logic lives in the Makefile.
- No duplicated Compose commands live in the Makefile.
- `make quickstart` is the friendly alias, not the canonical implementation.

**Verification:**

- `make -n quickstart`
- `make -n quickstart-source`
- `make -n quickstart-down`

**Dependencies:** Task 1

**Files likely touched:**

- `Makefile`

**Estimated scope:** Extra small

## Task 5: Update README Quick Start

**Description:** Replace the multi-command happy path with:

```bash
./scripts/quickstart.sh published
```

Keep source build as the contributor path:

```bash
./scripts/quickstart.sh source
```

Mention Makefile aliases as optional:

```bash
make quickstart
make quickstart-source
```

**Acceptance criteria:**

- README has one obvious default path.
- Existing detailed commands are not lost; they move or remain in the setup guide.
- Published image path stays no-SDK.

**Verification:**

- Read README quick start top-to-bottom as a new user.
- Confirm commands match script behavior.

**Dependencies:** Tasks 1 through 4

**Files likely touched:**

- `README.md`

**Estimated scope:** Small

## Task 6: Update Setup Guide

**Description:** Update `docs/setup-guide.md` so the Keycloak Local OAuth section leads with the wrapper, then shows the expanded commands as "what the wrapper does."

**Acceptance criteria:**

- Setup guide still documents Keycloak topology, accounts, clients, scopes, and troubleshooting.
- The wrapper does not hide the OAuth/security caveats.
- The old direct commands remain available for debugging.

**Verification:**

- Search docs for stale quickstart command blocks.
- Ensure `release.env.example`, `generate-env.sh`, and RunProfiles are still explained.

**Dependencies:** Tasks 1 through 5

**Files likely touched:**

- `docs/setup-guide.md`

**Estimated scope:** Small

## Final Verification

Run:

```bash
bash -n scripts/quickstart.sh
make -n quickstart
make -n quickstart-source
make -n quickstart-down
dotnet run --project src/InfraGate.RunProfiles -- validate
```

If Docker/minikube are available, also run one full smoke path:

```bash
./scripts/quickstart.sh published --tag latest
```

## Risks

- `down` may need to handle both release and source Compose files. Keep it explicit and conservative.
- `published` currently runs only Keycloak/Postgres/Gateway, while `source` runs agents too. README should state that difference.
- Makefile portability is fine for Linux/macOS, but Windows users should use the script directly under Bash/WSL.

## Recommendation

Implement this as one small vertical slice: wrapper, Makefile, README/setup-guide update, then verify. Do not add flags like `--no-agents`, alternate IdPs, or Helm values in this pass.
