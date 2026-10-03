# Learning Course Detail — V1

Status: **binding V1 planning specification**. The approved Original Jularr Desktop + Mobile mockup is the visual reference for this screen. Text wins over imagery where behavior/data semantics conflict.

Global UX: `docs/UX.md`  
Learning Home: `docs/mockups/learning-home/SPEC.md`  
Lesson / Review: `docs/mockups/lesson-review/SPEC.md`  
Original J mascot: `docs/assets/original-j/jularr-mascot-reference.png`

## 1. Purpose

Course Detail connects Learning Home with the focused Lesson surface.

It answers:
- what course am I learning?
- where am I in the curriculum?
- what should I continue next?
- which Chapters/Lessons are complete, in progress, available or genuinely locked?
- how much course progress have I made?
- how many reviews are due without confusing review mastery with course completion?

The page is a curriculum navigator, not a second Learning Dashboard and not a game map.

## 2. Canonical hierarchy

The UI projects the existing Learning curriculum model:

```text
Curriculum
  -> Level
    -> Chapter
      -> Lesson
        -> Exercise
```

Course/profile state is layered over that shared curriculum.

Rules:
- do not introduce a parallel persisted `Course -> Chapter -> Lesson` hierarchy;
- `Level` may be visually omitted/flattened when the authored course does not need a visible level grouping;
- when Levels are meaningful, they group Chapters and remain part of navigation/progress;
- Exercise belongs inside Lesson and is not expanded on Course Detail;
- the page consumes application/view contracts rather than binding UI directly to EF entities.

## 3. Course identity

Header may show:
- course name;
- source -> target language pair;
- optional authored proficiency range;
- concise description;
- course artwork;
- enabled practice/capability summary where useful.

Language direction is explicit and never inferred from UI locale.

Examples:
- `German -> Japanese`
- `English -> Spanish`

BCP-47 language identity remains canonical underneath localized display names.

## 4. Proficiency labels

Labels such as `A1-B1`, JLPT or another framework appear only when the authored course metadata explicitly declares that alignment.

Do not infer CEFR/JLPT from:
- XP level;
- number of Lessons completed;
- FSRS state;
- generic course progress.

Gamification levels and real proficiency frameworks remain separate.

## 5. Desktop approved hierarchy

Approved wide layout:

```text
<- Courses

[ Course hero / artwork / mascot decoration                  ]
[ Course name                                                ]
[ source -> target | concise description | capability tags   ]

[ 42% course progress ] [ 12 reviews due ] [ ~12 min next ]
                                        [ Continue Learning ]

[ Content ] [ About ] [ Statistics if supported ]

[ Level / Chapter 1 ---------------------------------- 100% ]
[ Lesson ][ Lesson ][ Lesson ][ Lesson ][ Lesson ][ Lesson ]

[ Level / Chapter 2 ----------------------------------- 67% ]
[ Lesson ][ Lesson ][ Lesson ][ locked ][ locked ][ locked ]

[ Chapter 3 -------------------------------------------- 0% ]
...
```

The curriculum list is the dominant content after the compact hero.

Do not add a dashboard right rail here.

## 6. Original J visual direction

Original Jularr may use:
- warm paper/cream surfaces;
- ink/watercolor Japanese scenery;
- cherry-blossom edge decoration;
- red/pink accent;
- the canonical Jularr mascot when a companion figure is used.

Canonical mascot:
`docs/assets/original-j/jularr-mascot-reference.png`

For Course Detail the mascot may:
- hold a book/course card;
- point toward Continue/current Chapter;
- appear in the hero artwork;
- use neutral/focused/encouraging poses.

The mascot is optional and must not cover course information or Lesson controls.

Do not use unrelated third-party anime characters as permanent Jularr course branding.

Clean uses the same layout/data/actions with neutral surfaces and purple default accent.

## 7. Course summary

The summary region shows only useful course-level status:
- overall course progress;
- completed Lessons / total Lessons where meaningful;
- current Chapter/Level;
- due Review count;
- estimated duration of the next Lesson when available;
- primary Continue/Start action.

Do not show:
- large XP analytics;
- FSRS internals;
- provider/model state;
- admin curriculum metadata.

## 8. Primary action

Primary action resolves deterministically:

