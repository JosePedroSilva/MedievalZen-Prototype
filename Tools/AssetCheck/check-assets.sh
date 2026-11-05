#!/usr/bin/env bash
# check-assets.sh - checks that the third-party asset packs MedievalZen needs are present in
# this working copy.
#
# The Synty packs, the polyperfect animals and the sound packs are licensed and weigh about
# 8.5 GB, so they are git-ignored (see .gitignore) and every clone has to import them locally.
# This script reads Tools/AssetCheck/third-party-packs.txt, checks each pack's folder, file
# count and sentinel files, and lists what still has to be downloaded. It also looks for
# Git LFS files that are still pointers (a clone made without Git LFS installed).
#
# Runs with the bash that ships with macOS (3.2) and Linux, and with Git Bash on Windows
# (Windows users can also run check-assets.cmd, which uses PowerShell).
#
# Usage: Tools/AssetCheck/check-assets.sh [--root <project dir>] [--manifest <file>] [--skip-lfs]
# Exit code 0: everything present. 1: something to download or fetch. 2: could not run.

set -u

script_dir=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$script_dir/../.." && pwd)
manifest="$script_dir/third-party-packs.txt"
skip_lfs=0

usage() { sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --root)     [ $# -ge 2 ] || { echo "--root needs a value" >&2; exit 2; }; root=$2; shift 2 ;;
    --manifest) [ $# -ge 2 ] || { echo "--manifest needs a value" >&2; exit 2; }; manifest=$2; shift 2 ;;
    --skip-lfs) skip_lfs=1; shift ;;
    -h|--help)  usage; exit 0 ;;
    *)          echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ ! -d "$root" ]; then echo "Project folder not found: $root" >&2; exit 2; fi
root=$(cd "$root" && pwd)
if [ ! -f "$root/ProjectSettings/ProjectVersion.txt" ]; then
  echo "Not a Unity project root (no ProjectSettings/ProjectVersion.txt): $root" >&2; exit 2
fi
if [ ! -f "$manifest" ]; then echo "Manifest not found: $manifest" >&2; exit 2; fi

if [ -t 1 ]; then
  c_ok=$'\033[32m'; c_bad=$'\033[31m'; c_warn=$'\033[33m'; c_dim=$'\033[2m'; c_head=$'\033[36m'; c_off=$'\033[0m'
else
  c_ok=""; c_bad=""; c_warn=""; c_dim=""; c_head=""; c_off=""
fi

# trim <text>: strip leading and trailing whitespace.
trim() {
  local v=$1
  v="${v#"${v%%[![:space:]]*}"}"
  v="${v%"${v##*[![:space:]]}"}"
  printf '%s' "$v"
}

# count_files <dir>: files of the pack, .meta files excluded (Unity rewrites those freely).
count_files() { find "$1" -type f ! -name '*.meta' 2>/dev/null | wc -l | tr -d '[:space:]'; }

# is_pointer <file>: a Git LFS file that was never fetched is a small text file starting with this line.
is_pointer() {
  case "$(head -c 48 "$1" 2>/dev/null | tr -d '\000')" in
    "version https://git-lfs.github.com/spec/v1"*) return 0 ;;
  esac
  return 1
}

echo
echo "${c_head}MedievalZen third-party asset check${c_off}"
echo "  project:  $root"
echo "  manifest: $manifest"
echo

