# Learning Progress & Achievements — V1

Status: **binding V1 planning specification**. The approved Original Jularr Desktop + Mobile mockup is the visual reference. Text wins over imagery where metrics, persistence or behavior differ.

Binding data/activity contract: `docs/LEARNING_GAMIFICATION.md`.
Binding course-progress contract: `docs/LEARNING_PROGRESS.md`.

Global UX: `docs/UX.md`  
Learning Home: `docs/mockups/learning-home/SPEC.md`  
Course Detail: `docs/mockups/course-detail/SPEC.md`  
Vocabulary & Sentences: `docs/mockups/vocabulary-sentences/SPEC.md`  
Lesson / Review: `docs/mockups/lesson-review/SPEC.md`  
Original J mascot: `docs/assets/original-j/jularr-mascot-reference.png`

## 1. Purpose

Progress is the learner's long-term Learning overview.

It answers:
- how much meaningful learning have I done?
- how is my activity changing over time?
- how far am I through my courses?
- what is my Vocabulary/Review state?
- which achievements have I earned or nearly earned?

It is not:
- an Admin analytics dashboard;
- an FSRS tuning screen;
- a provider-health page;
- a social leaderboard.

## 2. Product boundary

Progress and Achievements share one coherent surface.

Baseline tabs/sections may include:
- Overview;
- Courses;
- Vocabulary;
- Reviews;
- Achievements.

They are projections over the same canonical Learning data. Do not create separate progress truth for each chart.

The existing `/Learn/Progress` route remains the canonical entry/deep link. A future dedicated Achievements route may deep-link into the Achievements section without duplicating persistence.

## 3. Canonical metric sources

Each visible metric must come from canonical durable state or a reproducible aggregation over canonical events/state.

Examples:
- known/learning/due word counts -> LearningCards/Learning queries;
- Review counts/ratings/accuracy -> LearningCardReviews;
- Course completion -> learner curriculum/Lesson progress;
- media-derived context counts -> LearningContext;
- XP -> `LearningActivityEvent.XpAwarded` once the planned activity contract is implemented;
- learning time -> `LearningActivitySession` / `LearningSessionTimeSlice` once implemented;
- Streak -> completed `LearningDailyGoal` history once implemented;
- Achievements -> deterministic definitions + `LearningAchievementUnlock`; progress is derived rather than copied.

### Current implementation bridge

The existing `LearningStatisticsService` / `LearningStatisticsSnapshot` is an interim statistics projection and currently covers only a subset such as:
- KnownTerms;
- LearningTerms;
- DueReviews;
- total/recent Reviews;
- prepared media occurrences.

The approved mockup is the **target UX**, not permission to invent missing values in the UI.

The target durable contracts are now defined in `docs/LEARNING_PROGRESS.md` and `docs/LEARNING_GAMIFICATION.md`, but they are not automatically implemented by this mockup spec. Until each source exists in production:
- omit unavailable cards/sections;
- or show an explicit unavailable/not-yet-implemented state during implementation;
- never generate fake/demo values in production.

## 4. Language/course scope

The top scope selector can show:
- All Learning;
- a language direction, e.g. `German -> Japanese`;
- optionally a specific course where a section supports it.

Language direction comes from real LearningCourse source/target tags and is never inferred from UI locale.

Changing scope updates all compatible cards/charts beneath it consistently.

Do not mix a per-language heading with global totals without labeling the scope.

## 5. Approved Desktop hierarchy

Wide Desktop:

```text
<- Learning

Learning Progress
[ scope: German -> Japanese ]

[ XP ] [ Learning time ] [ Vocabulary ] [ Lessons ] [ Streak ] [ Review accuracy ]

[ Learning Activity --------------------- ] [ Review Distribution -------- ]
[ 7d | 30d | 90d | All                  ] [ Again / Hard / Good / Easy    ]
[ compact time-series chart              ] [ compact donut/summary         ]

[ Course Progress ----------------------- ] [ Vocabulary Status ------------ ]
[ course rows + progress                 ] [ Learning / Known / Due / Saved]

[ Achievements ------------------------------------------------------------ ]
[ achievement ][ achievement ][ achievement ][ next progress ...            ]
```

