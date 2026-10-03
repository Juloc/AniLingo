# Learning Home — V1 Learner Dashboard

Status: **binding V1 planning specification for Learning Home mockups**. This intentionally limits implementation scope. Jularr Learning may grow substantially later, but V1 must be coherent and shippable without pulling future multi-year features into the first delivery.

## 1. Product boundary

Learning is an optional Jularr product area.

An instance owner must be able to disable Learning completely when Jularr is used mainly as a media/request/Arr-stack replacement.

When the instance enables Learning, each permitted profile can also turn the **Learning module On or Off for itself** from User Settings → Modules & Features.

Precedence:
`instance Learning -> authorization -> profile Learning module preference -> detailed Learning capabilities/settings`.

When Learning is unavailable at either hard gate:
- Learning navigation/dashboard/widgets disappear for the affected scope/profile;
- Learning-specific Player/Reader actions disappear;
- profile-specific Learning jobs/notifications/sync do not run unnecessarily;
- stored courses, cards, progress and settings are preserved.

A personal Off never disables shared instance Learning data/work needed by other users.

V1 includes:
- Learning Dashboard;
- structured courses;
- Lessons;
- Reviews using the existing FSRS/SRS domain;
- Vocabulary;
- Sentences;
- Script/Kana where the language supports it;
- course progress;
- XP;
- Streak;
- combined Daily Goal;
- Achievements;
- basic learning statistics;
- media-derived learning from Player/Reader;
- Light/Dark and responsive layouts.

V1 does **not** require:
- social/community courses;
- public leaderboards;
- certificates;
- a full AI tutor;
- speaking/writing assessment;
- adaptive curriculum generation;
- complex placement/exam systems;
- offline AI.

Those remain future architecture considerations, not current implementation requirements.

## 2. Core model rules

- Course progress and SRS/FSRS mastery are separate.
- XP/gamification never replaces real course or review state.
- XP never unlocks required curriculum content; curriculum prerequisites do.
- Existing `LearningCard` review history remains the scheduling source of truth.
- Media-derived learning reuses canonical Learning units and canonical media context.
- No duplicate vocabulary, SRS, media or progress stores.
- AI is optional and must not be required for normal V1 learning.
- Capability resolution controls which modules/actions exist.
- Gamification can be disabled independently without breaking courses/reviews.

## 3. Dashboard purpose

Learning Home is a real learner dashboard. It answers:
1. What should I do today?
2. What lesson should I continue?
3. What reviews are due?
4. How am I doing against my Daily Goal?
5. What useful learning came from my media?
6. What courses and secondary tools are available?

The dashboard may be motivational and statistical, but must not become an admin analytics page.

## 4. Desktop V1 hierarchy

Recommended structure:

```text
Learning                                  [Course switcher]

[ Today / Daily Goal                                      ]
[ 42 / 60 XP ] [ 18 min ] [ 7 day streak ] [ Continue ]

[ Continue Learning — current course/lesson              ]
[ Chapter 3 · Lesson 2 · 68%                  Continue   ]

[ Due Reviews ]                [ Daily Plan               ]
[ 12 · ~5 min ]                [ ✓ Reviews 12/12         ]
[ Start Reviews ]              [ ○ Continue Lesson 4     ]
                                [ ○ 5 media words          ]

[ From Your Media                                     ]
[ context ] [ context ] [ context ]

Your Courses
[ course ] [ course ] [ course ]

[ Statistics summary ]          [ Achievements summary ]

Tools
Reviews · Vocabulary · Sentences · Kana · Progress
```

Disabled/empty modules collapse cleanly. No section exists merely to fill space.

## 5. Today / Daily Goal

The top dashboard area combines:
- Daily Goal progress;
- XP today;
- learning time today;
- current Streak;
- primary next action.

### Combined Daily Goal

The user may configure a preferred target using combinations of:
- XP;
- learning minutes;
- meaningful task/session completion.

Jularr can expose one normalized Daily Goal completion state while retaining the component metrics.

Example:
- 42 / 60 XP;
- 18 / 20 minutes;
- Reviews complete;
- Lesson still open.

### Streak rule

A day counts toward the Streak when the configured **Daily Goal is completed**.

Opening Learning or performing a trivial action does not complete a day.

Show:
- current streak;
- today complete/incomplete;
- compact weekly/calendar indication where useful.

Streak Freeze may exist only with a clear acquisition/use rule. Do not create a virtual currency/shop merely to support it.