n_ok=0; n_missing=0; n_partial=0; n_optional=0
to_download=""
line_no=0
while IFS= read -r raw || [ -n "$raw" ]; do
  line_no=$((line_no + 1))
  line=$(trim "$raw")
  case "$line" in ''|'#'*) continue ;; esac
  pipes=${line//[^|]/}
  if [ ${#pipes} -ne 5 ]; then
    echo "  ${c_warn}manifest line $line_no has $(( ${#pipes} + 1 )) fields, expected 6; skipped${c_off}"
    continue
  fi
  IFS='|' read -r f_name f_folder f_min f_req f_sent f_src <<<"$line"
  name=$(trim "$f_name"); folder=$(trim "$f_folder"); min=$(trim "$f_min")
  req=$(trim "$f_req"); sent=$(trim "$f_sent"); src=$(trim "$f_src")
  dir="$root/$folder"

  status=""; detail=""; color=""
  if [ ! -d "$dir" ]; then
    if [ "$req" = "required" ]; then
      status=MISSING; color=$c_bad; n_missing=$((n_missing + 1))
    else
      status=OPTIONAL; color=$c_dim; n_optional=$((n_optional + 1))
    fi
    detail="folder not found"
  else
    count=$(count_files "$dir")
    missing_sent=""
    if [ -n "$sent" ]; then
      IFS=';' read -ra parts <<<"$sent"
      for s in "${parts[@]}"; do
        s=$(trim "$s")
        [ -n "$s" ] || continue
        if [ ! -e "$dir/$s" ]; then
          if [ -n "$missing_sent" ]; then missing_sent="$missing_sent, $s"; else missing_sent=$s; fi
        fi
      done
    fi
    if [ "$count" -lt "$min" ] || [ -n "$missing_sent" ]; then
      status=PARTIAL; color=$c_warn; n_partial=$((n_partial + 1))
      if [ "$count" -lt "$min" ]; then detail="$count files, expected at least $min"; fi
      if [ -n "$missing_sent" ]; then
        if [ -n "$detail" ]; then detail="$detail; "; fi
        detail="${detail}missing: $missing_sent"
      fi
    else
      status=OK; color=$c_ok; n_ok=$((n_ok + 1))
      detail="$count files"
    fi
  fi

  printf '  %s[%-8s]%s %-44s %s\n' "$color" "$status" "$c_off" "$name" "$folder"
  if [ "$status" != "OK" ]; then
    printf '             %s%s%s\n' "$color" "$detail" "$c_off"
    if [ "$status" != "OPTIONAL" ]; then
      to_download="${to_download}${name}|${folder}|${src}"$'\n'
    fi
  fi
done < "$manifest"

# --- Git LFS pointers that were never fetched ---
lfs_tracked=0; lfs_pointers=0; lfs_example=""; lfs_note=""
if [ "$skip_lfs" -eq 1 ]; then
  lfs_note="skipped (--skip-lfs)"
elif ! command -v git >/dev/null 2>&1; then
  lfs_note="git is not on PATH; LFS check skipped"
elif [ "$(git -C "$root" rev-parse --is-inside-work-tree 2>/dev/null)" != "true" ]; then
  lfs_note="not a git work tree; LFS check skipped"
else
  while IFS= read -r -d '' path && IFS= read -r -d '' _attr && IFS= read -r -d '' value; do
    [ "$value" = "lfs" ] || continue
    lfs_tracked=$((lfs_tracked + 1))
    full="$root/$path"
    [ -f "$full" ] || continue
    if is_pointer "$full"; then
      lfs_pointers=$((lfs_pointers + 1))
      [ -n "$lfs_example" ] || lfs_example=$path
    fi
  done < <(git -C "$root" ls-files -z | git -C "$root" check-attr --stdin -z filter)
fi

echo
if [ -n "$lfs_note" ]; then
  echo "  ${c_dim}Git LFS: $lfs_note${c_off}"
elif [ "$lfs_pointers" -gt 0 ]; then
  echo "  ${c_bad}Git LFS: $lfs_pointers of $lfs_tracked LFS files are unfetched pointers, e.g. $lfs_example${c_off}"
  if git lfs version >/dev/null 2>&1; then
    echo "  ${c_bad}         run: git lfs pull${c_off}"
  else
    echo "  ${c_bad}         install Git LFS (https://git-lfs.com), then run: git lfs install && git lfs pull${c_off}"
  fi
else
  echo "  ${c_ok}Git LFS: $lfs_tracked tracked files, all fetched${c_off}"
fi

# --- Summary ---
echo
echo "  $n_ok ok, $n_missing missing, $n_partial partial, $n_optional optional not installed"
if [ -n "$to_download" ]; then
  echo
  echo "  ${c_warn}To download or re-import:${c_off}"
  printf '%s' "$to_download" | while IFS='|' read -r d_name d_folder d_src; do
    [ -n "$d_name" ] || continue
    echo "   ${c_warn}- $d_name${c_off}"
    echo "       into:  $d_folder"
    echo "       from:  $d_src"
  done
  echo
  echo "  Import each pack with its own .meta files (the .unitypackage from the store, or a copy of the folder"
  echo "  from another checkout): first-party prefabs, scenes and palettes reference these assets by GUID."
fi
echo
if [ "$n_missing" -gt 0 ] || [ "$n_partial" -gt 0 ] || [ "$lfs_pointers" -gt 0 ]; then
  echo "  ${c_bad}RESULT: FAIL - the project will not open cleanly until the items above are in place.${c_off}"
  exit 1
fi
echo "  ${c_ok}RESULT: OK - every required pack is present.${c_off}"
exit 0
