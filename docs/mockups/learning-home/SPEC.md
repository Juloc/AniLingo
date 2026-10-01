# Learning Home — Learner-first Clean Design

Status: **binding planning specification for Learning Home mockups**. This refines `UX.md`, `LEARNING_V2.md`, the canonical Learning domain and the structured-course direction in issue #441.

The page is a learner-facing continuation surface. It is **not** an admin curriculum dashboard, a settings page, or a grid of every Learning module.

Architecture source of truth:
- Learning references canonical Work/Episode/Chapter identities rather than duplicating media.
- Structured course progress and SRS/FSRS mastery are separate concerns.
- Existing `LearningCard` / review history remains the scheduling source of truth.
- Course/curriculum state uses stable curriculum/course/unit identities.
- `LearningModuleResolver` / capability resolution controls which learner modules/actions are available.
- AI is optional. The normal learning path must remain usable with AI completely disabled.
- Media-derived vocabulary/sentences reuse canonical Learning units/contexts rather than creating duplicate stores.

## 1. Purpose

Learning Home answers four questions immediately:

1. **What should I continue?**
2. **What is due today?**
3. **What can I learn from my media?**
4. **What other course/tool do I want to open?**

Primary priority:
`Continue Learning -> Due Reviews -> From Your Media -> Your Courses -> Progress / secondary tools`

The page must feel like the home screen of a learning product rather than a technical dashboard.

## 2. Core hierarchy

### A. Continue Learning — primary hero

When an active structured course has resumable progress, this is the dominant first section.

Show:
- language pair/course name;
- optional compact language/course icon;
- current level;
- current chapter;
- current lesson;
- concise lesson title;
- course or chapter progress;
- optional last-activity context;
- primary **Continue** action.

Example information hierarchy:

`Japanese`  
`Chapter 3 · Lesson 2`  
`Particles in everyday sentences`  
`68%`  
`[ Continue ]`

Do not show curriculum version IDs, generator/provider names, internal unit IDs or AI provenance here.

### B. Today / Due Reviews

Second priority when Reviews capability is enabled.

Show only useful learner information:
- due review count;
- estimated session duration when it can be calculated credibly;
- optional count of new cards entering today's queue;
- primary **Start reviews** action.

Examples:
- `12 reviews due · about 5 min`
- `All caught up`

Do not expose FSRS retention, interval math, queue internals or scheduling diagnostics on Learning Home.

Those belong to advanced Learning settings / progress diagnostics.

### C. From Your Media

Personal media-derived learning, clearly separate from the structured course.

May show:
- recently saved words;
- new words encountered in media;
- saved sentences;
- media contexts worth revisiting;
- Continue practice from a canonical episode/chapter context.

Each item can include:
- media artwork thumbnail;
- Work title;
- episode/chapter context;
- source language;
- compact learning state/count.

Examples:
- `8 new words from Frieren`
- `3 saved sentences from Chapter 12`

Selecting the media context opens the relevant learning/practice surface or canonical source context.

Media learning must never be required to use structured courses.

### D. Your Courses

Compact course/language switcher.

Each course row/card shows:
- source -> target language;
- course name/variant only when useful;
- current level/chapter;
- compact progress;
- Continue / Start action.

Keep this section visually secondary to the active course hero.

If many courses exist:
- show a short current/recent set;
- use `View all courses` rather than an endless card grid.

### E. Progress snapshot

A small learner-readable summary, not a business dashboard.

May show:
- lessons completed;
- current streak only if Jularr actually defines one;
- active Learning words;
- mature/Known words;
- review consistency;
- current course completion.

Use at most a few meaningful metrics.

Do not use:
- giant KPI tiles;
- decorative charts without a learner action;
- leaderboard/gamification metrics that do not exist in the domain.

### F. Learning tools

Secondary navigation to enabled tools:
- Reviews;
- Vocabulary;
- Sentences;
- Script/Kana trainer when supported;
- Progress;
- Courses.

These are compact text/icon actions or a small tool row/list.

Do not turn them into the dominant top section.

## 3. Page header

Desktop/tablet:
- title `Learning`;
- active course/language switcher when useful;
- optional concise progress context;
- Settings shortcut only if it is genuinely useful and not visually dominant.

