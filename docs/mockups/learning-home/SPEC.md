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

The approved Desktop reference is the **Original Jularr Light** mockup. Its information hierarchy is binding; the decorative Original J skin is not.

Wide Desktop uses a main column plus a compact right rail:

```text
Learning / themed header                         [active course/language]

[ Today's Progress / Daily Goal — wide main card              ]
[ goal progress | XP today | learning time | streak | Continue ]

MAIN COLUMN                                      RIGHT RAIL
[ Continue Learning                           ]  [ Learning Tools ]
[ current course/lesson + progress + Continue ]  [ Reviews         ]
                                                   [ Vocabulary      ]
[ Due Reviews ]          [ Daily Plan          ]  [ Sentences       ]
[ due + ~time + CTA ]    [ deterministic tasks ]  [ Kana / Script   ]
                                                   [ Progress        ]
[ From Your Media                            ]
[ compact media contexts                     ]  [ Statistics       ]
                                                [ compact metrics   ]
[ Your Courses                               ]
[ compact course cards                       ]  [ Achievements     ]
                                                [ recent/progress   ]
```

Layout rules:
- the **Today's Progress** card spans the content width above the two-column dashboard;
- **Continue Learning** is the dominant main-column card;
- **Due Reviews** and **Daily Plan** sit side by side below Continue on wide screens;
- **From Your Media** follows as one compact row/list;
- **Your Courses** follows below Media Learning;
- **Learning Tools**, **Statistics** and **Achievements** form the right rail on wide Desktop;
- the right rail is secondary and narrower than the learning flow;
- when width becomes insufficient, right-rail sections reflow below the main column in the same semantic order;
- disabled/empty modules collapse cleanly; no section exists merely to fill space.

The approved mockup's anime/book titles, thumbnails and character artwork are **sample content only**. They are not fixed product assets or required content.

## 5. Today / Daily Goal

The approved top card is **Today's Progress** / **Daily Goal**.

It combines:
- normalized Daily Goal progress;
- XP today;
- learning time today;
- current Streak;
- compact week/day completion state where useful;
- primary **Continue Learning** action.

The card may use an Original J themed illustration/background in Original Jularr, but the progress data and CTA must remain readable independently of that artwork.

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
- saved/tracked vocabulary;
- sentence practice/context candidates derived from canonical LearningContext;
- canonical LearningContext entries.

Binding Vocabulary & Sentences specification: `docs/mockups/vocabulary-sentences/SPEC.md`.

V1 does not assume a separate SavedSentence persistence model.

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

Binding Course Detail specification: `docs/mockups/course-detail/SPEC.md`.

Course Detail owns navigation over the canonical `Curriculum -> Level -> Chapter -> Lesson -> Exercise` hierarchy; Exercise remains inside Lesson.

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

Dashboard summary is deliberately compact and, on wide Desktop, belongs in the right rail.

It may show a small set such as:
- current streak;
- total/weekly learning time;
- words learned/active;
- Lessons completed;
- XP today/week;
- Review accuracy.

Do not show a full analytics chart on Learning Home. Detailed charts, heatmaps and trends belong to Progress/Stats.

## 15. Learning tools

Secondary navigation uses the canonical V1 tool set:
- Reviews;
- Vocabulary;
- Sentences;
- Script/Kana if supported;
- Progress.

Courses and Achievements have their own dashboard sections/routes and do not need duplicate tool buttons.

On wide Desktop, Learning Tools forms the top of the right rail. On narrower layouts it reflows below the primary learning sections.

Visibility remains capability-driven. Do not substitute unrelated modules such as generic Flashcards, Grammar, Dictionary or Notes as separate top-level Learning modules unless they are later specified as canonical product modules.

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

Mobile preserves the same semantic hierarchy while collapsing the Desktop right rail into the normal flow.

Priority:
1. Today's Progress / Daily Goal;
2. Continue Learning;
3. Due Reviews;
4. Daily Plan;
5. From Your Media;
6. Your Courses;
7. Learning Tools;
8. Statistics;
9. Achievements.

Rules:
- single column;
- large Continue/Start Reviews actions;
- active course/language switcher uses compact control or sheet;
- media/course rows may scroll horizontally where appropriate;
- statistics remain compact;
- no Desktop right rail or dashboard grid squeezed onto phone;
- bottom navigation remains visible;
- Original J decoration must not consume meaningful vertical space needed for learning actions.

## 20. Tablet

Portrait follows Mobile with wider cards.

Landscape may use:
- Today's Progress full width;
- Continue Learning full/main width;
- Due Reviews + Daily Plan side by side;
- a compact secondary column for Learning Tools or Stats only when width remains comfortable;
- two-column/compact Course cards.

Tablet does not blindly copy the full Desktop right rail when that would squeeze primary learning content.

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

## 22. Visual skins, Light / Dark

Jularr has two visual skins over the **same Learning Home structure**:
- **Clean** — neutral/minimal Fluent-style surfaces, purple default accent;
- **Original Jularr** — Japanese ink/watercolor/cherry-blossom surfaces, red/pink default accent.

The approved Learning Home visual reference for this specification is **Original Jularr Light**.

### Original Jularr binding visual direction

Canonical mascot reference when a mascot/companion character is used:
`docs/assets/original-j/jularr-mascot-reference.png`

The mascot is optional on Learning Home. If present, use the same canonical character as Lesson/Review and Error States; do not invent a different anime girl for the dashboard.

Use:
- warm off-white/paper surfaces;
- subtle ink/wash texture in page/background regions;
- Japanese landscape/torii/cherry-blossom decorative artwork in non-content background areas;
- red/pink semantic accent for primary actions/progress/focus;
- restrained petal/ink decoration around edges and section boundaries;
- media artwork inside actual media/course cards.

Do not:
- place decorative artwork behind dense body text without a controlled readable surface;
- let petals/ink overlap labels, inputs, progress or buttons;
- make every card ornamental;
- encode product state solely through red/pink decoration;
- hard-code third-party anime/franchise characters as permanent Jularr branding;
- replace the canonical Jularr mascot with a different Original J character when a mascot is intentionally used.

### Clean parity

Clean uses the exact same sections, actions, data and responsive hierarchy with neutral surfaces and purple accent. A theme switch must not move modules, change feature availability, alter data contracts or create a second Learning page implementation.

### Brightness

Light and Dark remain first-class for both skins. Dark Original J should reinterpret paper/ink safely rather than simply invert the Light mockup.

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

Approved/reference status:
1. **Desktop Original Jularr Light — full V1 Learning Dashboard** — **approved visual direction**
2. **Mobile Original Jularr Light — full V1 Learning Dashboard**
3. **Desktop Original Jularr Light — new learner**
4. **Desktop Original Jularr Light — gamification disabled**
5. **Mobile — course switcher / Daily Plan state**
6. **Desktop Original Jularr Dark — full dashboard**
7. **Clean parity reference** — same layout/data with neutral purple skin
8. **Sparse state — no reviews / little media learning**
9. **Partial provider/TTS failure reference**

The approved Desktop image is a visual aid. This text specification remains authoritative for behavior, data and responsive rules.

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
- No theme-specific Learning data model, route or page implementation.
- No fixed anime/franchise artwork baked into Learning Home as required content.
- No Original J decoration that changes section order or hides functionality available in Clean.

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
- the approved wide Desktop hierarchy uses Today's Progress, main learning column and compact right rail;
- Original J and Clean expose the same sections/actions/data;
- Original J decoration never reduces readability or changes product behavior;
- Light/Dark work for both visual skins;
- optional service failures degrade locally;
- future advanced Learning remains possible without being implemented now.