Rules:
- overview metrics remain one compact row/grid;
- only two meaningful charts above the fold;
- Course/Vocabulary status follows;
- Achievements stays motivational but secondary;
- no endless analytics card wall.

## 6. Overview metrics

Approved V1 target metrics:
- XP total/period when canonical XP exists;
- Learning time when canonical duration exists;
- learned/known Vocabulary;
- completed Lessons;
- current Streak when Daily Goal/Streak contract exists;
- Review accuracy for the selected period.

Each metric card:
- one number;
- one concise label;
- optional small delta only when derived from real comparable periods.

Do not show decorative trend arrows from insufficient data.

### Gamification disabled

If profile gamification is Off:
- hide XP;
- hide Streak;
- hide Achievements;
- keep Course, Vocabulary, Review and learning-time statistics that remain meaningful.

Layout closes gaps instead of rendering empty disabled cards.

## 7. Time range

Activity/Review charts may offer:
- 7 days;
- 30 days;
- 90 days;
- All.

Rules:
- selected range applies consistently to time-dependent metrics/charts;
- totals explicitly labeled `all-time` ignore the range only when the label makes that clear;
- all aggregation uses profile-local day boundaries resolved from the profile/instance timezone policy;
- raw UTC storage is normalized before daily grouping.

No accidental server-timezone streaks.

## 8. Learning Activity chart

One compact chart may combine meaningful activity series such as:
- Lessons completed;
- Reviews completed;
- new/started Vocabulary;
- learning minutes.

Only render series backed by real events.

Rules:
- legend can hide/toggle series if useful;
- chart remains readable with one series;
- zero-activity days are represented honestly;
- no synthetic interpolation;
- reduced-motion accessible;
- chart has a textual/tabular summary for screen readers.

For very long `All` ranges, aggregate by week/month rather than rendering thousands of daily bars.

## 9. Review distribution

Show a concise breakdown of actual `LearningCardReview` ratings:
- Again;
- Hard;
- Good;
- Easy.

May show:
- count;
- percentage;
- total reviewed in the selected period.

Accuracy is a learner-facing summary metric, not an FSRS parameter.

If an accuracy formula is used, define it once in the application/statistics contract and reuse it everywhere. Do not let each chart calculate its own interpretation.

No raw FSRS stability/difficulty formulas in the normal Progress UI.

## 10. Course progress

Course section shows a bounded list of courses with:
- course title;
- source -> target;
- completed / total required Lessons;
- progress percentage;
- open Course Detail.

Course progress is curriculum completion only.

It does not fall when:
- Reviews become due;
- memory retention decays;
- a Review is rated Again.

See `docs/mockups/course-detail/SPEC.md`.

## 11. Vocabulary status

Show canonical word/card lifecycle summaries such as:
- Learning;
- Known;
- Due now;
- Saved.

Optional:
- Suspended;
- Ignored in a deeper/filter view rather than a prominent positive metric.

Counts must use the same Recognition-card/unit semantics as Vocabulary.

Selecting a status opens Vocabulary with the corresponding filter when permitted.

Do not sum directional cards and call that `words` if one LearningUnit owns multiple review modes.

## 12. Media Learning statistics

Media-derived learning may expose compact metrics where useful:
- contexts captured;
- media-linked Vocabulary;
- media-linked practice completed.

Do not create separate media-learning XP/card truth.

Media stats derive from canonical LearningContext + normal Learning state.

A dedicated giant `Anime learning analytics` panel is not required in V1.

## 13. Achievements

V1 achievements are deterministic milestones over canonical Learning state/events.

