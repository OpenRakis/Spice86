# Task 28 — SQ3: import interrupt dictionaries

**Status: postponed.** Do not start this task. Re-evaluate it once the main
tracks are done; what is required may have changed by then.

Converter/Spice86 side quest. Does not block R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §SQ3

## Goal

Fold chani's interrupt annotations, which live in a separate `.dict`
file, into the project model.

## Deliverables

- Decide the format-side representation for interrupt annotations
  (extension of the model; the spelling must go through the shared
  library so both Spice86 and converters agree).
- Converter support: read the `.dict` file alongside the databases and
  emit the annotations.
- Spice86 consumption: surface the annotations where interrupts appear in
  generated code or reports.

## Acceptance

- A chani project with a `.dict` file converts with the annotations
  present, loads, and round trips.

## Dependencies

- Tasks 01, 04, 05 (model extension), 21-22 (converter).
