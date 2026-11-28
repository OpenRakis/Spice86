#!/usr/bin/env bash
# Builds the pull-request comment for the generated-code diff workflow.
#
# Usage: generated-code-diff-comment.sh <root>
# <root> contains base/ and head/, the dump folders produced by the
# AsmScenarioTest runs with SPICE86_DUMPS_FOLDER. The comment markdown is
# printed to stdout.
#
# Optional environment variables:
#   BASE_SHA, HEAD_SHA       short commit shas
#   RUN_URL                  link to the workflow run
#   ARTIFACT_URL             link to the uploaded diff artifact
#   BASE_DUMP_OUTCOME, HEAD_DUMP_OUTCOME   GitHub step outcomes
set -euo pipefail

root=${1:?usage: generated-code-diff-comment.sh <root>}
cd "$root" >/dev/null # >/dev/null: CDPATH can make cd echo the directory

BASE_SHA=${BASE_SHA:-}
HEAD_SHA=${HEAD_SHA:-}
RUN_URL=${RUN_URL:-}
ARTIFACT_URL=${ARTIFACT_URL:-}
BASE_DUMP_OUTCOME=${BASE_DUMP_OUTCOME:-}
HEAD_DUMP_OUTCOME=${HEAD_DUMP_OUTCOME:-}

DIFF_MAX_CHARS=2000
DIFF_TOTAL_MAX_CHARS=10000
TABLE_MAX_ROWS=50
METRICS_CELL_MAX=8

comment=""
line() { comment+="$1"$'\n'; }
blank() { comment+=$'\n'; }

sha_suffix() {
    if [ -n "$BASE_SHA$HEAD_SHA" ]; then
        printf ' Base `%s`, head `%s`.' "${BASE_SHA:-?}" "${HEAD_SHA:-?}"
    fi
}

dump_warnings() {
    if [ -n "$HEAD_DUMP_OUTCOME" ] && [ "$HEAD_DUMP_OUTCOME" != success ]; then
        line "Warning: the head dump step failed; missing or changed scenarios can come from test failures."
    fi
    if [ -n "$BASE_DUMP_OUTCOME" ] && [ "$BASE_DUMP_OUTCOME" != success ]; then
        line "Warning: the base dump step failed; missing or changed scenarios can come from test failures."
    fi
}

links_line() {
    local links=""
    if [ -n "$ARTIFACT_URL" ]; then
        links="[Full diff and dumps (zip)]($ARTIFACT_URL)"
    fi
    if [ -n "$RUN_URL" ]; then
        [ -n "$links" ] && links+=" · "
        links+="[Run]($RUN_URL)"
    fi
    if [ -n "$links" ]; then
        blank
        line "$links"
    fi
}

# A scenario is a subdirectory of base/ or head/ whose name does not start
# with a dot (leftover staging dirs). Top-level files are not scenarios.
shopt -s nullglob
declare -A in_base=() in_head=()
for d in base/*/; do
    name=${d%/}; name=${name#base/}
    [[ $name == .* ]] && continue
    in_base[$name]=1
done
for d in head/*/; do
    name=${d%/}; name=${name#head/}
    [[ $name == .* ]] && continue
    in_head[$name]=1
done
mapfile -t scenarios < <(printf '%s\n' "${!in_base[@]}" "${!in_head[@]}" | sed '/^$/d' | sort -u)

# Count the lines of every file under a directory.
dir_lines() {
    find "$1" -type f -exec cat {} + | wc -l | tr -d ' '
}

# --- Classify every scenario -------------------------------------------------
declare -A status=() added=() deleted=()
declare -a report=() changed_names=()
for n in "${scenarios[@]}"; do
    if [ -z "${in_base[$n]:-}" ]; then
        status[$n]=new
        added[$n]=$(dir_lines "head/$n")
        deleted[$n]=0
    elif [ -z "${in_head[$n]:-}" ]; then
        status[$n]=removed
        added[$n]=0
        deleted[$n]=$(dir_lines "base/$n")
    elif git diff --no-index --quiet "base/$n" "head/$n"; then
        continue # unchanged scenarios are omitted
    else
        status[$n]=changed
        numstat=$(git diff --no-index --numstat "base/$n" "head/$n" || true)
        added[$n]=$(awk '$1 ~ /^[0-9]+$/ {a += $1} END {print a + 0}' <<<"$numstat")
        deleted[$n]=$(awk '$2 ~ /^[0-9]+$/ {d += $2} END {print d + 0}' <<<"$numstat")
        changed_names+=("$n")
    fi
    report+=("$n")
done

count_new=0; count_removed=0; count_changed=0; total_add=0; total_del=0
for n in "${report[@]:-}"; do
    [ -z "$n" ] && continue
    case ${status[$n]} in
        new) count_new=$((count_new + 1)) ;;
        removed) count_removed=$((count_removed + 1)) ;;
        changed) count_changed=$((count_changed + 1)) ;;
    esac
    total_add=$((total_add + ${added[$n]}))
    total_del=$((total_del + ${deleted[$n]}))
