#!/usr/bin/env bash
set -euo pipefail

SKILLS_SOURCE="$HOME/OtherRepos/dotnet-skills"
SKILLS_TARGET="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills"

if [ ! -d "$SKILLS_SOURCE" ]; then
  echo "Cloning managedcode/dotnet-skills..."
  git clone https://github.com/managedcode/dotnet-skills "$SKILLS_SOURCE"
else
  echo "Pulling latest managedcode/dotnet-skills..."
  git -C "$SKILLS_SOURCE" pull --ff-only
fi

echo "Syncing skill symlinks to $SKILLS_TARGET..."
linked=0
skipped=0

while IFS= read -r skillmd; do
  skill_dir=$(dirname "$skillmd")
  skill_name=$(basename "$skill_dir")
  target="$SKILLS_TARGET/$skill_name"

  if [ -L "$target" ]; then
    ln -sfn "$skill_dir" "$target"
    ((linked++)) || true
  elif [ -e "$target" ]; then
    echo "  SKIP (real dir, not a symlink): $skill_name"
    ((skipped++)) || true
  else
    ln -s "$skill_dir" "$target"
    ((linked++)) || true
  fi
done < <(find "$SKILLS_SOURCE/catalog" -path "*/skills/*/SKILL.md")

echo "Done skills: $linked linked/updated, $skipped skipped."

AGENTS_TARGET="$(cd "$(dirname "$0")/.." && pwd)/agents"
linked=0
skipped=0

echo "Syncing agent symlinks to $AGENTS_TARGET..."

while IFS= read -r agentmd; do
  agent_name=$(basename "$(dirname "$agentmd")")
  target="$AGENTS_TARGET/${agent_name}.agent.md"

  if [ -L "$target" ]; then
    ln -sfn "$agentmd" "$target"
    ((linked++)) || true
  elif [ -e "$target" ]; then
    echo "  SKIP (real file, not a symlink): $agent_name"
    ((skipped++)) || true
  else
    ln -s "$agentmd" "$target"
    ((linked++)) || true
  fi
done < <(find "$SKILLS_SOURCE/catalog" -path "*/agents/*/AGENT.md" ! -path "*/skills/*/references/*")

echo "Done agents: $linked linked/updated, $skipped skipped."
