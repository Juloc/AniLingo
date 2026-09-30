# Lesson / Review — Clean Design

Status: planning baseline for mockups.

## Purpose
Focused lesson and SRS review surfaces using the shared Learning domain.

## Page structure
Lesson: objective/progress -> exercise surface -> feedback -> next. Review: due-card prompt -> reveal/answer -> rating/feedback -> next -> completion summary.

## Data / information
Course/lesson/exercise identity, canonical LearningUnit/Variant/Card, learner progress, review schedule, optional media context, TTS availability.

## Actions
Answer, reveal, rate review, listen/replay, skip where policy allows, inspect explanation/context, pause/exit with progress preserved.

## Light / Dark
Both first-class; distraction-minimized with accessible answer/feedback states.

## Platforms
Desktop: keyboard-first shortcuts plus pointer. Mobile/tablet: large touch targets and swipe only where non-destructive/clear. TV: simplified remote-capable exercise subset only.

## States
Loading, no reviews due, lesson complete, answer feedback, TTS unavailable, offline course/review, optional AI explanation unavailable, error/retry.

## Must not implement
No separate review scheduler/store, no forced AI, no hidden keyboard-only action, no media-specific learning copies, no loss of progress on normal exit.