Mobile:
- compact app bar;
- `Learning`;
- current course selector via sheet/menu;
- no dense header metadata.

Do not duplicate the global profile/settings navigation.

## 4. Course switcher

The user can have multiple learning courses/language pairs.

Switcher shows:
- source language;
- target language;
- course/variant name only when needed to disambiguate;
- current progress;
- active/current marker.

Example:

`German -> Japanese`  
`German -> Indonesian`

Course variants such as Jularr Standard / Instance Standard / shared user variants can be selected in a dedicated course management flow. Learning Home should not expose a complex version-management UI.

Changing the active course changes:
- Continue Learning hero;
- course-specific progress;
- course-specific vocabulary/review context where applicable.

It must not delete/reset another course's progress.

## 5. Continue Learning behavior

Primary button resolution:

### Existing in-progress lesson
`Continue` resumes exact lesson/step state.

### Enrolled course, no started lesson
`Start course` opens the first appropriate lesson.

### Current lesson completed
Primary action resolves to the next available lesson/chapter checkpoint.

### Course temporarily unavailable
Show compact state + valid recovery action rather than a dead button.

Course updates/version changes must not silently reset progress.

## 6. Reviews behavior

Reviews and course completion are intentionally separate.

A learner may have:
- completed today's lesson but reviews due;
- no course lesson in progress but reviews due;
- no reviews due but a course lesson available.

The UI must not imply:
- lesson completed = all vocabulary mastered;
- Known/mature card = course lesson completed.

### Due Reviews card

When due:
- number due;
- estimated time;
- `Start reviews`.

When none due:
- quiet success state such as `You're caught up`;
- do not render a giant empty card.

When Reviews capability is off:
- hide the section entirely;
- historic review data must not force it back into the UI.

## 7. From Your Media behavior

This section is capability- and data-driven.

Possible sources:
- Player subtitle learning contexts;
- Reader selections;
- saved vocabulary;
- saved sentences;
- media-derived practice items.

Rules:
- media context points to canonical Work/Episode/Chapter;
- one saved word/sentence does not create a second media identity;
- repeated encounters can reference the same Learning unit;
- no raw provider/source IDs in normal UI;
- no indexer/download/storage information;
- if media learning is disabled, hide the section cleanly.

If there is no media-derived learning data:
- do not fill the page with an oversized empty placeholder;
- optionally show a compact hint after the core course/review content.

## 8. Course cards

Course cards are deliberately calmer than entertainment media cards.

Use:
- language/name;
- level/chapter;
- progress;
- current/paused/completed state;
- one primary action.

Avoid:
- achievement badge walls;
- many status chips;
- provider/version metadata;
- large decorative artwork that competes with learning information.

Cards may use a subtle language/illustration accent but must remain within the Jularr clean design system.

## 9. Structured course progress

Course progress can show:

`Level -> Chapter -> Lesson`

The UI should communicate:
- where the learner is;
- what is next;
- how much of the current course/section is complete.

It does not need to expose the entire curriculum tree on Learning Home.

A dedicated course detail page can show:
- all levels;
- chapters;
- lessons;
- completion states;
- checkpoints.

Learning Home links into that detail when the user wants more structure.

## 10. Learning tools / existing modules

Existing module concepts remain valid and capability-gated.

### Reviews
Only when `Reviews` is enabled.

### Vocabulary
Only when `Vocabulary` is enabled.

### Sentences
Only when `SentencePractice` is enabled.

### Kana / Script trainer
Only when:
- `ScriptTrainer` is enabled; and
- an enabled course/source-language toolkit supports it.

Do not show Kana as a universal language-learning concept.

### Progress
Only when `Progress` is enabled.

### Language Tools only mode

When Language Tools are available but study modules/courses are disabled:
- Learning Home becomes a quiet information surface;
- explain briefly that lookup/readings/translation are available in Player/Reader;
- do not show fake course/review sections.

## 11. New learner / no course state

A user with no enrolled course should immediately understand how to start.

Primary empty-state content:
- concise title;
- short explanation;
- `Choose a course` primary action.