1. incomplete active Lesson -> **Continue Lesson**;
2. otherwise next available Lesson -> **Start Lesson**;
3. completed course -> **Review / Course complete** state according to product policy;
4. no available content -> concise unavailable state.

Continue opens the exact saved Lesson Step where resumable.

It must not create a second progress pointer separate from canonical learner course/Lesson progress.

## 9. Course progress

Course progress represents authored curriculum completion only.

It may be derived from:
- completed required Lessons;
- completed optional Lessons separately if the curriculum distinguishes them.

It is not derived from:
- FSRS retention;
- number of reviews due;
- XP;
- streak;
- time spent.

Review mastery is shown separately.

## 10. Due Reviews

Course Detail may show a compact due-review indicator:
- due count;
- optional estimated time;
- action to start Reviews filtered/biased to this course only if the scheduler/application contract explicitly supports that scope.

Never imply:
- course is incomplete because cards are due;
- a completed Lesson becomes incomplete when memory decays.

Scheduler remains the canonical FSRS source of truth.

## 11. Level presentation

If the curriculum uses meaningful Levels:
- show Level heading/label;
- show progress within Level;
- group its Chapters underneath;
- allow collapse/expand for large courses.

If the course has one trivial Level or no learner-facing Level concept:
- visually flatten Level;
- show Chapters directly;
- preserve canonical Level identity internally.

Do not invent `World`, `Path`, `Unit` or similar parallel hierarchy merely for gamification.

## 12. Chapter presentation

Each Chapter shows:
- title;
- optional concise description;
- completed / total Lesson count;
- Chapter progress;
- expand/collapse;
- optional artwork thumbnail.

States:
- complete;
- in progress/current;
- available/not started;
- locked by curriculum prerequisite.

Large courses must virtualize/page/lazy-load as needed rather than rendering an unbounded Lesson tree.

## 13. Lesson card / row

Each Lesson displays:
- authored order/number;
- title;
- optional skill/type icon;
- estimated duration where known;
- status;
- progress only when in progress;
- Start / Continue where appropriate.

Optional concise capability labels can include:
- Vocabulary;
- Grammar/concepts;
- Reading;
- Listening;
- Script/Kana;
- other actually supported exercise capabilities.

Do not advertise unsupported V1 assessment such as pronunciation scoring merely because a visual mockup contains a `Speaking` tag.

## 14. Lesson states

### Completed
- clear checkmark/status;
- no oversized celebration;
- may show completion/accuracy summary only if useful.

### In progress
- progress visible;
- **Continue**;
- visually prioritized inside current Chapter.

### Available
- **Start**;
- no fake progress.

### Locked
Only when an actual authored prerequisite is unresolved.

Show a concise reason on interaction/focus, e.g.:
`Complete Lesson 3 first`.

### Optional
If curriculum explicitly marks it optional:
- label as Optional;
- does not block required progression unless curriculum says otherwise.

## 15. Locking rules

Never lock curriculum by:
- XP;
- streak;
- account level;
- achievement count;
- payment-like virtual currency.

Locks come only from explicit curriculum prerequisites, availability/policy, or capability constraints.

A locked Lesson must not look like a server permission error.

## 16. Tabs / secondary course information

Baseline tabs:
- **Content** — default curriculum view;
- **About course** — authored description, goals, language direction and actual capabilities;
- **Statistics** — only concise course-specific learning progress if implemented.

Do not make `Notes` a required V1 tab merely because it appeared in exploratory imagery. Notes require their own canonical product contract before becoming a permanent course destination.

Avoid excessive nested tabs.

## 17. Course settings

A compact Course Settings action may open:
- enabled practice modes that are already part of the canonical course/profile model;
- primary course designation where relevant;
- course-specific personal preferences explicitly supported by Learning settings.

It must not expose:
- curriculum-authoring/admin controls;
- raw FSRS tuning;
- AI provider configuration;
- instance Learning policy.

Personal/global Learning settings stay in User Settings.

## 18. Course updates

When shared curriculum content changes:
- learner completion/progress must be preserved where canonical Lesson identity still resolves;
- reordered Lessons do not silently reset completion;
- deleted/replaced curriculum items follow explicit migration rules;
- newly added required Lessons do not retroactively corrupt stored historical progress.

UI may show a small `Course updated` notice if meaningful.

