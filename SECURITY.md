# Security Policy

## Supported Versions

Kubernetes MCP Guard is an **experimental** project. Security fixes target the latest release only. No backports are planned while the project is in pre-release.

| Version | Supported |
| --- | --- |
| latest pre-release | Yes |
| older releases | No |

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Use [GitHub Security Advisories](https://github.com/mirusser/Kubernetes-MCP-Guard/security/advisories/new) to report vulnerabilities privately. This keeps the details confidential until a fix is coordinated and released.

## What to Include

A useful report includes:

- Affected version or commit hash.
- Description of the vulnerability and the affected component.
- Steps to reproduce (minimal, self-contained where possible).
- Potential impact — what an attacker could achieve.
- Suggested mitigation or fix, if known.

## Disclosure

Please allow reasonable time for investigation and a patch before public disclosure. We aim to acknowledge reports within 5 business days and to coordinate disclosure timing with reporters.

## Scope

The OAuth 2.1 / OIDC auth surface and MCP transport compliance are documented in [docs/MCP-compliance.md](docs/MCP-compliance.md). A full security model document (`docs/security-model.md`) covering hard boundaries, threat model, and non-goals is planned for Epic 4 of the roadmap.

The `InfraGate.DevIssuer` component is a development-only OAuth/OIDC issuer. It is not designed for production use and is not in scope for production security review.