done

# --- Metrics -----------------------------------------------------------------
# A metrics.txt row is "part=program fixture=X k=v ... | part=support
# fixture=X k=v ...". Emit one TSV row per value: fixture, part.key, value.
parse_metrics() {
    awk '
        {
            n = split($0, half, / \| /)
            for (h = 1; h <= n; h++) {
                m = split(half[h], tok, /[ \t]+/)
                part = ""; fixture = ""; nk = 0
                delete keys; delete vals
                for (i = 1; i <= m; i++) {
                    if (tok[i] ~ /^part=/) {
                        part = substr(tok[i], 6)
                    } else if (tok[i] ~ /^fixture=/) {
                        fixture = substr(tok[i], 9)
                    } else if (tok[i] ~ /^warningIds=/) {
                        # free text, may contain spaces: the rest of the half
                        keys[++nk] = "warningIds"
                        vals[nk] = substr(half[h], index(half[h], "warningIds=") + 11)
                        break
                    } else if (tok[i] ~ /^[A-Za-z0-9_.]+=/) {
                        keys[++nk] = tok[i]
                        sub(/=.*/, "", keys[nk])
                        vals[nk] = substr(tok[i], length(keys[nk]) + 2)
                    }
                }
                for (i = 1; i <= nk; i++) {
                    print fixture "\t" part "." keys[i] "\t" vals[i]
                }
            }
        }' "$1"
}

