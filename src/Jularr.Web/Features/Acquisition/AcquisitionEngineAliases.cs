// Transitional aliases. The acquisition release/scoring/quality engine is now media-type-agnostic
// (Jularr.Web.Features.Acquisition.Release / .Quality), with anime as one registration. Existing
// callers still refer to the historical anime-prefixed type names; these compile-time aliases keep
// them working while the canonical types are generic. There is a single source of truth per type —
// an alias is not a duplicate type. Consumers are renamed off these aliases in a follow-up.

global using AnimeReleaseParser = Jularr.Web.Features.Acquisition.Release.ReleaseParser;
global using AnimeReleaseInfo = Jularr.Web.Features.Acquisition.Release.ReleaseInfo;
global using AnimeReleaseEvidence = Jularr.Web.Features.Acquisition.Release.ReleaseEvidence;
global using AnimeReleaseSource = Jularr.Web.Features.Acquisition.Release.ReleaseSource;
global using AnimeVideoCodec = Jularr.Web.Features.Acquisition.Release.ReleaseVideoCodec;
global using AnimeAudioCodec = Jularr.Web.Features.Acquisition.Release.ReleaseAudioCodec;
global using AnimeHdrFormat = Jularr.Web.Features.Acquisition.Release.ReleaseHdrFormat;

global using AnimeReleaseScorer = Jularr.Web.Features.Acquisition.Quality.ReleaseScorer;
global using AnimeReleaseQuality = Jularr.Web.Features.Acquisition.Quality.ReleaseQuality;
global using AnimeQualityProfile = Jularr.Web.Features.Acquisition.Quality.QualityProfile;
global using AnimeReleaseCandidate = Jularr.Web.Features.Acquisition.Quality.ReleaseCandidate;
global using AnimeReleaseScoreResult = Jularr.Web.Features.Acquisition.Quality.ReleaseScoreResult;
global using AnimeReleaseScoreRule = Jularr.Web.Features.Acquisition.Quality.ReleaseScoreRule;
global using AnimeReleaseRuleField = Jularr.Web.Features.Acquisition.Quality.ReleaseRuleField;
global using AnimeReleaseRuleMatch = Jularr.Web.Features.Acquisition.Quality.ReleaseRuleMatch;
global using AnimeQualityProfileStore = Jularr.Web.Features.Acquisition.Quality.QualityProfileStore;
