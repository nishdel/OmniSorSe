# OpenSorSe 1.6 Troubleshooting

## A setting, catalog, workflow, or history change did not save

Check the visible status and redacted diagnostics for permission, disk-space,
capacity, or unavailable-directory errors. OpenSorSe writes a complete sibling
before replacing application-owned JSON, so a failed write should leave the
previous complete document intact. Do not delete an unexpected `*.tmp` file
while another OpenSorSe process is running. OpenSorSe's coordination is
process-local, not a cross-process lock.

## An invalid owned file was preserved

This is intentional. Settings and workflow recovery keep malformed input for
diagnosis rather than silently overwriting it. Back up the file, inspect only
for non-sensitive configuration data, then use the documented explicit save or
clear action. Optional content and semantic indexes can be rebuilt.

## Cancellation appears delayed

Cancellation is cooperative and is observed between safe units of work.
Filesystem APIs, an external OCR process, a provider request, or a currently
executing safe file-operation boundary may need to return before cancellation
completes. OpenSorSe does not interrupt a mutation midway through an unsafe
boundary.

## Watched-folder state says reconciliation is required

Watcher events are hints and can be duplicated, reordered, omitted, or
overflowed. Use **Full reconciliation**. Restore access if the root is
unavailable, then refresh. A case-only name can represent one path or two paths
depending on the host/filesystem case policy reported by System check.

## The application closed while watching or scanning

On a normal close, v1.6 cancels and awaits the owned watcher loops. The next
startup still performs offline reconciliation. If the process was forcibly
terminated, inspect watched activity and Operation History; do not infer success
from the absence of a notification.

## A Change Plan or Undo is blocked

Revalidate the plan. OpenSorSe blocks stale sources, occupied destinations,
changed identities, later-operation dependencies, and unsafe Undo rather than
overwriting. The Operation Journal and exported operation report contain the
safe failure category and recovery facts.

## Screen-reader status is missing

Verify the native v1.6 Desktop is running, the host accessibility service is
enabled, and focus is within the application. Critical status surfaces use
polite live announcements; the screen reader can intentionally defer them
while speaking. Record the host, reader/version, page, control, and exact action
for a reproducible issue.

For feature-specific guidance, also see the
[v1.5 troubleshooting guide](TROUBLESHOOTING_v1.5.md) and
[v1.6 manual checklist](MANUAL_TESTING_v1.6.md).