## 6. XP

XP is a motivational layer.

V1 XP sources:
- meaningful Lesson exercises/steps;
- Lesson completion;
- Reviews;
- Vocabulary practice;
- Sentence practice;
- Script/Kana practice;
- meaningful media-learning exercises/interactions.

No XP for passive navigation.

### XP balancing

Prevent trivial farming. Award may consider:
- exercise type/difficulty;
- first attempt/correctness;
- new material vs repeated easy content;
- completion;
- small accuracy/perfect-session bonuses.

The exact formula remains an implementation/domain policy, not a prominent UI explanation.

### XP display

Show:
- unobtrusive +XP feedback after meaningful actions;
- daily XP total;
- Session Summary XP;
- progress toward visible global/per-language XP level when enabled.

V1 has no XP store/currency economy.

## 7. Levels

Architecture keeps these concepts separate:
- global Learning Account Level;
- per-language XP Level;
- course progress;
- future real proficiency/mastery.

V1 may visibly implement:
- global XP level;
- per-language XP level.

These are gamification indicators only and must never be labeled as CEFR/JLPT competence.

## 8. Continue Learning

Primary course card shows:
- language pair/course;
- Level/Chapter/Lesson;
- lesson title;
- exact resume state where useful;
- progress;
- Continue.

States:
- Continue current lesson;
- Start next lesson;
- Start course;
- course temporarily unavailable.

Course updates never silently reset learner progress.

## 9. Due Reviews

When due:
- due count;
- estimated duration;
- optional queued new items;
- **Start Reviews**.

When none:
- compact `All caught up` state.

No FSRS interval math/tuning on the dashboard.

Course completion and Review mastery remain visually separate.

## 10. Daily Plan

V1 Daily Plan is deterministic and simple, not an advanced AI scheduler.

It can combine:
- due reviews;
- current/next course Lesson;
- Vocabulary/Sentence practice;
- small media-learning target.

Example:

```text
Today
✓ 12 reviews
○ Continue Lesson 4
○ Practice 5 media words
```

Rules:
- generated from existing state;
- user may reorder/skip non-required items;
- no AI dependency;
- future adaptive planning may improve recommendations without changing the dashboard contract.

## 11. From Your Media

Media Learning is first-class V1 but not required for structured course use.

Sources:
- Player subtitle learning interactions;
- Reader selections;
- saved vocabulary;
- saved sentences;
- canonical LearningContext entries.

Each compact context can show:
- artwork/cover;
- Work title;
- episode/chapter;
- number/type of useful learning items;
- Continue Practice.

Examples:
- `8 words from Frieren · Episode 5`
- `3 saved sentences · Chapter 12`

No technical media/import/provider information.

## 12. Your Courses

Course cards show:
- language pair;
- course title if useful;
- current chapter/lesson;
- compact progress;
- Continue/Start.

A separate Course Detail page owns the complete `Level -> Chapter -> Lesson` tree.

Switching active course changes the dashboard context but never deletes another course's progress.

## 13. Achievements

V1 supports achievements.

Categories may include:
- Courses/Lessons;
- Reviews;
- Vocabulary;
- Sentences;
- Script/Kana;
- Media Learning;
- XP;
- Streak;
- learning time.

Dashboard shows only:
- recent achievement;
- next/relevant achievement progress;
- link to full Achievements page.

Allow both major milestones and smaller motivational achievements, but avoid launching with hundreds of meaningless badges.

Achievements never gate curriculum.

## 14. Basic statistics

Dashboard summary may show:
- XP today/week;
- learning time;
- Lessons completed;
- Reviews completed;
- accuracy;
- active/known words;
- current streak.

Detailed Progress/Stats may later show richer charts/heatmaps. Learning Home stays concise.

## 15. Learning tools

Secondary navigation:
- Reviews;
- Vocabulary;
- Sentences;
- Script/Kana if supported;
- Progress;
- Courses;
- Achievements.

Visibility remains capability-driven.

## 16. New learner

No course/enrollment:
- clear **Choose a course** primary action;
- short explanation;
- available language pairs/courses;
- optional Media Learning entry if applicable.

The user must not need Settings to discover how to start.

## 17. Learning disabled / gamification disabled

### Learning disabled
If instance policy disables Learning completely:
- hide Learning nav for everyone;
- hide dashboard/widgets;
- stop instance/profile Learning work according to the module gate;
- do not render dead module cards.

