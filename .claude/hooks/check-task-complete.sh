#!/bin/bash
# SubagentStop hook: blocks session end if a task is still in progress.
# Exit 0 = allow (task was completed or never started)
# Exit 2 = block (task started but not done — message fed back to agent)

ACTIVE_TASK=".ydk/active-task.json"

# From a linked worktree, active-task.json lives in the main checkout.
unset GIT_DIR GIT_WORK_TREE GIT_COMMON_DIR
WT_GIT_DIR=$(cd "$(git rev-parse --git-dir 2>/dev/null)" 2>/dev/null && pwd -P)
WT_COMMON_DIR=$(cd "$(git rev-parse --git-common-dir 2>/dev/null)" 2>/dev/null && pwd -P)
if [ -n "$WT_GIT_DIR" ] && [ -n "$WT_COMMON_DIR" ] && [ "$WT_GIT_DIR" != "$WT_COMMON_DIR" ] \
    && [ "$(basename "$WT_COMMON_DIR")" = ".git" ]; then
    ACTIVE_TASK="$(dirname "$WT_COMMON_DIR")/.ydk/active-task.json"
fi

if [ ! -f "$ACTIVE_TASK" ]; then
    exit 0
fi

TASK_IDS=$(python3 -c "import json, sys; d=json.load(open(sys.argv[1])); print(','.join(d.get('tasks', {})))" "$ACTIVE_TASK" 2>/dev/null)

if [ -z "$TASK_IDS" ]; then
    exit 0
fi

echo "BLOCKED: Task(s) $TASK_IDS still in progress."
echo ""
echo "You must complete the task before finishing:"
echo "  1. Ensure all tests pass"
echo "  2. Ensure lint is clean"
echo "  3. Run: ydk task done <task-id>"
echo ""
echo "The session cannot end until the task is properly completed with a PR."
exit 2