Possible categories:
- Lessons/Courses;
- Reviews;
- Vocabulary;
- Sentences;
- Script/Kana;
- Media Learning;
- XP;
- Streak;
- learning time.

Each Achievement definition needs:
- stable ID;
- localized title/description;
- category;
- deterministic criterion/version;
- threshold(s);
- visual asset/icon;
- whether progress is countable before unlock.

Profile achievement persistence stores only:
- profile ID;
- achievement definition key/version;
- unlocked timestamp.

Achievement progress is derived from canonical Learning activity/state in V1; do not create a mutable badge-progress store.

### Rules

Achievements:
- never unlock curriculum;
- never alter FSRS;
- never grant required functional permissions;
- are not currency;
- are not social ranking.

Avoid hundreds of trivial badges in V1.

## 14. Achievement cards

Dashboard/Progress can show:
- recently unlocked;
- nearest meaningful incomplete;
- major completed milestones.

Card may contain:
- icon;
- title;
- short criterion;
- progress, e.g. `84 / 100`;
- completed check;
- unlock date when useful.

Completed cards remain visibly complete without giant celebration effects.

A full Achievements view may group by category and completed/incomplete state using the same definitions.

## 15. Achievement evaluation

Achievement evaluation must be idempotent.

Preferred model:
- meaningful Learning events/state changes trigger/requeue achievement evaluation;
- evaluator checks canonical data;
- unique profile + achievement/version constraint prevents duplicate unlock;
- historical rebuild/reconciliation can safely recompute unlocks where possible.

Do not place achievement business rules in Razor/UI components.

## 16. Daily Goal / Streak

Streak remains defined by Learning Home:
- a day counts only when configured Daily Goal completes.

Progress may show:
- current Streak;
- longest Streak if canonical history supports it;
- compact calendar/heatmap later.

Do not infer a streak from `any activity today`.

If the profile disables gamification:
- no Streak card/achievement presentation;
- underlying historical events may be retained for re-enable.

## 17. XP

XP is motivational activity accounting, separate from curriculum and FSRS.

Progress may show:
- total XP;
- XP in selected period;
- per-language XP where canonical model supports it.

No XP currency/storefront semantics.

No course completion or review rating is inferred from XP.

## 18. Learning time

Learning time must reflect meaningful active Learning sessions, not page-open wall clock time.

Count only explicitly defined activity/session intervals such as:
- active Lesson time;
- active Review time;
- active Vocabulary/Sentence/Script practice time.

Rules:
- ignore background/hidden idle time after a bounded inactivity threshold;
- avoid double-counting overlapping session surfaces;
- use the canonical activity/session contract in `docs/LEARNING_GAMIFICATION.md`;
- Player/Reader consumption time is not automatically Learning time merely because Learning is enabled.

## 19. Original J visual direction

Approved Original Jularr reference:
- warm cream/paper surfaces;
- scenic sakura/temple/Fuji-style header;
- red/pink primary accent;
- restrained category colors for metrics where semantics require distinction;
- canonical mascot as the header companion.

Canonical mascot:
`docs/assets/original-j/jularr-mascot-reference.png`

The mascot:
- uses a calm/proud/encouraging pose;
- may display one short encouragement bubble;
- does not cover metrics/chart labels;
- is not repeated throughout every statistics card.

Charts themselves remain clean and legible; do not texture the plotting area with decorative art.

Clean uses identical layout/data/actions with neutral surfaces and purple accent, without the Original J mascot by default.

## 20. Chart color semantics

The approved mockup uses multiple colors for Review ratings and Vocabulary states.

These colors are semantic data-series distinctions, not arbitrary module decoration.

Requirements:
- labels/legend always accompany color;
- sufficient contrast;
- same series/state uses the same semantic token throughout this screen;
- colorblind-safe differentiation also uses text/order/shape where needed;
- user accent color does not blindly recolor Again/Hard/Good/Easy into indistinguishable values.

## 21. Mobile approved composition

Mobile keeps the same information priority:

```text
Learning Progress
[ scope selector ]

[ compact metric grid ]

Learning Activity
[ range ]
[ compact chart ]

Course Progress
Vocabulary Status
Achievements
[ Review distribution/details when space/order makes sense ]
```

Rules:
- one column;
- 2-column compact metric cards where readable;
- charts use responsive simplified axes/labels;
- horizontal overflow is avoided;
- Review distribution can become a list/compact chart rather than shrinking a Desktop donut;
- Achievement cards use a short horizontal row or compact grid;
- tap opens deeper section/detail;
- no Desktop sidebar/table squeezed into phone.

## 22. Tablet

Portrait follows Mobile with wider cards.

Landscape may use:
- metric grid;
- Activity + Review distribution side by side;
- Course + Vocabulary side by side;
- Achievements full width.

Touch remains primary.

## 23. TV

Full analytical Progress is not required for TV V1.

If exposed:
- headline metrics;
- Streak/Daily Goal;
- Course progress;
- recent Achievements.

Detailed charts should hand off to phone/tablet/web rather than creating a dense TV analytics UI.

## 24. Empty / new learner

A new learner must not see a dashboard full of `0` cards.

Show:
- short `Start learning to build your progress`;
- primary Choose/Start Course;
- only metrics that are already meaningful;
- optional canonical mascot in Original J.

As activity exists, sections appear progressively.

## 25. Loading / partial / errors

Loading:
- metric/chart skeletons;
- no fake values.

Partial:
- one unavailable metric does not fail entire page;
- chart can omit a missing series;
- Achievements failure does not hide Course progress.

Errors:
- section-level Retry where safe;
- no raw SQL/FSRS/provider details.

## 26. Data retention / rebuild

Statistics should favor canonical events/state from which aggregates can be rebuilt.

Do not persist every chart bucket as independent truth unless performance requires a derived cache.

Derived caches:
- have explicit version/window keys;
- can be invalidated/rebuilt;
- never replace underlying Learning state/events.

Achievement unlock timestamps are durable user state once earned, even if later criteria/version changes require migration policy.

## 27. Privacy / social boundary

V1 Progress is private to the profile/account subject to normal account access.

No:
- public leaderboard;
- friend comparison;
- percentile ranking;
- public learning history.

Any future social feature requires separate privacy/visibility rules.

## 28. Required visual references

Approved:
1. **Desktop Original Jularr Light — Progress + Achievements**
2. **Mobile Original Jularr Light — Progress + Achievements**

Still useful:
3. gamification disabled;
4. new learner/sparse data;
5. 30/90-day dense chart;
6. Dark validation;
7. Clean parity;
8. Achievements full view if it becomes a distinct route.

The approved image is a visual reference; this text specification is authoritative.

## 29. Must not implement

- No fake XP/time/Streak/Achievement values when no canonical store exists.
- No second Review or Course progress store.
- No XP-derived curriculum/FSRS state.
- No `any activity = Streak day` shortcut.
- No per-chart independent definition of accuracy.
- No double-counted directional cards as Vocabulary words.
- No giant analytics wall.
- No raw FSRS internals in learner UI.
- No public leaderboard/social comparison in V1.
- No Achievement-based permissions or curriculum locks.
- No page-local achievement business rules.
- No theme-specific statistics behavior.
- No alternate Original J mascot identity.

## 30. Acceptance

Progress & Achievements is complete only when:
- every visible metric has a canonical/reproducible source;
- current interim statistics can evolve without becoming a second source of truth;
- Course, Vocabulary, Reviews and gamification remain semantically separate;
- time range and scope are consistent;
- charts remain bounded and readable;
- Achievement evaluation is deterministic/idempotent;
- gamification can disappear without breaking core Learning progress;
- new learners do not see meaningless zero walls;
- Desktop/Mobile/Tablet behavior is intentional;
- Original J and Clean remain skins over identical data/behavior.