Optional:
- suggested available language pairs;
- `Learning from media` entry if media-derived study is available.

Do not require the user to visit Settings to discover how to begin.

Do not show admin curriculum-generation controls.

## 12. Learning fully off state

If Learning is fully disabled for the profile:
- show a quiet disabled state;
- concise explanation;
- link to user Learning settings if the user has permission to change it.

Do not render disabled module cards.

If Learning exists only at lower scopes:
- explain that learning tools appear inside enabled media;
- do not pretend profile-wide Reviews/Course modules are available.

## 13. Loading state

Use section-level skeletons rather than blocking the entire shell.

Priority loading order:
1. active course / Continue Learning;
2. due reviews;
3. media learning;
4. courses;
5. progress/tools.

The page may become interactive progressively.

Avoid fake numbers or placeholder percentages.

## 14. Partial / degraded states

The core page must remain usable when optional dependencies fail.

### AI unavailable
- no global error;
- deterministic course/review/media learning remains usable;
- AI-specific enrich/explain actions simply disappear or show a local unavailable state.

### Dictionary / linguistic provider unavailable
Normal cached/local course and review flows continue.

### Media unavailable
Structured course/reviews remain usable.

### Course content partially unavailable
Show the affected course/lesson state only.

### TTS unavailable
Learning remains usable; listen controls show unavailable only where relevant.

Do not turn an optional provider failure into a full-page Learning outage.

## 15. Error state

Full-page error only when the Learning Home contract itself cannot load.

Show:
- concise learner-facing error;
- Retry;
- navigation remains available.

Section-specific failures should stay inside their section.

Never expose stack traces, provider tokens or internal curriculum IDs.

## 16. Offline state

If deterministic/cached course content and review state are available offline:
- Continue Learning remains available where local content permits;
- reviews may run using locally available queue/state if supported by the offline contract;
- queued learner state syncs later.

If a requested lesson is not offline:
- explain `Not available offline`;
- allow choosing locally available course/review content.

Do not pretend cloud/AI-generated actions work offline.

## 17. Desktop composition

Desktop baseline uses the standard Consumer shell.

Recommended structure:

```text
Learning
[active course switcher]

[ Continue Learning — wide primary card        ]
[ lesson/chapter/progress              Continue ]

[ Due Reviews ]      [ From Your Media          ]
[ 12 · ~5 min ]      [ compact media contexts   ]

Your Courses
[ course ] [ course ] [ course ]

Progress
[ compact meaningful summary ]

Tools
Reviews · Vocabulary · Sentences · Kana · Progress
```

Rules:
- Continue card spans the primary content width;
- secondary sections may use two columns;
- no 3x4 dashboard tile wall;
- max content width keeps hierarchy controlled;
- cards have calm Fluent-style surfaces.

## 18. Tablet composition

Touch-first.

Landscape:
- similar hierarchy to Desktop;
- Due Reviews and Media may sit side by side;
- course cards can use 2 columns.

Portrait:
- mostly one-column;
- Continue + Reviews stay first;
- larger touch targets;
- no hover-only actions.

## 19. Mobile composition

One-column priority:

1. Continue Learning
2. Due Reviews
3. From Your Media
4. Your Courses
5. Progress
6. Tools

Rules:
- primary Continue button is thumb-friendly;
- course switcher uses sheet/menu;
- cards are compact;
- no horizontal metric tables;
- no dense charts;
- media contexts can use compact horizontal cards if they remain readable;
- bottom navigation remains visible.

## 20. TV composition

Learning is present on TV only where interaction is meaningful.

TV Learning Home may show:
- Continue Learning;
- Due Reviews only if a remote-friendly review mode exists;
- current courses;
- media-derived listening/browse contexts.

Detailed writing/text-entry-heavy exercises should offer a handoff to phone/tablet rather than forcing poor TV UX.

Focus:
- strong ring/glow;
- predictable D-pad order;
- large targets;
- no hover assumptions.

A TV Learning screen is not required to expose every Desktop tool.

## 21. Light / Dark

Both first-class.

Light:
- clean neutral surfaces;
- subtle borders/elevation;
- selective accent on primary course progress/actions.

