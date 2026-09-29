// Transitional aliases mirroring src/Jularr.Web/Features/Acquisition/AcquisitionEngineAliases.cs so
// existing tests keep referring to the historical anime-prefixed type names while the canonical
// acquisition engine types are media-type-agnostic. Removed with the consumer rename follow-up.

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
