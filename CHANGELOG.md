# Changelog

All notable changes to Kubernetes MCP Guard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses pre-release tags while it remains experimental.

## [Unreleased]

### Added

- Added contributor and maintenance documentation for Epic 9: contributor guide, pull request checklist, issue templates, public roadmap, and changelog.
- Server-side Kubernetes `dryRun=All` validation before approval plan creation and again at apply time. Mutation plans record dry-run results in the hash-bound pending plan; the gateway approval page renders dry-run status; legacy plans without dry-run data are refused.

## Release History

Earlier experimental tags exist (`v0.0.1` through `v0.0.4`), but detailed historical changelog entries have not been reconstructed. Use GitHub Releases and commit history for those versions.