If the instance enables Learning but the current profile turns its Learning module Off:
- hide Learning nav/dashboard/widgets for that profile;
- hide Player/Reader Learning entry points for that profile;
- preserve all Learning state;
- expose the personal Learning toggle only through User Settings → Modules & Features so it can be turned back On.

### Gamification disabled
Courses, Lessons, Reviews, Vocabulary, Sentences and Media Learning continue normally.

Hide:
- XP;
- levels;
- streak;
- achievements;
- gamified Daily Goal presentation.

Learning remains fully functional.

## 18. Partial/degraded behavior

AI unavailable:
- no global error;
- core V1 unaffected.

TTS unavailable:
- only Listen actions affected.

Media unavailable:
- courses/reviews remain usable.

Course content unavailable:
- affected course/lesson state only.

Dictionary/provider unavailable:
- cached/local functionality continues where possible.

## 19. Mobile

Priority:
1. Today / Daily Goal;
2. Continue Learning;
3. Due Reviews;
4. Daily Plan;
5. From Your Media;
6. Courses;
7. Stats/Achievements;
8. Tools.

Rules:
- single column;
- large Continue/Start Reviews actions;
- course switcher via sheet;
- compact statistics;
- no desktop dashboard grid squeezed onto phone;
- bottom navigation remains visible.

## 20. Tablet

Portrait follows Mobile with wider cards.

Landscape may use:
- Due Reviews + Daily Plan side-by-side;
- Stats + Achievements side-by-side;
- two-column Courses.

Touch remains primary.

## 21. TV

TV is secondary.

V1 only needs a Learning TV surface where a remote-friendly exercise subset is actually supported.

Possible:
- Continue Learning;
- Daily Goal/Streak;
- Due Reviews;
- media listening/practice.

Text-entry-heavy tasks hand off to phone/tablet/web.

## 22. Light / Dark

Both first-class.

Accent is used for:
- Daily Goal;
- Continue;
- XP/level progress;
- selected course;
- active/focus states.

Avoid rainbow module coloring and neon game-dashboard styling.

## 23. Loading / empty / error

Loading:
- section skeletons;
- no fake XP/counts.

Empty:
- contextual and compact.

Error:
- section-level where possible;
- full page only if dashboard bootstrap fails;
- Retry;
- no raw exceptions.

## 24. Required mockups

Create in this order:
1. **Desktop Light — full V1 Learning Dashboard**
2. **Mobile Light — full V1 Learning Dashboard**
3. **Desktop Light — new learner**
4. **Desktop Light — gamification disabled**
5. **Mobile — course switcher / Daily Plan state**
6. **Desktop Dark — full dashboard**
7. **Sparse state — no reviews / little media learning**
8. **Partial provider/TTS failure reference**

## 25. Future-proofing, not V1 scope

Architecture must not block later:
- Placement Tests;
- skill mastery;
- adaptive Daily Plan;
- Remedial Lessons;
- Exams;
- Speaking/Writing;
- AI Tutor;
- deep Media comprehension analysis;
- Challenges;
- advanced statistics/heatmaps/radar;
- social/leaderboards;
- community courses;
- certificates;
- offline AI.

Do not implement these merely because they are listed.

## 26. Must not implement in V1

- No second SRS/FSRS scheduler.
- No second vocabulary store.
- No duplicate media identities.
- No AI requirement for normal learning.
- No social network.
- No public leaderboard.
- No community course marketplace.
- No certificate system.
- No complex virtual currency/shop.
- No XP-based curriculum gating.
- No claim that XP level equals CEFR/JLPT.
- No admin curriculum controls on Learning Home.
- No provider/model health dashboard in learner UI.
- No giant analytics wall.
- No Japanese-only assumptions in generic course architecture.

## 27. Acceptance

V1 Learning Home is complete only when:
- Learning can be disabled cleanly at instance level;
- active learner immediately sees Daily Goal, Continue and Reviews;
- XP/Streak/Achievements motivate without becoming curriculum truth;
- Daily Plan works without AI;
- course progress and FSRS mastery remain separate;
- media-derived learning is integrated but optional;
- new user can start without media;
- gamification can be disabled without breaking Learning;
- Desktop/Mobile/Tablet are intentionally designed;
- Light/Dark work;
- optional service failures degrade locally;
- future advanced Learning remains possible without being implemented now.