## 19. Mobile approved composition

Mobile is a vertical curriculum browser:

```text
<- Course

[ compact hero/artwork ]
Course name
source -> target

42% progress
[ reviews due ] [ next ~time ]
[ Continue Learning ]

[ Content | About | Stats ]

Chapter 1                  6/6
  ✓ Lesson 1
  ✓ Lesson 2
  ✓ Lesson 3

Chapter 2                  4/6
  ✓ Lesson 1
  ● Lesson 2       60%
  ○ Lesson 3       Start
  🔒 Lesson 4
```

Rules:
- one column;
- Chapters are collapsible;
- current Chapter defaults open;
- completed old Chapters may default collapsed;
- large touch targets;
- no Desktop Lesson-card grid squeezed into phone;
- hero decoration stays compact;
- primary Continue remains easy to reach.

## 20. Tablet

Portrait follows Mobile with wider rows/cards.

Landscape may use:
- compact hero + summary;
- 2-3 Lesson cards per row;
- Chapters full width.

Do not introduce a permanent secondary dashboard column unless the available width genuinely supports it.

## 21. TV

Course Detail is optional on TV.

If supported:
- large Chapter/Lesson rows;
- remote-safe focus;
- Continue as dominant action;
- only TV-compatible Lessons may start directly.

Text-entry-heavy Lessons can offer handoff instead.

No dense Desktop grid on TV.

## 22. Loading / empty / partial / error

### Loading
- stable hero/header skeleton;
- Chapter/Lesson skeletons;
- no fake progress values.

### Empty curriculum
If the course exists but has no published Lessons:
- concise `No lessons available yet`;
- Back;
- no fabricated Chapter.

### Partial
If one Chapter/Lesson fails to load:
- keep the rest of the course usable;
- section-level retry where safe.

### Course unavailable
If the course is disabled/removed for this profile:
- return to Learning Home or shared unavailable state;
- preserve historical progress.

No raw exceptions.

## 23. Accessibility

- semantic Chapter/Level headings;
- expand/collapse state announced;
- status not color-only;
- keyboard operation on Desktop;
- visible focus;
- touch targets >=44px;
- language names/target text use appropriate language metadata;
- locked reason accessible without hover;
- reduced motion supported.

## 24. Data contract

Course Detail needs a view/application contract containing at least:
- course/profile course identity;
- curriculum identity/version;
- source/target language;
- title/description/artwork;
- optional authored proficiency metadata;
- Level hierarchy;
- Chapter hierarchy;
- Lesson identity/order/title/capabilities/duration;
- prerequisite resolution;
- learner Lesson progress;
- course progress summary;
- exact resumable Lesson/Step target;
- due Review summary;
- effective availability/capability state.

Do not expose EF entities directly.

## 25. Required visual references

Approved:
1. **Desktop Original Jularr Light — Course Detail**
2. **Mobile Original Jularr Light — Course Detail**

Still useful later:
3. Desktop Dark validation;
4. Clean parity;
5. very large course / collapsed Levels+Chapters;
6. completed course;
7. unavailable/partial state.

The approved image is a visual reference; this text remains binding.

## 26. Must not implement

- No second curriculum hierarchy.
- No duplicate course progress store.
- No XP-based Lesson locking.
- No FSRS mastery treated as course completion.
- No course progress reset from ordinary curriculum reorder.
- No required Notes tab without a canonical Notes contract.
- No unsupported Speaking/pronunciation feature implied by decoration.
- No giant skill/game map for V1.
- No social/leaderboard/community-course surface here.
- No provider/admin internals.
- No theme-specific course behavior.
- No alternate Original J mascot identity when a mascot is used.

## 27. Acceptance

Course Detail is complete only when:
- it bridges Learning Home to exact Lesson resume/start;
- canonical Curriculum/Level/Chapter/Lesson structure is preserved;
- Level may flatten visually without duplicating data;
- current Lesson is immediately discoverable;
- completed/in-progress/available/locked states are distinct;
- locks come only from real curriculum/policy constraints;
- course progress and Review mastery stay separate;
- long courses remain usable;
- Mobile is intentionally list-based rather than squeezed Desktop;
- Original J and Clean are skins over identical behavior;
- the canonical mascot is used whenever Original J intentionally shows the mascot.