Dark:
- deliberately designed dark neutral cards;
- no glowing dashboard aesthetic;
- progress remains readable without oversaturated color.

Use accent primarily for:
- primary Continue action;
- selected course;
- progress fill;
- focused/active controls.

Do not assign a different bright color to every module.

## 22. Progress visualization

Preferred:
- simple horizontal progress bar;
- compact fraction/percentage;
- small chapter/lesson completion indicator.

Optional:
- one restrained progress visualization when it communicates something actionable.

Avoid:
- ring-chart collections;
- radar charts;
- large analytics graphs;
- arbitrary streak heatmaps unless the underlying product deliberately defines them.

Detailed analytics belong on Progress, not Learning Home.

## 23. Accessibility

Required:
- keyboard navigation on Desktop;
- visible focus;
- correct heading hierarchy;
- accessible progress labels;
- no color-only completion state;
- >=44 px touch targets on touch devices;
- screen-reader names for icon-only actions;
- reduced-motion support;
- TV focus independent from animation.

## 24. Data / information contract

Learning Home consumes view data derived from:
- active/enrolled LearnerCourse;
- course/version identity;
- current Level/Chapter/Lesson;
- exact learner course progress;
- due review count/estimate;
- existing LearningCard/FSRS scheduling state;
- enabled Learning capabilities;
- recent LearningContext records linked to canonical media;
- saved vocabulary/sentences;
- available course/language pairs;
- compact learner progress metrics;
- optional offline availability.

It does not consume:
- admin curriculum editing state;
- raw provider imports;
- model/API secrets;
- media download/indexer state;
- duplicated Work/Episode/Chapter identities.

## 25. Required mockup set

Create in this order:

1. **Desktop Light — active learner / normal state**  
   Continue Learning + Due Reviews + From Your Media + Courses.

2. **Mobile Light — active learner**  
   Validate one-column hierarchy and touch targets.

3. **Desktop Light — new learner / no course**  
   Validate course discovery/start path.

4. **Desktop Light — reviews caught up / sparse state**  
   Ensure the page does not become empty or tile-heavy.

5. **Mobile Light — course switcher sheet**  
   Multiple language pairs/courses.

6. **Desktop Dark — normal state**  
   Validate progress/card contrast.

7. **Partial/degraded state**  
   AI/provider unavailable while core learning remains functional.

8. **TV reference only if remote-friendly Learning scope is approved.**

Do not create decorative course-detail variants before this hierarchy is approved.

## 26. Must not implement / copy

- No technical module-dashboard as the primary Learning Home.
- No admin curriculum editor on learner pages.
- No second SRS/FSRS engine.
- No second vocabulary/term store.
- No duplicate media identity for learning contexts.
- No requirement that a user own/watch/read media before structured learning works.
- No AI requirement for course creation or normal study.
- No AI/provider health dashboard in Consumer UI.
- No FSRS tuning controls on Learning Home.
- No giant dashboard KPI tile wall.
- No leaderboard/XP/streak mechanics unless separately specified.
- No Japanese-only architecture hidden behind generic labels.
- No universal Kana tile for non-Japanese courses.
- No course update that silently resets learner progress.
- No historic review data bypassing capability rules.
- No Learning module shown when its capability is disabled.
- No raw curriculum version/provider/model IDs in normal learner UI.
- No direct mutation of canonical media from learning flows.
- No copying Duolingo-style gamification or another product's visual identity; use Jularr Clean Design.

## 27. Acceptance checklist

A Learning Home mockup is acceptable only when:
- Continue Learning is clearly the primary action for an active learner;
- due reviews are immediately understandable;
- structured course progress and SRS mastery are visibly different concepts;
- media-derived learning is useful but secondary;
- a user with no media can still begin a course;
- a new learner knows how to choose/start a course without entering Settings;
- existing tools remain discoverable without dominating the page;
- capability-disabled modules disappear cleanly;
- optional AI/provider failures do not break the core page;
- Desktop/Mobile/Tablet hierarchy is intentional;
- Light/Dark are both designed;
- no admin/curriculum internals leak into learner UX;
- no duplicate media, vocabulary or scheduling model is implied.