metrics_identical=true
metric_rows=""
if [ -f base/metrics.txt ] && [ -f head/metrics.txt ]; then
    if ! cmp -s base/metrics.txt head/metrics.txt; then
        metrics_identical=false
    fi
    # One TSV row per changed value: fixture \t "part.key old→new".
    metric_rows=$(awk -F'\t' '
        NR==FNR { bv[$1, $2] = $3; bf[$1] = 1; next }
                { hv[$1, $2] = $3; hf[$1] = 1 }
        END {
            for (f in bf) if (!(f in hf)) print f "\tremoved"
            for (f in hf) if (!(f in bf)) print f "\tnew"
            for (k in bv) {
                split(k, a, SUBSEP); f = a[1]
                if (!(f in bf) || !(f in hf)) continue
                if (!((f, a[2]) in hv) || hv[f, a[2]] != bv[f, a[2]]) {
                    new = ((f, a[2]) in hv) ? hv[f, a[2]] : "-"
                    print f "\t" a[2] " " bv[f, a[2]] "→" new
                }
            }
            for (k in hv) {
                if (k in bv) continue
                split(k, a, SUBSEP); f = a[1]
                if (!(f in bf) || !(f in hf)) continue
                print f "\t" a[2] " -→" hv[f, a[2]]
            }
        }' <(parse_metrics base/metrics.txt) <(parse_metrics head/metrics.txt) \
        | sort -t$'\t' -k1,1 -k2,2)
elif [ -f base/metrics.txt ] || [ -f head/metrics.txt ]; then
    # Present on one side only: a change, but no table (spec: omit when a
    # metrics file is missing on one side).
    metrics_identical=false
fi

# --- Empty base --------------------------------------------------------------
base_empty=false
if [ ! -d base ] || [ -z "$(find base -mindepth 1 -maxdepth 1 ! -name '.*' -print -quit)" ]; then
    base_empty=true
fi

if $base_empty; then
    line "Base has no dump support (base ${BASE_SHA:-?}); head produced ${#in_head[@]} scenarios."
    dump_warnings
    links_line
    printf '%s' "$comment"
    exit 0
fi

# --- No change at all --------------------------------------------------------
if [ ${#report[@]} -eq 0 ] && $metrics_identical; then
    line "No change in generated code or listings.$(sha_suffix)"
    dump_warnings
    links_line
    printf '%s' "$comment"
    exit 0
fi

# --- Full comment ------------------------------------------------------------
line "Generated code: $count_changed scenarios changed, $count_new new, $count_removed removed (+$total_add -$total_del lines).$(sha_suffix)"
dump_warnings
blank

if [ ${#report[@]} -gt 0 ]; then
    line "| Scenario | Change | Lines |"
    line "|---|---|---|"
    rows=0
    for n in "${report[@]}"; do
        rows=$((rows + 1))
        if [ $rows -gt $TABLE_MAX_ROWS ]; then
            line "| … | $(( ${#report[@]} - TABLE_MAX_ROWS )) more | |"
            break
        fi
        case ${status[$n]} in
            changed) lines="+${added[$n]} -${deleted[$n]}" ;;
            new) lines="+${added[$n]}" ;;
            removed) lines="-${deleted[$n]}" ;;
        esac
        line "| \`$n\` | ${status[$n]} | $lines |"
    done
    blank
fi

if [ -n "$metric_rows" ]; then
    line "| Metrics fixture | Changes |"
    line "|---|---|"
    metric_table=$(awk -F'\t' -v max="$METRICS_CELL_MAX" -v maxrows="$TABLE_MAX_ROWS" '
        function flush() {
            if (fixture == "") return
            rowcount++
            if (rowcount > maxrows) { pending += count; fixture = ""; return }
            cell = changes[1]
            for (i = 2; i <= count && i <= max; i++) cell = cell ", " changes[i]
            if (count > max) cell = cell ", +" (count - max) " more"
            print "| `" fixture "` | " cell " |"
            fixture = ""; count = 0
        }
        $1 != fixture { flush(); fixture = $1 }
        { count++; changes[count] = $2 }
        END {
            flush()
            if (pending > 0) print "| … | " pending " more |"
        }' <<<"$metric_rows")
    line "$metric_table"
    blank
fi

# Details blocks for changed scenarios only, within the char budgets.
diff_budget=$DIFF_TOTAL_MAX_CHARS
shown=0
for n in "${changed_names[@]:-}"; do
    [ -z "$n" ] && continue
    remaining=$(( ${#changed_names[@]} - shown ))
    if [ "$diff_budget" -le 0 ] && [ "$remaining" -gt 0 ]; then
        line "Diffs of $remaining more scenarios are not shown, see the full diff."
        break
    fi
    d=$(git diff --no-index "base/$n" "head/$n" || true)
    cap=$DIFF_MAX_CHARS
    [ "$cap" -gt "$diff_budget" ] && cap=$diff_budget
    cut=$(awk -v cap="$cap" '
        { if (len + length($0) + 1 > cap) { trunc = 1; exit } print; len += length($0) + 1 }
        END { if (trunc) print "... truncated, see the full diff" }' <<<"$d")
    line "<details><summary><code>$n</code> +${added[$n]} -${deleted[$n]}</summary>"
    blank
    line '````diff'
    line "$cut"
    line '````'
    line "</details>"
    blank
    diff_budget=$((diff_budget - ${#cut}))
    shown=$((shown + 1))
done

links_line
printf '%s' "$comment"
